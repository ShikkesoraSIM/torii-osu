// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using Realms;
using Realms.Schema;

namespace osu.Game.Database.RealmDowngrader
{
    /// <summary>
    /// Confirms a freshly-rebuilt v51 realm round-tripped every object
    /// from its v52 source. Run AFTER the copier commits its
    /// transaction and BEFORE the atomic swap into the live path —
    /// this is the last gate before we touch the user's filesystem in
    /// any irreversible way.
    ///
    /// What got us here
    /// -----------------
    /// The first generation of this verifier only checked per-class
    /// COUNTS plus a destination-side schema-version + Pinned-column
    /// check. It happily passed a destination realm that had every
    /// <c>RealmNamedFileUsage.File</c> reference unset — the embedded
    /// file-usage rows existed (so the count matched), but their
    /// <c>File</c> property pointed at no destination <c>RealmFile</c>.
    /// On next startup, osu!'s file-store cleanup ran the query
    /// <c>RealmFile.Usages.@count = 0</c>, marked every RealmFile row
    /// as orphaned, and deleted ~614,000 physical files (~200&#160;GB)
    /// from the user's <c>%APPDATA%\osu\files\</c> folder. Recovery
    /// from Recuva took the entire night.
    ///
    /// What this verifier checks now
    /// ------------------------------
    /// 1. <b>Schema version is exactly v51.</b>
    /// 2. <b>Pinned column is gone</b> from the destination's <c>Skin</c>
    ///    schema.
    /// 3. <b>Per-class count parity</b> for every PKed top-level class.
    ///    Pkless classes (BeatmapMetadata, RealmRulesetSetting) are
    ///    intentionally excluded because the copier materialises them
    ///    fresh per inbound reference (one dest row per ref) — counts
    ///    can differ legitimately.
    /// 4. <b>RealmFile orphan parity</b> — the count of RealmFile rows
    ///    matching <c>Usages.@count = 0</c> in the destination MUST
    ///    not exceed the source's count. This is the assertion that
    ///    would have caught the file-deletion catastrophe directly:
    ///    if the destination has more "orphans" than the source, the
    ///    cleanup pass on next startup will delete user data, period.
    /// 5. <b>File-usage integrity sample</b> — for a sampled subset of
    ///    BeatmapSetInfos and Skins, verify that every
    ///    <c>Files[i].File.Hash</c> in the destination matches the
    ///    source's value at the same index. This is a stronger
    ///    statement than (4): it catches not just orphan creation but
    ///    silently re-pointed references.
    /// 6. <b>BeatmapMetadata population</b> — every destination
    ///    BeatmapInfo whose source had non-null Metadata must also
    ///    have non-null Metadata, with matching Title. This catches
    ///    pkless materialisation regressions.
    /// </summary>
    public sealed class RealmDowngradeVerifier
    {
        public sealed class VerifyResult
        {
            public bool Ok { get; init; }
            public IReadOnlyList<string> Issues { get; init; } = Array.Empty<string>();
            public IReadOnlyDictionary<string, (int source, int dest)> Counts { get; init; } = new Dictionary<string, (int, int)>();

            /// <summary>
            /// Free-form lines describing what each integrity check
            /// actually examined (e.g. "sampled 2000 BeatmapSet rows,
            /// 2000 with non-empty Files, 0 mismatches"). Lets the
            /// runner / CLI demonstrate that the checks ran with real
            /// data instead of trivially passing on zero-row inputs.
            /// </summary>
            public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();
        }

        /// <summary>
        /// Hard cap on how many objects we sample per integrity check.
        /// 2000 is enough to catch a SYSTEMATIC corruption (the only
        /// kind worth aborting for) while keeping the verify phase
        /// finish in single-digit seconds even on huge libraries.
        /// </summary>
        private const int sample_cap = 2000;

