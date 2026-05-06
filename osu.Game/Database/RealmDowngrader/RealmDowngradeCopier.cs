// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Logging;
using Realms;
using Realms.Schema;

namespace osu.Game.Database.RealmDowngrader
{
    /// <summary>
    /// Copies all data from a v52 realm into a freshly-built v51 realm,
    /// dropping the <c>SkinInfo.Pinned</c> column in the process.
    ///
    /// Realm.NET API used
    /// ------------------
    /// Both source and destination realms are opened with explicit
    /// typed schemas (<c>SkinInfo</c> on source, <c>SkinInfoV51</c> on
    /// destination — same realm-class slot via <c>[MapTo("Skin")]</c>).
    /// All property reads and writes go through
    /// <see cref="IRealmObjectBase.DynamicApi"/>'s typed accessors —
    /// <see cref="DynamicObjectApi.Get{T}"/>,
    /// <see cref="DynamicObjectApi.Set"/>,
    /// <see cref="DynamicObjectApi.GetList{T}"/> — with
    /// <see cref="RealmValue"/> as the universal value type. We never
    /// touch typed C# properties of the typed instances, so adding /
    /// removing properties on a typed class doesn't ripple into here.
    ///
    /// Two-pass model
    /// --------------
    /// - Pass A: create destination skeletons (PK + primitives +
    ///   embedded objects copied recursively).
    /// - Pass B: link relationships (single object refs, ref lists,
    ///   ref sets) by primary-key lookup in the destination.
    ///
    /// Batched commits
    /// ---------------
    /// Each pass commits every <see cref="batch_size"/> objects so the
    /// destination realm's transaction log never accumulates more than
    /// one batch worth of writes. A crash mid-batch leaves the temp
    /// realm at the previous commit boundary, which the runner deletes
    /// on rollback.
    /// </summary>
    public sealed class RealmDowngradeCopier
    {
        public IReadOnlyDictionary<string, int> CopiedCounts => copiedCounts;

        private const int batch_size = 5000;

        private readonly Dictionary<string, int> copiedCounts = new Dictionary<string, int>();

        /// <summary>
        /// For top-level classes that have no primary key (BeatmapMetadata,
        /// RulesetSetting in osu!'s schema), we can't resolve a reference
        /// from source-object to dest-object via PK lookup. Instead we
        /// build a Dictionary keyed by source IRealmObjectBase (which
        /// Realm overrides Equals/GetHashCode on to use underlying row
        /// identity) for O(1) resolution in pass B. Without this, the
        /// pkless lookup degrades to O(n²) on large libraries
        /// (128k+ BeatmapMetadata × 128k+ Beatmaps = ~15 minutes
        /// without the dict; ~30 seconds with it).
        /// </summary>
        private readonly Dictionary<string, Dictionary<IRealmObjectBase, IRealmObjectBase>> pklessClassMaps
            = new Dictionary<string, Dictionary<IRealmObjectBase, IRealmObjectBase>>();

        public void Copy(Realm source, Realm destination)
        {
            var topLevelClasses = source.Schema
                                        .Where(s => s.BaseType == ObjectSchema.ObjectType.RealmObject)
                                        .Select(s => s.Name)
                                        .ToArray();

            foreach (string className in topLevelClasses)
                copyClassSkeletons(source, destination, className);

            foreach (string className in topLevelClasses)
                copyClassReferences(source, destination, className);
        }

