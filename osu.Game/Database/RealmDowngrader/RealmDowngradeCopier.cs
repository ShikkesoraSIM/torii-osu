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

        /// <summary>
        /// Optional progress sink. Receives one human-readable line
        /// per N objects copied (where N = <see cref="batch_size"/>)
        /// plus per-class start / end summaries. Lets the runner
        /// surface live status to a progress UI without the copier
        /// having to know anything about that UI.
        /// </summary>
        public Action<string>? OnProgress { get; set; }

        private const int batch_size = 5000;

        private readonly Dictionary<string, int> copiedCounts = new Dictionary<string, int>();

        /// <summary>
        /// Pkless top-level classes that ARE referenced by another
        /// schema class (e.g. BeatmapMetadata, the target of
        /// BeatmapInfo.Metadata). These are NOT iterated in pass A —
        /// they're materialised on demand from inside
        /// <see cref="resolveReference"/> (one fresh dest row per
        /// inbound reference, see <see cref="materialisePklessReference"/>).
        ///
        /// Why per-reference materialisation: an earlier version of
        /// this copier iterated pkless classes in pass A and built a
        /// Dictionary&lt;sourceRow, destRow&gt; for pass B to look up.
        /// That broke silently because Realm.NET's IRealmObjectBase
        /// doesn't guarantee Equals/GetHashCode are stable for the
        /// same row across independent queries. Per-ref materialisation
        /// trades storage (each inbound ref gets its own copy) for
        /// provable identity correctness.
        /// </summary>
        private readonly System.Collections.Generic.HashSet<string> pklessReferencedClasses = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);

        public void Copy(Realm source, Realm destination)
        {
            var topLevelClasses = source.Schema
                                        .Where(s => s.BaseType == ObjectSchema.ObjectType.RealmObject)
                                        .Select(s => s.Name)
                                        .ToArray();

            // Step 1 — identify every pkless top-level class.
            var pklessAll = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (string className in topLevelClasses)
            {
                if (!source.Schema.TryFindObjectSchema(className, out var schema) || schema == null)
                    continue;
                if (findPrimaryKey(schema) == null)
                    pklessAll.Add(className);
            }

            // Step 2 — find which of those pkless classes are TARGETS
            // of at least one Object property somewhere in the schema.
            // Only referenced pkless classes get the per-ref-materialise
            // treatment. Standalone pkless classes (e.g. RulesetSetting,
            // which is queried globally rather than via reference) are
            // iterated normally in pass A — there's no caller that
            // would otherwise create their dest rows.
            pklessReferencedClasses.Clear();
            foreach (var schema in source.Schema)
            {
                foreach (var prop in schema)
                {
                    if (prop.ObjectType == null) continue;
                    if (pklessAll.Contains(prop.ObjectType))
                        pklessReferencedClasses.Add(prop.ObjectType);
                }
            }

            OnProgress?.Invoke("Pass A — copying objects (skeletons + embedded data)...");
            foreach (string className in topLevelClasses)
            {
                if (pklessReferencedClasses.Contains(className))
                {
                    OnProgress?.Invoke($"  Pass A '{className}': pkless + referenced, deferring to materialise-on-reference.");
                    continue;
                }

                copyClassSkeletons(source, destination, className);
            }

            OnProgress?.Invoke("Pass B — linking cross-class references...");
            foreach (string className in topLevelClasses)
            {
                if (pklessReferencedClasses.Contains(className))
                    continue;

                copyClassReferences(source, destination, className);
            }
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

            // Pkless top-level classes that ARE referenced by other
            // classes are filtered out by Copy() before invoking us —
            // those go through materialise-on-reference. Pkless classes
            // that are NOT referenced (e.g. RulesetSetting) get iterated
            // normally here; createDestinationObject handles the
            // no-PK case by calling DynamicApi.CreateObject without a
            // pk argument, which is a fresh row.
            var sourceObjects = source.DynamicApi.All(className);
            int count = 0;
            int sinceLastCommit = 0;
            Transaction? tx = destination.BeginWrite();

            try
            {
                foreach (var sourceItem in sourceObjects)
                {
                    var sourceObj = (IRealmObjectBase)sourceItem;
                    var destObj = createDestinationObject(destination, sourceSchema, sourceObj);

                    foreach (var prop in sourceSchema)
                    {
                        if (shouldSkipProperty(className, prop)) continue;
                        if (prop.IsPrimaryKey) continue;

                        // Skip TOP-LEVEL Object refs (single, list, set,
                        // dict) — those go through pass B which can
                        // resolve them by primary key against the now-
                        // populated dest. Embedded targets (single OR
                        // list) STAY here in pass A: they have no
                        // independent identity, they live nested under
                        // the parent, and they're populated via
                        // CreateEmbeddedObjectForProperty /
                        // AddEmbeddedObjectToList.
                        if (isReferenceProperty(prop) && !isEmbeddedTarget(source, prop)) continue;

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
                        OnProgress?.Invoke($"  Pass A '{className}': copied {count:N0} objects so far...");
                    }
                }

                tx.Commit();
            }
            finally
            {
                tx?.Dispose();
            }

            copiedCounts[className] = count;
            string passADone = $"  Pass A '{className}': {count:N0} objects copied.";
            Logger.Log($"[RealmDowngrade] {passADone}", LoggingTarget.Database);
            OnProgress?.Invoke(passADone);
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
                        // Embedded SINGLE refs were materialised in pass
                        // A — they have no independent identity to look
                        // up. Their NESTED top-level refs (e.g.
                        // RealmNamedFileUsage.File) are handled below by
                        // linkTopLevelRefsInsideEmbeddedsOf.
                        if (isEmbeddedTarget(source, prop)) continue;
                        copyReference(source, destination, sourceObj, destObj, prop);
                    }

                    // Walk every embedded subtree under this top-level
                    // object and link any top-level refs they contain.
                    // This is the half of pass B that the original
                    // implementation was missing — without it,
                    // RealmNamedFileUsage.File ended up unset, every
                    // RealmFile row looked orphaned, and osu!'s file
                    // store cleanup deleted the user's entire content
                    // library on next startup.
                    linkTopLevelRefsInsideEmbeddedsOf(source, destination, sourceSchema, sourceObj, destObj);

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

                        OnProgress?.Invoke($"  Pass B '{className}': linked {(i + 1):N0} / {sourceObjects.Count:N0} references...");
                    }
                }

                tx.Commit();
            }
            finally
            {
                tx?.Dispose();
            }

            string passBDone = $"  Pass B '{className}': {sourceObjects.Count:N0} references linked.";
            Logger.Log($"[RealmDowngrade] {passBDone}", LoggingTarget.Database);
            OnProgress?.Invoke(passBDone);
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

        /// <summary>
        /// Pass-A copy of an embedded object's STRUCTURE: every primitive,
        /// every nested embedded (recursively), and every embedded list /
        /// set / dictionary. Top-level Object references inside this
        /// embedded (e.g. <c>RealmNamedFileUsage.File</c> pointing to a
        /// <c>RealmFile</c>) are intentionally LEFT UNSET here — they are
        /// linked in pass B by <see cref="copyEmbeddedReferences"/> once
        /// every top-level destination row exists and can be looked up
        /// by primary key.
        ///
        /// This matters: previously this method called
        /// <c>CreateEmbeddedObjectForProperty</c> blindly for any
        /// Object-typed property. For an embedded target that's correct,
        /// but for a top-level target (the only example in this schema
        /// is <c>RealmNamedFileUsage.File</c> -&gt; <c>RealmFile</c>) it
        /// produced a malformed RealmFile-shaped embedded that no
        /// existing top-level RealmFile row pointed at. The destination
        /// realm then looked like every RealmFile row was orphaned, and
        /// <c>RealmFileStore</c>'s post-startup cleanup deleted ALL the
        /// physical files in <c>%APPDATA%\osu\files\</c>. Hence the
        /// strict gate on <see cref="isEmbeddedTarget"/> below.
        /// </summary>
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
                        // Embedded list of embedded targets — recurse.
                        // Embedded list of TOP-LEVEL targets — defer to
                        // pass B; we don't even pre-allocate here because
                        // pass B will resolve and Add() for each source
                        // entry, preserving order.
                        if (!isEmbeddedTarget(source, prop))
                            continue;

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
                    // Top-level Object refs inside an embedded object are
                    // deferred to pass B (copyEmbeddedReferences). DO NOT
                    // call CreateEmbeddedObjectForProperty here — the
                    // target isn't embedded, and the resulting malformed
                    // row was the bug that wiped users' file stores.
                    if (!isEmbeddedTarget(source, prop))
                        continue;

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
            var destRef = resolveReference(source, destination, sourceRef, prop.ObjectType!);
            destObj.DynamicApi.Set(prop.Name, RealmValue.Object(destRef));
        }

        private void copyReferenceList(Realm source, Realm destination, IRealmObjectBase sourceObj, IRealmObjectBase destObj, Property prop)
        {
            var sourceList = sourceObj.DynamicApi.GetList<IRealmObjectBase>(prop.Name);
            var destList = destObj.DynamicApi.GetList<IRealmObjectBase>(prop.Name);

            foreach (var sourceRef in sourceList)
            {
                var destRef = resolveReference(source, destination, sourceRef, prop.ObjectType!);
                destList.Add(destRef);
            }
        }

        private void copyReferenceSet(Realm source, Realm destination, IRealmObjectBase sourceObj, IRealmObjectBase destObj, Property prop)
        {
            var sourceSet = sourceObj.DynamicApi.GetSet<IRealmObjectBase>(prop.Name);
            var destSet = destObj.DynamicApi.GetSet<IRealmObjectBase>(prop.Name);

            foreach (var sourceRef in sourceSet)
            {
                var destRef = resolveReference(source, destination, sourceRef, prop.ObjectType!);
                destSet.Add(destRef);
            }
        }

        /// <summary>
        /// Look up (or, for pkless targets, materialise) the destination
        /// object equivalent to <paramref name="sourceRef"/>. PKed
        /// targets are resolved via O(log n) <c>Find</c>; pkless targets
        /// are FRESHLY materialised on each call (see
        /// <see cref="materialisePklessReference"/> for why we don't use
        /// an identity map).
        /// </summary>
        private IRealmObjectBase resolveReference(Realm source, Realm destination, IRealmObjectBase sourceRef, string targetClassName)
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

            // Pkless top-level target — there's no PK to look up against,
            // and identity-mapping IRealmObjectBase across independent
            // queries proved unreliable (see notes on
            // pkless_top_level_classes_skipped_in_pass_a). Materialise a
            // fresh dest copy of the source row instead. If multiple
            // inbound references share the same source row, each gets
            // its own dest copy — strictly more storage but guaranteed
            // identity-correct.
            return materialisePklessReference(source, destination, sourceRef, targetClassName);
        }

        /// <summary>
        /// Create a fresh destination object for a pkless top-level
        /// class and copy ALL of its fields from <paramref name="sourceRef"/>:
        /// primitives, embedded structure (incl. recursive embeddeds),
        /// and any nested top-level Object refs. Called from
        /// <see cref="resolveReference"/> at the moment a reference to
        /// this pkless class is being linked, so by the time we run
        /// every PKed top-level row in the destination already exists
        /// and can be looked up.
        /// </summary>
        private IRealmObjectBase materialisePklessReference(Realm source, Realm destination, IRealmObjectBase sourceRef, string targetClassName)
        {
            if (!source.Schema.TryFindObjectSchema(targetClassName, out ObjectSchema? srcSchema) || srcSchema == null)
                throw new InvalidOperationException($"Pkless target '{targetClassName}' not in source schema.");

            var destObj = (IRealmObjectBase)destination.DynamicApi.CreateObject(targetClassName);

            // Phase 1 — primitives + embedded structure (singles and
            // lists alike). The same filter copyClassSkeletons uses:
            // skip TOP-LEVEL Object refs (handled in phase 2), but
            // KEEP embedded Object props so e.g. BeatmapMetadata.Author
            // (RealmUser, embedded) actually gets created here. The
            // earlier version of this loop blindly skipped every
            // Object prop, which left BeatmapMetadata.Author null in
            // every materialised dest row — every BeatmapInfo whose
            // Metadata.Author was accessed (e.g. by IntroScreen during
            // first paint) NREd, hanging the UI before the welcome
            // screen could draw.
            foreach (var prop in srcSchema)
            {
                if (shouldSkipProperty(targetClassName, prop)) continue;
                if (prop.IsPrimaryKey) continue;
                if (isReferenceProperty(prop) && !isEmbeddedTarget(source, prop)) continue;
                copyPrimitiveOrEmbedded(source, destination, sourceRef, destObj, prop);
            }

            // Phase 2 — top-level Object refs at the top level of the
            // pkless class (e.g. if the schema had a pkless class with
            // an outbound reference, which currently doesn't exist but
            // is supported for forward compatibility).
            foreach (var prop in srcSchema)
            {
                if (shouldSkipProperty(targetClassName, prop)) continue;
                if (!isReferenceProperty(prop)) continue;
                if (isEmbeddedTarget(source, prop)) continue;
                copyReference(source, destination, sourceRef, destObj, prop);
            }

            // Phase 3 — link any top-level refs hiding inside embedded
            // subtrees of this pkless class. BeatmapMetadata.Author is
            // an embedded RealmUser with no top-level refs, so this
            // currently does nothing for the schema we ship with — but
            // the bug-class it guards against (file-store deletion via
            // unset embedded refs) is exactly what we just spent a
            // night recovering from, so we cover it everywhere.
            linkTopLevelRefsInsideEmbeddedsOf(source, destination, srcSchema, sourceRef, destObj);

            return destObj;
        }

        /// <summary>
        /// Walk every embedded property of <paramref name="parentSchema"/>
        /// (singles AND lists), pair source/destination embeddeds
        /// positionally, and recurse into <see cref="copyEmbeddedReferences"/>
        /// to link any top-level Object refs nested inside. Called from
        /// pass B's per-object loop and from
        /// <see cref="materialisePklessReference"/>.
        /// </summary>
        private void linkTopLevelRefsInsideEmbeddedsOf(Realm source, Realm destination, ObjectSchema parentSchema, IRealmObjectBase sourceParent, IRealmObjectBase destParent)
        {
            foreach (var prop in parentSchema)
            {
                if (shouldSkipProperty(parentSchema.Name, prop)) continue;
                if ((prop.Type & PropertyType.LinkingObjects) == PropertyType.LinkingObjects) continue;

                var bt = prop.Type & ~PropertyType.Nullable & ~PropertyType.Array & ~PropertyType.Set & ~PropertyType.Dictionary;
                if (bt != PropertyType.Object) continue;
                if (!isEmbeddedTarget(source, prop)) continue;

                if ((prop.Type & PropertyType.Array) == PropertyType.Array)
                {
                    var srcList = sourceParent.DynamicApi.GetList<IRealmObjectBase>(prop.Name);
                    var dstList = destParent.DynamicApi.GetList<IRealmObjectBase>(prop.Name);
                    int count = Math.Min(srcList.Count, dstList.Count);
                    for (int i = 0; i < count; i++)
                        copyEmbeddedReferences(source, destination, srcList[i], dstList[i], prop.ObjectType!);
                }
                else
                {
                    var srcRv = sourceParent.DynamicApi.Get<RealmValue>(prop.Name);
                    var dstRv = destParent.DynamicApi.Get<RealmValue>(prop.Name);
                    if (srcRv.Type == RealmValueType.Null || dstRv.Type == RealmValueType.Null) continue;
                    copyEmbeddedReferences(source, destination, srcRv.AsRealmObject<IRealmObjectBase>(), dstRv.AsRealmObject<IRealmObjectBase>(), prop.ObjectType!);
                }
            }
        }

        /// <summary>
        /// Pass B counterpart of <see cref="copyEmbeddedProperties"/>.
        /// For each property of the embedded class, set or rebuild any
        /// top-level Object ref (these were intentionally left unset by
        /// pass A) and recurse into nested embeddeds. Primitives and
        /// non-Object collections are skipped — pass A copied them.
        /// </summary>
        private void copyEmbeddedReferences(Realm source, Realm destination, IRealmObjectBase sourceEmbedded, IRealmObjectBase destEmbedded, string embeddedClassName)
        {
            if (!source.Schema.TryFindObjectSchema(embeddedClassName, out ObjectSchema? embSchema) || embSchema == null)
                return;

            foreach (var prop in embSchema)
            {
                if (prop.IsPrimaryKey) continue;
                if ((prop.Type & PropertyType.LinkingObjects) == PropertyType.LinkingObjects) continue;

                var bt = prop.Type & ~PropertyType.Nullable & ~PropertyType.Array & ~PropertyType.Set & ~PropertyType.Dictionary;
                if (bt != PropertyType.Object) continue;

                bool embeddedTarget = isEmbeddedTarget(source, prop);

                if ((prop.Type & PropertyType.Array) == PropertyType.Array)
                {
                    if (embeddedTarget)
                    {
                        // Pass A built the dest list with placeholder
                        // embeddeds matched 1:1 against source. Recurse
                        // positionally to link any top-level refs they
                        // contain.
                        var srcList = sourceEmbedded.DynamicApi.GetList<IRealmObjectBase>(prop.Name);
                        var dstList = destEmbedded.DynamicApi.GetList<IRealmObjectBase>(prop.Name);
                        int count = Math.Min(srcList.Count, dstList.Count);
                        for (int i = 0; i < count; i++)
                            copyEmbeddedReferences(source, destination, srcList[i], dstList[i], prop.ObjectType!);
                    }
                    else
                    {
                        // Embedded list of TOP-LEVEL refs. Pass A left
                        // the dest list empty — we rebuild it now in
                        // source order, resolving each ref against
                        // destination's already-populated PKed rows.
                        var srcList = sourceEmbedded.DynamicApi.GetList<IRealmObjectBase>(prop.Name);
                        var dstList = destEmbedded.DynamicApi.GetList<IRealmObjectBase>(prop.Name);
                        dstList.Clear();
                        foreach (var sRef in srcList)
                            dstList.Add(resolveReference(source, destination, sRef, prop.ObjectType!));
                    }

                    continue;
                }

                if (embeddedTarget)
                {
                    // Single nested embedded — recurse if both sides
                    // have a row (pass A would have created the dest
                    // counterpart whenever the source had one).
                    var srcRv = sourceEmbedded.DynamicApi.Get<RealmValue>(prop.Name);
                    var dstRv = destEmbedded.DynamicApi.Get<RealmValue>(prop.Name);
                    if (srcRv.Type == RealmValueType.Null || dstRv.Type == RealmValueType.Null) continue;
                    copyEmbeddedReferences(source, destination, srcRv.AsRealmObject<IRealmObjectBase>(), dstRv.AsRealmObject<IRealmObjectBase>(), prop.ObjectType!);
                    continue;
                }

                // Single TOP-LEVEL ref inside the embedded — link it.
                // This is the path that handles RealmNamedFileUsage.File
                // and is the whole reason this method exists.
                var rv = sourceEmbedded.DynamicApi.Get<RealmValue>(prop.Name);
                if (rv.Type == RealmValueType.Null)
                {
                    destEmbedded.DynamicApi.Set(prop.Name, RealmValue.Null);
                    continue;
                }

                var sRefSingle = rv.AsRealmObject<IRealmObjectBase>();
                var dRef = resolveReference(source, destination, sRefSingle, prop.ObjectType!);
                destEmbedded.DynamicApi.Set(prop.Name, RealmValue.Object(dRef));
            }
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