        public VerifyResult Verify(Realm source, Realm destination, ulong expectedSchemaVersion = 51)
        {
            var issues = new List<string>();
            var notes = new List<string>();
            var counts = new Dictionary<string, (int source, int dest)>();

            // (1) destination schema version
            ulong destSchemaVersion = destination.Config.SchemaVersion;
            if (destSchemaVersion != expectedSchemaVersion)
                issues.Add($"Destination schema version is {destSchemaVersion}, expected {expectedSchemaVersion}.");

            // (2) Pinned column gone from Skin
            if (destination.Schema.TryFindObjectSchema("Skin", out ObjectSchema? skinSchema) && skinSchema != null)
            {
                if (skinSchema.Any(p => p.Name == "Pinned"))
                    issues.Add("Destination 'Skin' schema still contains a 'Pinned' property — downgrade did not strip it.");
            }
            else
            {
                issues.Add("Destination schema is missing the 'Skin' class entirely.");
            }

            // Build the same "pkless and referenced" set the copier
            // uses. We treat the parity rule differently for each
            // bucket:
            //   - PKed:                strict parity (src == dst)
            //   - Pkless + referenced: strict parity is NOT meaningful
            //                          because the copier materialises
            //                          one dest row per inbound ref
            //                          rather than one per source row.
            //                          We only flag empty-dest as a
            //                          probable bug.
            //   - Pkless + unreferenced: strict parity (the copier
            //                          iterates them normally in pass
            //                          A — see RulesetSetting).
            var pklessAll = new HashSet<string>(StringComparer.Ordinal);
            foreach (var schema in source.Schema)
            {
                if (schema.BaseType != ObjectSchema.ObjectType.RealmObject) continue;
                if (!schema.Any(p => p.IsPrimaryKey))
                    pklessAll.Add(schema.Name);
            }

            var pklessReferenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var schema in source.Schema)
            {
                foreach (var prop in schema)
                {
                    if (prop.ObjectType == null) continue;
                    if (pklessAll.Contains(prop.ObjectType))
                        pklessReferenced.Add(prop.ObjectType);
                }
            }

            // (3) Per-class count parity
            foreach (var schema in source.Schema)
            {
                if (schema.BaseType != ObjectSchema.ObjectType.RealmObject) continue;

                string className = schema.Name;

                int sourceCount = tryCount(source, className);

                if (!destination.Schema.TryFindObjectSchema(className, out _))
                {
                    issues.Add($"Class '{className}' is in the source but missing from the destination schema.");
                    counts[className] = (sourceCount, -1);
                    continue;
                }

                int destCount = tryCount(destination, className);
                counts[className] = (sourceCount, destCount);

                bool isPkless = pklessAll.Contains(className);
                bool isPklessReferenced = pklessReferenced.Contains(className);

                if (isPkless && isPklessReferenced)
                {
                    // Counts diverge legitimately when a source row is
                    // referenced multiple times (each ref gets its own
                    // dest copy) or never (no ref → no dest copy).
                    // Only flag the obvious bug: source had rows but
                    // destination is empty.
                    if (sourceCount > 0 && destCount == 0)
                        issues.Add($"Pkless+referenced class '{className}': source had {sourceCount} rows but destination is empty — no inbound references materialised any?");

                    continue;
                }

                // PKed classes AND pkless-unreferenced both require
                // strict parity — pkless-unreferenced is iterated
                // normally in pass A.
                if (sourceCount != destCount)
                    issues.Add($"Count mismatch for '{className}': source={sourceCount} dest={destCount}.");
            }

            // (4) RealmFile orphan parity — the assertion that would
            // have prevented the file-deletion incident. If dest has
            // MORE orphans than source, the cleanup pass on next
            // startup will delete real on-disk files.
            checkRealmFileOrphans(source, destination, issues, notes);

            // (5) File-usage integrity sample — checks RealmNamedFileUsage.File
            // hash equality for sampled BeatmapSets and Skins.
            // Class names below are realm SLOT names (the [MapTo] target),
            // not C# type names — DynamicApi.All() / Find() key off the
            // slot name, so "BeatmapSet" works and "BeatmapSetInfo" would
            // silently return zero rows.
            sampleFileUsageIntegrity(source, destination, "BeatmapSet", issues, notes);
            sampleFileUsageIntegrity(source, destination, "Skin", issues, notes);

            // (6) BeatmapInfo.Metadata population sample.
            sampleBeatmapMetadataPopulated(source, destination, issues, notes);