        // ================================================================
        // Pass A
        // ================================================================
        private void copyClassSkeletons(Realm source, Realm destination, string className)
        {
            if (!source.Schema.TryFindObjectSchema(className, out ObjectSchema? sourceSchema) || sourceSchema == null)
                return;

            if (!destination.Schema.TryFindObjectSchema(className, out _))
            {
                Logger.Log($"[RealmDowngrade] Skipping class '{className}' — not in destination schema.", LoggingTarget.Database);
                return;
            }

            var sourceObjects = source.DynamicApi.All(className);
            int count = 0;
            int sinceLastCommit = 0;
            Transaction? tx = destination.BeginWrite();

            // If this class is pkless we need an identity map for pass
            // B's reference resolution. We use a Dictionary keyed by
            // the source IRealmObjectBase — Realm overrides Equals
            // and GetHashCode on managed objects to compare by
            // underlying row identity, so this gives O(1) lookup.
            bool classIsPkless = findPrimaryKey(sourceSchema) == null;
            Dictionary<IRealmObjectBase, IRealmObjectBase>? pklessMap = null;
            if (classIsPkless)
                pklessMap = new Dictionary<IRealmObjectBase, IRealmObjectBase>();

            try
            {
                foreach (var sourceItem in sourceObjects)
                {
                    var sourceObj = (IRealmObjectBase)sourceItem;
                    var destObj = createDestinationObject(destination, sourceSchema, sourceObj);

                    if (classIsPkless)
                        pklessMap![sourceObj] = destObj;

                    foreach (var prop in sourceSchema)
                    {
                        if (shouldSkipProperty(className, prop)) continue;
                        if (prop.IsPrimaryKey) continue;
                        if (isReferenceProperty(prop)) continue;
                        copyPrimitiveOrEmbedded(source, destination, sourceObj, destObj, prop);
                    }

                    count++;
                    sinceLastCommit++;

                    if (sinceLastCommit >= batch_size)
                    {
                        tx.Commit();
                        tx.Dispose();
                        sinceLastCommit = 0;
                        tx = destination.BeginWrite();
                    }
                }

                tx.Commit();
            }
            finally
            {
                tx?.Dispose();
            }

            if (classIsPkless)
                pklessClassMaps[className] = pklessMap!;

            copiedCounts[className] = count;
            Logger.Log($"[RealmDowngrade] Pass A: copied {count} '{className}' skeletons.", LoggingTarget.Database);
        }

        // ================================================================
        // Pass B
        // ================================================================
        private void copyClassReferences(Realm source, Realm destination, string className)
        {
            if (!source.Schema.TryFindObjectSchema(className, out ObjectSchema? sourceSchema) || sourceSchema == null)
                return;
            if (!destination.Schema.TryFindObjectSchema(className, out _))
                return;

            // Snapshot to lists so the iteration is stable across batch
            // commits (commits invalidate live IRealmCollection cursors).
            // Realm's collection types don't support LINQ Cast<T>, so
            // we cast element-by-element via foreach.
            var sourceObjects = new List<IRealmObjectBase>();
            foreach (var item in source.DynamicApi.All(className))
                sourceObjects.Add((IRealmObjectBase)item);

            var destObjects = new List<IRealmObjectBase>();
            foreach (var item in destination.DynamicApi.All(className))
                destObjects.Add((IRealmObjectBase)item);

            if (sourceObjects.Count != destObjects.Count)
                throw new InvalidOperationException($"Pass B count mismatch for '{className}': source={sourceObjects.Count} dest={destObjects.Count}.");

            Property? pkProp = findPrimaryKey(sourceSchema);
            int sinceLastCommit = 0;
            Transaction? tx = destination.BeginWrite();

            try
            {
                for (int i = 0; i < sourceObjects.Count; i++)
                {
                    var sourceObj = sourceObjects[i];
                    IRealmObjectBase destObj;

                    if (pkProp.HasValue)
                    {
                        var pkRv = sourceObj.DynamicApi.Get<RealmValue>(pkProp.Value.Name);
                        destObj = findByPk(destination, className, pkProp.Value, pkRv)
                                  ?? throw new InvalidOperationException($"Could not find pass-A insert for '{className}' pk={pkRv}.");
                    }
                    else
                    {
                        destObj = destObjects[i];
                    }

                    foreach (var prop in sourceSchema)
                    {
                        if (shouldSkipProperty(className, prop)) continue;
                        if (!isReferenceProperty(prop)) continue;
                        // Embedded refs were materialised in pass A —
                        // they have no independent identity to look
                        // up, so they don't go through pass B.
                        if (isEmbeddedTarget(source, prop)) continue;
                        copyReference(source, destination, sourceObj, destObj, prop);
                    }

                    sinceLastCommit++;

                    if (sinceLastCommit >= batch_size)
                    {
                        tx.Commit();
                        tx.Dispose();
                        sinceLastCommit = 0;
                        tx = destination.BeginWrite();

                        if (!pkProp.HasValue)
                        {
                            destObjects = new List<IRealmObjectBase>();
                            foreach (var refreshed in destination.DynamicApi.All(className))
                                destObjects.Add((IRealmObjectBase)refreshed);
                        }
                    }
                }

                tx.Commit();
            }
            finally
            {
                tx?.Dispose();
            }

            Logger.Log($"[RealmDowngrade] Pass B: linked {sourceObjects.Count} '{className}' references.", LoggingTarget.Database);
        }

        // ================================================================
        // Object factories — handle every PK shape Realm supports.
        // ================================================================
        private static IRealmObjectBase createDestinationObject(Realm destination, ObjectSchema schema, IRealmObjectBase sourceObj)
        {
            Property? pk = findPrimaryKey(schema);

            if (!pk.HasValue)
                return (IRealmObjectBase)destination.DynamicApi.CreateObject(schema.Name);

            var pkRv = sourceObj.DynamicApi.Get<RealmValue>(pk.Value.Name);

            switch (pkRv.Type)
            {
                case RealmValueType.Guid:
                    return (IRealmObjectBase)destination.DynamicApi.CreateObject(schema.Name, (Guid?)pkRv.AsGuid());
                case RealmValueType.String:
                    return (IRealmObjectBase)destination.DynamicApi.CreateObject(schema.Name, pkRv.AsString());
                case RealmValueType.Int:
                    return (IRealmObjectBase)destination.DynamicApi.CreateObject(schema.Name, (long?)pkRv.AsInt64());
                case RealmValueType.Null:
                    return (IRealmObjectBase)destination.DynamicApi.CreateObject(schema.Name, (Guid?)null);
                default:
                    throw new InvalidOperationException($"Unsupported primary key type {pkRv.Type} for class '{schema.Name}'.");
            }
        }

        private static IRealmObjectBase? findByPk(Realm destination, string className, Property pkProp, RealmValue pk)
        {
            switch (pk.Type)
            {
                case RealmValueType.Guid:
                    return (IRealmObjectBase?)destination.DynamicApi.Find(className, (Guid?)pk.AsGuid());
                case RealmValueType.String:
                    return (IRealmObjectBase?)destination.DynamicApi.Find(className, pk.AsString());
                case RealmValueType.Int:
                    return (IRealmObjectBase?)destination.DynamicApi.Find(className, (long?)pk.AsInt64());
                case RealmValueType.Null:
                    return (IRealmObjectBase?)destination.DynamicApi.Find(className, (Guid?)null);
                default:
                    throw new InvalidOperationException($"Unsupported primary key type {pk.Type} for class '{className}'.");
            }
        }