            // (7) Deep-equality sample — for each PKed top-level class,
            // sample N rows and compare EVERY field of the destination
            // schema against the source value, recursing into embeddeds
            // and dereferencing pkless refs (which materialise fresh
            // dest copies). PKed refs are compared by primary key only
            // to avoid infinite loops via cycles.
            //
            // This is the broadest gate: it catches any property — at
            // any depth — that the copier silently fails to populate.
            // We added it after the BeatmapMetadata.Author null bug got
            // past every earlier check and only surfaced as a UI hang
            // at IntroScreen.LogoArriving.
            foreach (var schema in destination.Schema)
            {
                if (schema.BaseType != ObjectSchema.ObjectType.RealmObject) continue;
                bool hasPk = false;
                foreach (var p in schema)
                {
                    if (p.IsPrimaryKey) { hasPk = true; break; }
                }
                if (!hasPk) continue; // pkless-unreferenced is checked indirectly via inbound refs
                sampleDeepEquality(source, destination, schema.Name, pklessReferenced, issues, notes);
            }

            return new VerifyResult
            {
                Ok = issues.Count == 0,
                Issues = issues,
                Counts = counts,
                Notes = notes,
            };
        }

        private static int tryCount(Realm realm, string className)
        {
            try { return realm.DynamicApi.All(className).Count(); }
            catch { return -1; }
        }

        /// <summary>
        /// Runs the same query osu!'s file-store cleanup runs
        /// (<c>RealmFile.Usages.@count = 0</c>) against both realms.
        /// If the destination has MORE matches than the source, the
        /// migration broke RealmFile references and cleanup will
        /// delete physical files on next startup.
        /// </summary>
        private static void checkRealmFileOrphans(Realm source, Realm destination, List<string> issues, List<string> notes)
        {
            // RealmFile's realm class slot is "File" (see [MapTo]), not
            // "RealmFile". Use the slot name with DynamicApi.
            if (!source.Schema.TryFindObjectSchema("File", out _))
                return;
            if (!destination.Schema.TryFindObjectSchema("File", out _))
            {
                issues.Add("Destination is missing the 'File' (RealmFile) class entirely.");
                return;
            }

            int srcOrphans;
            int dstOrphans;
            int srcTotal;
            int dstTotal;

            try
            {
                srcTotal = source.DynamicApi.All("File").Count();
                dstTotal = destination.DynamicApi.All("File").Count();
                srcOrphans = source.DynamicApi.All("File").Filter("Usages.@count = 0").Count();
                dstOrphans = destination.DynamicApi.All("File").Filter("Usages.@count = 0").Count();
            }
            catch (Exception ex)
            {
                issues.Add(
                    $"RealmFile orphan check failed to run ({ex.Message}). Cannot verify file references survived. " +
                    "Aborting out of caution — bypassing this check would risk re-triggering the file-deletion bug.");
                return;
            }

            notes.Add($"RealmFile orphan check: src={srcOrphans}/{srcTotal} orphan, dst={dstOrphans}/{dstTotal} orphan.");

            if (dstOrphans > srcOrphans)
            {
                issues.Add(
                    $"FILE-STORE INTEGRITY CHECK FAILED. Destination realm has {dstOrphans} RealmFile rows with no incoming Usage refs; " +
                    $"source had {srcOrphans}. Each extra orphan means a real user-content file that osu!'s post-startup cleanup would DELETE. " +
                    "Aborting BEFORE the atomic swap so your live realm and file-store are untouched.");
            }
        }