        // ================================================================
        // Pass A property copying
        // ================================================================
        private void copyPrimitiveOrEmbedded(Realm source, Realm destination, IRealmObjectBase sourceObj, IRealmObjectBase destObj, Property prop)
        {
            if ((prop.Type & PropertyType.Array) == PropertyType.Array)
            {
                copyListPrimitiveOrEmbedded(source, destination, sourceObj, destObj, prop);
                return;
            }

            if ((prop.Type & PropertyType.Set) == PropertyType.Set)
            {
                copySetPrimitive(sourceObj, destObj, prop);
                return;
            }

            if ((prop.Type & PropertyType.Dictionary) == PropertyType.Dictionary)
            {
                copyDictionaryPrimitive(sourceObj, destObj, prop);
                return;
            }

            var baseType = prop.Type & ~PropertyType.Nullable;

            if (baseType == PropertyType.Object)
            {
                if (isEmbeddedTarget(source, prop))
                {
                    copyEmbeddedSingle(source, destination, sourceObj, destObj, prop);
                    return;
                }

                // Top-level reference — pass B.
                return;
            }

            // Primitive scalar.
            try
            {
                var rv = sourceObj.DynamicApi.Get<RealmValue>(prop.Name);
                destObj.DynamicApi.Set(prop.Name, rv);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed copying primitive '{prop.Name}': {ex.Message}", ex);
            }
        }

        private void copyListPrimitiveOrEmbedded(Realm source, Realm destination, IRealmObjectBase sourceObj, IRealmObjectBase destObj, Property prop)
        {
            var baseType = prop.Type & ~PropertyType.Array & ~PropertyType.Nullable;

            // Reference lists deferred to pass B.
            if (baseType == PropertyType.Object && !isEmbeddedTarget(source, prop))
                return;

            if (baseType == PropertyType.Object && isEmbeddedTarget(source, prop))
            {
                var sourceList = sourceObj.DynamicApi.GetList<IRealmObjectBase>(prop.Name);
                var destList = destObj.DynamicApi.GetList<IRealmObjectBase>(prop.Name);

                foreach (var sourceEmbedded in sourceList)
                {
                    var destEmbedded = (IRealmObjectBase)destination.DynamicApi.AddEmbeddedObjectToList(destList);
                    copyEmbeddedProperties(source, destination, sourceEmbedded, destEmbedded, prop.ObjectType!);
                }

                return;
            }

            // List of primitive RealmValues.
            var srcPrimList = sourceObj.DynamicApi.GetList<RealmValue>(prop.Name);
            var dstPrimList = destObj.DynamicApi.GetList<RealmValue>(prop.Name);
            foreach (var v in srcPrimList)
                dstPrimList.Add(v);
        }

        private static void copySetPrimitive(IRealmObjectBase sourceObj, IRealmObjectBase destObj, Property prop)
        {
            var sourceSet = sourceObj.DynamicApi.GetSet<RealmValue>(prop.Name);
            var destSet = destObj.DynamicApi.GetSet<RealmValue>(prop.Name);
            foreach (var v in sourceSet)
                destSet.Add(v);
        }

        private static void copyDictionaryPrimitive(IRealmObjectBase sourceObj, IRealmObjectBase destObj, Property prop)
        {
            var sourceDict = sourceObj.DynamicApi.GetDictionary<RealmValue>(prop.Name);
            var destDict = destObj.DynamicApi.GetDictionary<RealmValue>(prop.Name);
            foreach (var kv in sourceDict)
                destDict[kv.Key] = kv.Value;
        }

        private static void copyEmbeddedSingle(Realm source, Realm destination, IRealmObjectBase sourceObj, IRealmObjectBase destObj, Property prop)
        {
            var rv = sourceObj.DynamicApi.Get<RealmValue>(prop.Name);
            if (rv.Type == RealmValueType.Null) return;

            var sourceEmbedded = rv.AsRealmObject<IRealmObjectBase>();
            var destEmbedded = (IRealmObjectBase)destination.DynamicApi.CreateEmbeddedObjectForProperty(destObj, prop.Name);
            copyEmbeddedProperties(source, destination, sourceEmbedded, destEmbedded, prop.ObjectType!);
        }

        private static void copyEmbeddedProperties(Realm source, Realm destination, IRealmObjectBase sourceEmbedded, IRealmObjectBase destEmbedded, string embeddedClassName)
        {
            if (!source.Schema.TryFindObjectSchema(embeddedClassName, out ObjectSchema? embeddedSchema) || embeddedSchema == null)
                return;

            foreach (var prop in embeddedSchema)
            {
                if (prop.IsPrimaryKey) continue;
                if ((prop.Type & PropertyType.LinkingObjects) == PropertyType.LinkingObjects) continue;

                var baseType = prop.Type & ~PropertyType.Nullable & ~PropertyType.Array & ~PropertyType.Set & ~PropertyType.Dictionary;

                if ((prop.Type & PropertyType.Array) == PropertyType.Array)
                {
                    if (baseType == PropertyType.Object)
                    {
                        var srcList = sourceEmbedded.DynamicApi.GetList<IRealmObjectBase>(prop.Name);
                        var dstList = destEmbedded.DynamicApi.GetList<IRealmObjectBase>(prop.Name);

                        foreach (var nested in srcList)
                        {
                            var nestedDest = (IRealmObjectBase)destination.DynamicApi.AddEmbeddedObjectToList(dstList);
                            copyEmbeddedProperties(source, destination, nested, nestedDest, prop.ObjectType!);
                        }
                    }
                    else
                    {
                        var srcList = sourceEmbedded.DynamicApi.GetList<RealmValue>(prop.Name);
                        var dstList = destEmbedded.DynamicApi.GetList<RealmValue>(prop.Name);
                        foreach (var v in srcList)
                            dstList.Add(v);
                    }

                    continue;
                }

                if ((prop.Type & PropertyType.Set) == PropertyType.Set)
                {
                    var srcSet = sourceEmbedded.DynamicApi.GetSet<RealmValue>(prop.Name);
                    var dstSet = destEmbedded.DynamicApi.GetSet<RealmValue>(prop.Name);
                    foreach (var v in srcSet)
                        dstSet.Add(v);
                    continue;
                }

                if ((prop.Type & PropertyType.Dictionary) == PropertyType.Dictionary)
                {
                    var srcDict = sourceEmbedded.DynamicApi.GetDictionary<RealmValue>(prop.Name);
                    var dstDict = destEmbedded.DynamicApi.GetDictionary<RealmValue>(prop.Name);
                    foreach (var kv in srcDict)
                        dstDict[kv.Key] = kv.Value;
                    continue;
                }

                if (baseType == PropertyType.Object)
                {
                    var rv = sourceEmbedded.DynamicApi.Get<RealmValue>(prop.Name);
                    if (rv.Type == RealmValueType.Null) continue;

                    var nestedSrc = rv.AsRealmObject<IRealmObjectBase>();
                    var nestedDst = (IRealmObjectBase)destination.DynamicApi.CreateEmbeddedObjectForProperty(destEmbedded, prop.Name);
                    copyEmbeddedProperties(source, destination, nestedSrc, nestedDst, prop.ObjectType!);
                    continue;
                }

                var primRv = sourceEmbedded.DynamicApi.Get<RealmValue>(prop.Name);
                destEmbedded.DynamicApi.Set(prop.Name, primRv);
            }
        }

        // ================================================================
        // Pass B reference copying
        // ================================================================
        private void copyReference(Realm source, Realm destination, IRealmObjectBase sourceObj, IRealmObjectBase destObj, Property prop)
        {
            if ((prop.Type & PropertyType.Array) == PropertyType.Array)
            {
                copyReferenceList(source, destination, sourceObj, destObj, prop);
                return;
            }

            if ((prop.Type & PropertyType.Set) == PropertyType.Set)
            {
                copyReferenceSet(source, destination, sourceObj, destObj, prop);
                return;
            }

            // Single object ref.
            var rv = sourceObj.DynamicApi.Get<RealmValue>(prop.Name);
            if (rv.Type == RealmValueType.Null)
            {
                destObj.DynamicApi.Set(prop.Name, RealmValue.Null);
                return;
            }

            var sourceRef = rv.AsRealmObject<IRealmObjectBase>();
            var destRef = resolveReference(destination, sourceRef, prop.ObjectType!);
            destObj.DynamicApi.Set(prop.Name, RealmValue.Object(destRef));
        }