        /// <summary>
        /// For up to <see cref="sample_cap"/> objects of <paramref name="className"/>
        /// (which must be a top-level class with PK and an embedded
        /// <c>Files</c> list of <c>RealmNamedFileUsage</c>), confirm
        /// that for every <c>Files[i]</c>, the destination's
        /// <c>File.Hash</c> and <c>Filename</c> equal the source's.
        /// This catches the exact failure mode that wiped the user's
        /// file store: <c>File</c> reference set to a row with the
        /// wrong hash, or unset entirely (Hash empty / null).
        /// </summary>
        private static void sampleFileUsageIntegrity(Realm source, Realm destination, string className, List<string> issues, List<string> notes)
        {
            if (!source.Schema.TryFindObjectSchema(className, out var srcSchema) || srcSchema == null)
                return;
            if (!destination.Schema.TryFindObjectSchema(className, out _))
                return;

            // Find the PK property — needed to align source/dest rows.
            Property? pk = null;

            foreach (var p in srcSchema)
            {
                if (p.IsPrimaryKey)
                {
                    pk = p;
                    break;
                }
            }

            if (!pk.HasValue)
            {
                issues.Add($"sampleFileUsageIntegrity: class '{className}' has no primary key, can't align source/dest.");
                return;
            }

            int sampled = 0;
            int sampledNonEmpty = 0;
            int totalFilesChecked = 0;
            int mismatches = 0;
            int firstMismatchAt = -1;
            string? firstMismatchDetail = null;

            try
            {
                foreach (dynamic srcObj in source.DynamicApi.All(className))
                {
                    if (sampled >= sample_cap)
                        break;

                    sampled++;

                    object pkValue = srcObj.DynamicApi.Get<RealmValue>(pk.Value.Name);
                    var pkRv = (RealmValue)pkValue;

                    dynamic? dstObj = findByPk(destination, className, pkRv);

                    if (dstObj == null)
                    {
                        mismatches++;
                        if (firstMismatchAt < 0)
                        {
                            firstMismatchAt = sampled;
                            firstMismatchDetail = $"dest row missing for pk={pkRv}";
                        }

                        continue;
                    }

                    var srcFiles = (System.Collections.IList)srcObj.DynamicApi.GetList<dynamic>("Files");
                    var dstFiles = (System.Collections.IList)dstObj.DynamicApi.GetList<dynamic>("Files");

                    if (srcFiles.Count != dstFiles.Count)
                    {
                        mismatches++;
                        if (firstMismatchAt < 0)
                        {
                            firstMismatchAt = sampled;
                            firstMismatchDetail = $"{className} pk={pkRv}: Files count {srcFiles.Count} vs {dstFiles.Count}";
                        }

                        continue;
                    }

                    int n = srcFiles.Count;
                    if (n > 0)
                        sampledNonEmpty++;
                    totalFilesChecked += n;
                    for (int i = 0; i < n; i++)
                    {
                        dynamic srcUsage = srcFiles[i]!;
                        dynamic dstUsage = dstFiles[i]!;

                        string srcHash = (string)srcUsage.File.Hash;
                        string srcName = (string)srcUsage.Filename;

                        // The pre-fix bug expressed itself as either an
                        // exception accessing dst.File or null/empty
                        // hash on dst.File — capture both.
                        string dstHash;
                        string dstName;
                        try
                        {
                            if (dstUsage.File == null)
                            {
                                mismatches++;
                                if (firstMismatchAt < 0)
                                {
                                    firstMismatchAt = sampled;
                                    firstMismatchDetail = $"{className} pk={pkRv} Files[{i}]: dest File is NULL (would orphan a RealmFile row)";
                                }

                                break;
                            }

                            dstHash = (string)dstUsage.File.Hash;
                            dstName = (string)dstUsage.Filename;
                        }
                        catch (Exception fileEx)
                        {
                            mismatches++;
                            if (firstMismatchAt < 0)
                            {
                                firstMismatchAt = sampled;
                                firstMismatchDetail = $"{className} pk={pkRv} Files[{i}]: dest File access threw: {fileEx.Message}";
                            }

                            break;
                        }

                        if (srcHash != dstHash || srcName != dstName)
                        {
                            mismatches++;
                            if (firstMismatchAt < 0)
                            {
                                firstMismatchAt = sampled;
                                firstMismatchDetail =
                                    $"{className} pk={pkRv} Files[{i}]: src={srcName}@{srcHash} dst={dstName}@{dstHash}";
                            }

                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"sampleFileUsageIntegrity('{className}') threw: {ex.Message}. Treating as failure to be safe.");
                return;
            }

            if (mismatches > 0)
            {
                issues.Add(
                    $"FILE-USAGE INTEGRITY MISMATCH on '{className}': {mismatches}/{sampled} sampled objects differ. " +
                    $"First at sample #{firstMismatchAt}: {firstMismatchDetail}. " +
                    "This is the exact failure mode that deletes user files on next startup.");
            }

            notes.Add($"File-usage integrity '{className}': sampled={sampled}, with-non-empty-Files={sampledNonEmpty}, total-Files-entries-compared={totalFilesChecked}, mismatches={mismatches}.");
        }

        /// <summary>
        /// Sample BeatmapInfos. For every source row whose Metadata is
        /// non-null, the destination row's Metadata must also be
        /// non-null and have the same Title. This catches pkless
        /// materialisation regressions (BeatmapMetadata is pkless).
        /// </summary>
        private static void sampleBeatmapMetadataPopulated(Realm source, Realm destination, List<string> issues, List<string> notes)
        {
            // BeatmapInfo's realm class slot is "Beatmap" (see [MapTo]).
            if (!source.Schema.TryFindObjectSchema("Beatmap", out _) || !destination.Schema.TryFindObjectSchema("Beatmap", out _))
                return;

            int sampled = 0;
            int destNullWhereSrcWasNonNull = 0;
            int titleMismatches = 0;
            int destAuthorNull = 0;
            int authorUsernameMismatches = 0;
            string? firstFailureDetail = null;

            try
            {
                foreach (dynamic srcInfo in source.DynamicApi.All("Beatmap"))
                {
                    if (sampled >= sample_cap)
                        break;

                    sampled++;

                    Guid id = (Guid)srcInfo.ID;
                    dynamic? dstInfo = destination.DynamicApi.Find("Beatmap", (Guid?)id);

                    if (dstInfo == null)
                        continue; // class-count check above will already flag this

                    // Direct dynamic property access matches the rest
                    // of this file's pattern. `Metadata` is a top-level
                    // BeatmapMetadata reference (pkless target), so the
                    // value is either an IRealmObjectBase or null.
                    object? srcMeta = srcInfo.Metadata;
                    object? dstMeta = dstInfo.Metadata;

                    if (srcMeta != null && dstMeta == null)
                    {
                        destNullWhereSrcWasNonNull++;
                        if (firstFailureDetail == null)
                            firstFailureDetail = $"BeatmapInfo {id}: source.Metadata non-null, dest.Metadata NULL.";

                        continue;
                    }

                    if (srcMeta == null || dstMeta == null)
                        continue;

                    string srcTitle = (string)srcInfo.Metadata.Title;
                    string dstTitle = (string)dstInfo.Metadata.Title;

                    if (srcTitle != dstTitle)
                    {
                        titleMismatches++;
                        if (firstFailureDetail == null)
                            firstFailureDetail = $"BeatmapInfo {id}: Title src='{srcTitle}' dst='{dstTitle}'.";
                    }

                    // BeatmapMetadata.Author is an embedded RealmUser.
                    // If migration left it null, IntroScreen.LogoArriving
                    // -> WorkingBeatmap.ToString() -> GetDisplayTitle()
                    // NREs at line 59 (`metadataInfo.Author.Username`)
                    // and the UI hangs before the welcome screen draws.
                    // We learned this the hard way; cover it here.
                    object? srcAuthor = srcInfo.Metadata.Author;
                    object? dstAuthor = dstInfo.Metadata.Author;

                    if (srcAuthor != null && dstAuthor == null)
                    {
                        destAuthorNull++;
                        if (firstFailureDetail == null)
                            firstFailureDetail = $"BeatmapInfo {id}: source.Metadata.Author non-null, dest.Metadata.Author NULL (would NRE GetDisplayTitle).";

                        continue;
                    }

                    if (srcAuthor == null || dstAuthor == null)
                        continue;

                    string srcUser = (string)srcInfo.Metadata.Author.Username;
                    string dstUser = (string)dstInfo.Metadata.Author.Username;

                    if (srcUser != dstUser)
                    {
                        authorUsernameMismatches++;
                        if (firstFailureDetail == null)
                            firstFailureDetail = $"BeatmapInfo {id}: Author.Username src='{srcUser}' dst='{dstUser}'.";
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"sampleBeatmapMetadataPopulated threw: {ex.Message}. Treating as failure.");
                return;
            }

            if (destNullWhereSrcWasNonNull > 0)
                issues.Add($"BeatmapInfo.Metadata went NULL on {destNullWhereSrcWasNonNull}/{sampled} samples. {firstFailureDetail}");

            if (titleMismatches > 0)
                issues.Add($"BeatmapInfo.Metadata.Title diverged on {titleMismatches}/{sampled} samples. {firstFailureDetail}");

            if (destAuthorNull > 0)
                issues.Add($"BeatmapInfo.Metadata.Author went NULL on {destAuthorNull}/{sampled} samples (would NRE the IntroScreen). {firstFailureDetail}");

            if (authorUsernameMismatches > 0)
                issues.Add($"BeatmapInfo.Metadata.Author.Username diverged on {authorUsernameMismatches}/{sampled} samples. {firstFailureDetail}");

            notes.Add($"BeatmapInfo.Metadata sample: sampled={sampled}, metadata-null={destNullWhereSrcWasNonNull}, title-diff={titleMismatches}, author-null={destAuthorNull}, author-name-diff={authorUsernameMismatches}.");
        }

        /// <summary>
        /// Sample up to <see cref="sample_cap"/> rows of <paramref name="className"/>
        /// (which must be a PKed top-level class) and assert deep
        /// equality between source and destination. For each property
        /// of the destination schema, recurse:
        ///   - primitive: compare RealmValue equality.
        ///   - primitive list / set / dict: compare element-wise.
        ///   - embedded single / list: recurse via the embedded class
        ///     schema, walking into every field of the nested object.
        ///   - top-level Object ref (PKed target): compare primary
        ///     keys only — recursing would loop forever via cycles
        ///     like BeatmapSet -&gt; Beatmaps[i] -&gt; BeatmapSet.
        ///   - top-level Object ref (pkless target): recurse via the
        ///     target's class schema (these are materialised fresh
        ///     per-reference, so the source and dest rows should
        ///     be field-identical).
        ///
        /// Reports the FULL field path on any divergence, e.g.
        /// "BeatmapInfo[id=...].Metadata.Author.Username". This is
        /// the gate that would have caught the BeatmapMetadata.Author
        /// null bug before the user ever saw a hung UI.
        /// </summary>
        private static void sampleDeepEquality(Realm source, Realm destination, string className, HashSet<string> pklessReferenced, List<string> issues, List<string> notes)
        {
            if (!source.Schema.TryFindObjectSchema(className, out var srcSchema) || srcSchema == null)
                return;
            if (!destination.Schema.TryFindObjectSchema(className, out var dstSchema) || dstSchema == null)
                return;

            Property? pk = null;
            foreach (var p in dstSchema)
            {
                if (p.IsPrimaryKey) { pk = p; break; }
            }
            if (!pk.HasValue) return; // caller should have filtered

            int sampled = 0;
            int divergent = 0;
            int totalLeavesChecked = 0;
            string? firstDivergence = null;

            try
            {
                foreach (dynamic srcObj in source.DynamicApi.All(className))
                {
                    if (sampled >= sample_cap) break;
                    sampled++;

                    var pkRv = (RealmValue)srcObj.DynamicApi.Get<RealmValue>(pk.Value.Name);
                    dynamic? dstObj = findByPk(destination, className, pkRv);

                    if (dstObj == null)
                    {
                        divergent++;
                        if (firstDivergence == null)
                            firstDivergence = $"{className}[pk={pkRv}]: dest row missing";
                        continue;
                    }

                    string rowPath = $"{className}[pk={pkRv}]";
                    string? mismatch = deepEqualObject(source, destination, (IRealmObjectBase)srcObj, (IRealmObjectBase)dstObj, dstSchema, pklessReferenced, rowPath, depth: 0, ref totalLeavesChecked);
                    if (mismatch != null)
                    {
                        divergent++;
                        if (firstDivergence == null)
                            firstDivergence = mismatch;
                    }
                }
            }
            catch (Exception ex)
            {
                issues.Add($"sampleDeepEquality('{className}') threw: {ex.Message}. Treating as failure.");
                return;
            }

            if (divergent > 0)
                issues.Add($"DEEP-EQUALITY MISMATCH on '{className}': {divergent}/{sampled} samples diverge. First: {firstDivergence}");

            notes.Add($"Deep-equality '{className}': sampled={sampled}, leaves-checked={totalLeavesChecked}, divergent={divergent}.");
        }

        // Bound recursion depth to avoid pathological loops if a future
        // schema introduces a cycle through embeddeds we can't detect
        // statically. 8 is plenty for the current schema (max real
        // depth is 3: BeatmapInfo -> Metadata -> Author -> primitive).
        private const int max_recursion_depth = 8;

        /// <summary>
        /// Recursive deep-equality walk. Returns null on success, or a
        /// string describing the first divergence (with field path).
        /// </summary>
        private static string? deepEqualObject(Realm source, Realm destination, IRealmObjectBase srcObj, IRealmObjectBase dstObj, ObjectSchema dstSchema, HashSet<string> pklessReferenced, string path, int depth, ref int leavesChecked)
        {
            if (depth > max_recursion_depth)
                return null; // bail; too-deep recursion is a verifier limitation, not a migration defect

            foreach (var prop in dstSchema)
            {
                if (prop.IsPrimaryKey) continue;
                if ((prop.Type & PropertyType.LinkingObjects) == PropertyType.LinkingObjects) continue;

                string fieldPath = $"{path}.{prop.Name}";
                bool isArray = (prop.Type & PropertyType.Array) == PropertyType.Array;
                bool isSet = (prop.Type & PropertyType.Set) == PropertyType.Set;
                bool isDict = (prop.Type & PropertyType.Dictionary) == PropertyType.Dictionary;
                var baseType = prop.Type & ~PropertyType.Nullable & ~PropertyType.Array & ~PropertyType.Set & ~PropertyType.Dictionary;

                if (baseType == PropertyType.Object)
                {
                    string targetClass = prop.ObjectType!;
                    bool targetEmbedded = false;
                    if (source.Schema.TryFindObjectSchema(targetClass, out var targetSchema) && targetSchema != null)
                        targetEmbedded = targetSchema.BaseType == ObjectSchema.ObjectType.EmbeddedObject;

                    if (isArray)
                    {
                        var srcList = srcObj.DynamicApi.GetList<IRealmObjectBase>(prop.Name);
                        var dstList = dstObj.DynamicApi.GetList<IRealmObjectBase>(prop.Name);

                        if (srcList.Count != dstList.Count)
                            return $"{fieldPath}: list count {srcList.Count} vs {dstList.Count}";

                        for (int i = 0; i < srcList.Count; i++)
                        {
                            string elemPath = $"{fieldPath}[{i}]";

                            if (targetEmbedded)
                            {
                                if (targetSchema == null) continue;
                                var nested = deepEqualObject(source, destination, srcList[i], dstList[i], targetSchema, pklessReferenced, elemPath, depth + 1, ref leavesChecked);
                                if (nested != null) return nested;
                            }
                            else
                            {
                                // Top-level ref list — compare PKs.
                                string? refMismatch = compareRefByPk(source, srcList[i], dstList[i], targetClass, elemPath);
                                if (refMismatch != null) return refMismatch;
                                leavesChecked++;
                            }
                        }
                    }
                    else if (isSet || isDict)
                    {
                        // Schema currently has no Set<Object> or Dict<Object>.
                        // Skip silently rather than guess.
                    }
                    else
                    {
                        // Single Object property (single-valued ref or embedded).
                        var srcRv = srcObj.DynamicApi.Get<RealmValue>(prop.Name);
                        var dstRv = dstObj.DynamicApi.Get<RealmValue>(prop.Name);
                        bool srcNull = srcRv.Type == RealmValueType.Null;
                        bool dstNull = dstRv.Type == RealmValueType.Null;

                        if (srcNull != dstNull)
                            return $"{fieldPath}: src null={srcNull} dst null={dstNull}";
                        if (srcNull) { leavesChecked++; continue; }

                        var srcRef = srcRv.AsRealmObject<IRealmObjectBase>();
                        var dstRef = dstRv.AsRealmObject<IRealmObjectBase>();

                        if (targetEmbedded)
                        {
                            if (targetSchema == null) continue;
                            var nested = deepEqualObject(source, destination, srcRef, dstRef, targetSchema, pklessReferenced, fieldPath, depth + 1, ref leavesChecked);
                            if (nested != null) return nested;
                        }
                        else if (pklessReferenced.Contains(targetClass))
                        {
                            // Pkless top-level: each ref is a freshly
                            // materialised dest row. Recurse — fields
                            // should match the source row.
                            if (targetSchema == null) continue;
                            var nested = deepEqualObject(source, destination, srcRef, dstRef, targetSchema, pklessReferenced, fieldPath, depth + 1, ref leavesChecked);
                            if (nested != null) return nested;
                        }
                        else
                        {
                            // PKed top-level: compare PKs, don't recurse
                            // (would loop forever via cycles).
                            string? refMismatch = compareRefByPk(source, srcRef, dstRef, targetClass, fieldPath);
                            if (refMismatch != null) return refMismatch;
                            leavesChecked++;
                        }
                    }
                }
                else
                {
                    // Primitive property.
                    if (isArray)
                    {
                        var srcList = srcObj.DynamicApi.GetList<RealmValue>(prop.Name);
                        var dstList = dstObj.DynamicApi.GetList<RealmValue>(prop.Name);

                        if (srcList.Count != dstList.Count)
                            return $"{fieldPath}: list count {srcList.Count} vs {dstList.Count}";

                        for (int i = 0; i < srcList.Count; i++)
                        {
                            if (!srcList[i].Equals(dstList[i]))
                                return $"{fieldPath}[{i}]: src={renderRv(srcList[i])} dst={renderRv(dstList[i])}";
                            leavesChecked++;
                        }
                    }
                    else if (isSet)
                    {
                        var srcSet = srcObj.DynamicApi.GetSet<RealmValue>(prop.Name);
                        var dstSet = dstObj.DynamicApi.GetSet<RealmValue>(prop.Name);

                        if (srcSet.Count != dstSet.Count)
                            return $"{fieldPath}: set size {srcSet.Count} vs {dstSet.Count}";

                        var dstItems = new HashSet<RealmValue>(dstSet);
                        foreach (var s in srcSet)
                        {
                            if (!dstItems.Contains(s))
                                return $"{fieldPath}: src has {renderRv(s)}, missing in dst";
                            leavesChecked++;
                        }
                    }
                    else if (isDict)
                    {
                        var srcDict = srcObj.DynamicApi.GetDictionary<RealmValue>(prop.Name);
                        var dstDict = dstObj.DynamicApi.GetDictionary<RealmValue>(prop.Name);

                        if (srcDict.Count != dstDict.Count)
                            return $"{fieldPath}: dict size {srcDict.Count} vs {dstDict.Count}";

                        foreach (var kv in srcDict)
                        {
                            if (!dstDict.TryGetValue(kv.Key, out var dstVal))
                                return $"{fieldPath}: src key '{kv.Key}' missing in dst";
                            if (!kv.Value.Equals(dstVal))
                                return $"{fieldPath}[{kv.Key}]: src={renderRv(kv.Value)} dst={renderRv(dstVal)}";
                            leavesChecked++;
                        }
                    }
                    else
                    {
                        var srcVal = srcObj.DynamicApi.Get<RealmValue>(prop.Name);
                        var dstVal = dstObj.DynamicApi.Get<RealmValue>(prop.Name);

                        if (!srcVal.Equals(dstVal))
                            return $"{fieldPath}: src={renderRv(srcVal)} dst={renderRv(dstVal)}";

                        leavesChecked++;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Compare two top-level Object refs by primary key only.
        /// Used by the deep-equality walker for refs into PKed classes
        /// — recursing would loop on cyclic schemas (BeatmapSet ↔
        /// Beatmaps[i] is the obvious case).
        /// </summary>
        private static string? compareRefByPk(Realm source, IRealmObjectBase srcRef, IRealmObjectBase dstRef, string targetClassName, string fieldPath)
        {
            if (!source.Schema.TryFindObjectSchema(targetClassName, out var targetSchema) || targetSchema == null)
                return null;

            Property? pk = null;
            foreach (var p in targetSchema)
            {
                if (p.IsPrimaryKey) { pk = p; break; }
            }
            if (!pk.HasValue) return null; // pkless target — covered by pkless-recursion path

            var srcPk = srcRef.DynamicApi.Get<RealmValue>(pk.Value.Name);
            var dstPk = dstRef.DynamicApi.Get<RealmValue>(pk.Value.Name);

            if (!srcPk.Equals(dstPk))
                return $"{fieldPath}: ref PK src={renderRv(srcPk)} dst={renderRv(dstPk)}";

            return null;
        }

        private static string renderRv(RealmValue v)
        {
            // Compact, single-line representation safe to put inside
            // an issue string. RealmValue.ToString() can return huge
            // payloads for binary blobs; we don't have any of those
            // in the schema right now but this is a defensive cap.
            string s = v.ToString();
            if (s.Length > 80) s = s.Substring(0, 77) + "...";
            return s;
        }

        /// <summary>
        /// DynamicApi.Find with the right overload for the PK type.
        /// Returns null if not found (matches the find-by-PK behaviour
        /// the rest of the codebase relies on).
        /// </summary>
        private static dynamic? findByPk(Realm realm, string className, RealmValue pk)
        {
            switch (pk.Type)
            {
                case RealmValueType.Guid:
                    return realm.DynamicApi.Find(className, (Guid?)pk.AsGuid());
                case RealmValueType.String:
                    return realm.DynamicApi.Find(className, pk.AsString());
                case RealmValueType.Int:
                    return realm.DynamicApi.Find(className, (long?)pk.AsInt64());
                case RealmValueType.Null:
                    return realm.DynamicApi.Find(className, (Guid?)null);
                default:
                    return null;
            }
        }
    }
}