        private void copyReferenceList(Realm source, Realm destination, IRealmObjectBase sourceObj, IRealmObjectBase destObj, Property prop)
        {
            var sourceList = sourceObj.DynamicApi.GetList<IRealmObjectBase>(prop.Name);
            var destList = destObj.DynamicApi.GetList<IRealmObjectBase>(prop.Name);

            foreach (var sourceRef in sourceList)
            {
                var destRef = resolveReference(destination, sourceRef, prop.ObjectType!);
                destList.Add(destRef);
            }
        }

        private void copyReferenceSet(Realm source, Realm destination, IRealmObjectBase sourceObj, IRealmObjectBase destObj, Property prop)
        {
            var sourceSet = sourceObj.DynamicApi.GetSet<IRealmObjectBase>(prop.Name);
            var destSet = destObj.DynamicApi.GetSet<IRealmObjectBase>(prop.Name);

            foreach (var sourceRef in sourceSet)
            {
                var destRef = resolveReference(destination, sourceRef, prop.ObjectType!);
                destSet.Add(destRef);
            }
        }

        private IRealmObjectBase resolveReference(Realm destination, IRealmObjectBase sourceRef, string targetClassName)
        {
            if (!destination.Schema.TryFindObjectSchema(targetClassName, out ObjectSchema? targetSchema) || targetSchema == null)
                throw new InvalidOperationException($"Target class '{targetClassName}' not found in destination schema.");

            Property? pkProp = findPrimaryKey(targetSchema);

            if (pkProp.HasValue)
            {
                var pkRv = sourceRef.DynamicApi.Get<RealmValue>(pkProp.Value.Name);
                var found = findByPk(destination, targetClassName, pkProp.Value, pkRv);

                if (found == null)
                    throw new InvalidOperationException($"Reference target not found in destination: class='{targetClassName}', pk={pkRv}.");

                return found;
            }

            // No PK on target — look up via the identity map captured
            // during pass A. Realm's row-identity Equals/GetHashCode
            // makes Dictionary lookup O(1).
            if (!pklessClassMaps.TryGetValue(targetClassName, out var map))
                throw new InvalidOperationException(
                    $"Reference target is pkless class '{targetClassName}' but pass A didn't record a mapping for it. " +
                    "Did pass A run for this class? Was the class even present in the source?");

            if (!map.TryGetValue(sourceRef, out var dest))
                throw new InvalidOperationException(
                    $"Could not find source object in pass-A pkless map for class '{targetClassName}'.");

            return dest;
        }

        // ================================================================
        // Helpers
        // ================================================================
        private static bool shouldSkipProperty(string className, Property prop)
        {
            // The whole point of the downgrade.
            if (className == "Skin" && prop.Name == "Pinned")
                return true;

            // Backlinks are computed; trying to set them throws.
            if ((prop.Type & PropertyType.LinkingObjects) == PropertyType.LinkingObjects)
                return true;

            return false;
        }

        private static bool isReferenceProperty(Property prop)
        {
            var baseType = prop.Type & ~PropertyType.Nullable & ~PropertyType.Array & ~PropertyType.Set & ~PropertyType.Dictionary;
            return baseType == PropertyType.Object;
        }

        private static bool isEmbeddedTarget(Realm realm, Property prop)
        {
            if (prop.ObjectType == null) return false;
            if (!realm.Schema.TryFindObjectSchema(prop.ObjectType, out ObjectSchema? targetSchema) || targetSchema == null)
                return false;
            return targetSchema.BaseType == ObjectSchema.ObjectType.EmbeddedObject;
        }

        private static Property? findPrimaryKey(ObjectSchema schema)
        {
            foreach (var prop in schema)
            {
                if (prop.IsPrimaryKey)
                    return prop;
            }

            return null;
        }
    }
}
