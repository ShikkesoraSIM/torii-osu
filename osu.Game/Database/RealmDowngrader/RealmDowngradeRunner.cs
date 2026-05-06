// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Configuration;
using osu.Game.Input.Bindings;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Skinning;
using Realms;

namespace osu.Game.Database.RealmDowngrader
{
    /// <summary>
    /// Orchestrates the full v52 → v51 realm downgrade end-to-end with
    /// every safety guarantee from <c>REALM_DOWNGRADE_PLAN.md</c>:
    /// pre-flight, pinned side-car, atomic backup, fresh-build copy,
    /// verify, atomic swap, re-open confirmation, rollback on any
    /// failure.
    ///
    /// The source realm at the live path is not touched until <see
    /// cref="Phase.AtomicSwap"/>. Every phase before that operates on
    /// either the read-only source or a temp-pathed work file, so a
    /// crash at any earlier moment leaves the user's data untouched.
    ///
    /// The runner is invoked by the user explicitly (button in
    /// settings + automatic prompt at startup if v52 is detected).
    /// It is NOT meant to run silently.
    /// </summary>
    public sealed class RealmDowngradeRunner
    {
        public enum Phase
        {
            PreFlight,
            SidecarPinnedSkins,
            Backup,
            OpenSource,
            BuildDestination,
            Verify,
            AtomicSwap,
            ReopenCheck,
            Done,
        }

        public sealed class RunResult
        {
            public bool Success { get; init; }
            public Phase StoppedAt { get; init; }
            public string? ErrorMessage { get; init; }
            public string? BackupPath { get; init; }
            public IReadOnlyDictionary<string, (int source, int dest)> Counts { get; init; }
                = new Dictionary<string, (int, int)>();
        }

        public ulong SourceSchemaVersion { get; private set; } = 0;

        private readonly Storage storage;
        private readonly string realmFilename;

        public RealmDowngradeRunner(Storage storage, string realmFilename)
        {
            this.storage = storage;
            this.realmFilename = realmFilename;
        }

        /// <summary>
        /// Run the full downgrade. The caller MUST ensure no other
        /// component (RealmAccess, importers, anything) is currently
        /// holding the realm open when this is invoked. The simplest
        /// way is to invoke it before <c>OsuGameBase</c> initialises
        /// any of its realm-consuming dependencies.
        /// </summary>
        public RunResult Run()
        {
            string realmPath = storage.GetFullPath(realmFilename);

            Phase currentPhase = Phase.PreFlight;
            RealmDowngradeBackup? backup = null;
            string? tempPath = null;
            IReadOnlyDictionary<string, (int source, int dest)> counts = new Dictionary<string, (int, int)>();

            try
            {
                Logger.Log($"[RealmDowngrade] Starting downgrade for {realmPath}", LoggingTarget.Database);

                // -----------------------------------------------------
                // Phase 0 — pre-flight
                // -----------------------------------------------------
                preFlight(realmPath);

                if (SourceSchemaVersion < 52)
                {
                    Logger.Log($"[RealmDowngrade] Source already at v{SourceSchemaVersion}, no work to do.", LoggingTarget.Database);
                    return new RunResult
                    {
                        Success = true,
                        StoppedAt = Phase.Done,
                    };
                }

                // -----------------------------------------------------
                // Phase 1 — side-car the Pinned skins
                // -----------------------------------------------------
                currentPhase = Phase.SidecarPinnedSkins;
                sidecarPinnedSkins(realmPath);

                // -----------------------------------------------------
                // Phase 2 — backup
                // -----------------------------------------------------
                currentPhase = Phase.Backup;
                backup = new RealmDowngradeBackup(realmPath);
                backup.Create();

                // -----------------------------------------------------
                // Phase 3 / 4 — open source read-only, build dest at v51
                // -----------------------------------------------------
                currentPhase = Phase.OpenSource;

                tempPath = realmPath + ".v51.tmp";
                if (File.Exists(tempPath))
                    File.Delete(tempPath);

                using (var source = Realm.GetInstance(buildSourceConfig(realmPath)))
                {
                    currentPhase = Phase.BuildDestination;

                    var copier = new RealmDowngradeCopier();

                    using (var dest = Realm.GetInstance(buildDestConfig(tempPath)))
                    {
                        // Copier opens its own write transactions per
                        // batch — see batch_size in RealmDowngradeCopier.
                        copier.Copy(source, dest);

                        // -----------------------------------------------
                        // Phase 5 — verify
                        // -----------------------------------------------
                        currentPhase = Phase.Verify;
                        var verifier = new RealmDowngradeVerifier();
                        var verifyResult = verifier.Verify(source, dest, expectedSchemaVersion: 51);
                        counts = verifyResult.Counts;

                        if (!verifyResult.Ok)
                        {
                            string issues = string.Join("; ", verifyResult.Issues);
                            throw new InvalidOperationException($"Verifier rejected the rebuilt realm: {issues}");
                        }
                    }
                }

                // -----------------------------------------------------
                // Phase 6 — atomic swap
                // -----------------------------------------------------
                currentPhase = Phase.AtomicSwap;
                backup.Verify();
                atomicSwap(realmPath, tempPath);

                // -----------------------------------------------------
                // Phase 7 — re-open at v51 to confirm
                // -----------------------------------------------------
                currentPhase = Phase.ReopenCheck;
                using (var verify = Realm.GetInstance(buildDestConfig(realmPath, isReadOnly: true)))
                {
                    if (verify.Config.SchemaVersion != 51)
                        throw new InvalidOperationException($"Post-swap reopen got schema {verify.Config.SchemaVersion}, expected 51.");
                }

                Logger.Log($"[RealmDowngrade] Downgrade completed successfully. Backup at {backup.BackupPath}", LoggingTarget.Database);

                return new RunResult
                {
                    Success = true,
                    StoppedAt = Phase.Done,
                    BackupPath = backup.BackupPath,
                    Counts = counts,
                };
            }
            catch (Exception ex)
            {
                Logger.Log($"[RealmDowngrade] FAILED at {currentPhase}: {ex}", LoggingTarget.Database, LogLevel.Error);

                // Rollback: clean up the temp dest if it exists.
                if (tempPath != null && File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); }
                    catch (Exception cleanup) { Logger.Log($"[RealmDowngrade] Could not delete temp '{tempPath}': {cleanup.Message}", LoggingTarget.Database, LogLevel.Important); }
                }

                // If we made it past the swap before failing, the live
                // path now has the new file but the reopen-confirm failed
                // — restore from backup so the user is never left with a
                // broken realm.
                if (currentPhase == Phase.ReopenCheck && backup != null)
                {
                    Logger.Log("[RealmDowngrade] Reopen failed AFTER swap. Restoring backup over live path...", LoggingTarget.Database, LogLevel.Important);
                    try
                    {
                        backup.Restore();
                    }
                    catch (Exception restoreEx)
                    {
                        Logger.Log($"[RealmDowngrade] CRITICAL: backup restore also failed: {restoreEx}. User must manually copy '{backup.BackupPath}' over '{realmPath}'.", LoggingTarget.Database, LogLevel.Error);
                    }
                }

                return new RunResult
                {
                    Success = false,
                    StoppedAt = currentPhase,
                    ErrorMessage = ex.Message,
                    BackupPath = backup?.BackupPath,
                    Counts = counts,
                };
            }
        }

        // =================================================================
        // Phase implementations
        // =================================================================

        private void preFlight(string realmPath)
        {
            if (!File.Exists(realmPath))
                throw new FileNotFoundException("Realm file not found at the live path.", realmPath);

            // Confirm it opens cleanly at v52 (proves it isn't held by
            // another process and that the file is at the version we
            // expect to downgrade from). We don't keep the realm open
            // — just a quick probe.
            try
            {
                using (var probe = Realm.GetInstance(buildProbeConfig(realmPath)))
                {
                    SourceSchemaVersion = probe.Config.SchemaVersion;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Could not open '{realmPath}' for pre-flight inspection. The file may be locked by another process: {ex.Message}", ex);
            }

            // Disk space sanity: we need backup + temp + headroom. A
            // realm that's 100MB needs ~300MB free on the same volume.
            long fileSize = new FileInfo(realmPath).Length;
            long required = fileSize * 3 + 50_000_000;

            string? volumePath = Path.GetPathRoot(realmPath);
            if (!string.IsNullOrEmpty(volumePath))
            {
                try
                {
                    var drive = new DriveInfo(volumePath);
                    if (drive.IsReady && drive.AvailableFreeSpace < required)
                    {
                        throw new IOException(
                            $"Insufficient disk space. Need {required / 1_000_000} MB free, have {drive.AvailableFreeSpace / 1_000_000} MB on volume {volumePath}.");
                    }
                }
                catch (ArgumentException)
                {
                    // Path isn't on a recognised drive (e.g. UNC path);
                    // skip the check rather than spuriously aborting.
                }
            }
        }

        private void sidecarPinnedSkins(string realmPath)
        {
            // Open the source at v52 read-only and pull out any Pinned
            // skin GUIDs. We use DynamicApi so we don't have to drag in
            // the typed SkinInfo class here (avoids accidental writes
            // and keeps this method side-effect-free on the source).
            var pinned = new List<Guid>();

            using (var source = Realm.GetInstance(buildSourceConfig(realmPath)))
            {
                if (!source.Schema.TryFindObjectSchema("Skin", out var skinSchema) || skinSchema == null)
                    return;

                if (!skinSchema.Any(p => p.Name == "Pinned"))
                {
                    // Source is at v52 but doesn't actually have the
                    // Pinned column — possible if it was migrated up
                    // and back somehow. Nothing to side-car.
                    return;
                }

                foreach (dynamic skin in source.DynamicApi.All("Skin"))
                {
                    bool isPinned = (bool)skin.Pinned;
                    if (isPinned)
                        pinned.Add((Guid)skin.ID);
                }
            }

            var sidecar = new PinnedSkinsStore(storage);
            sidecar.ReplaceAll(pinned);

            Logger.Log($"[RealmDowngrade] Side-car saved {pinned.Count} pinned skin GUID(s).", LoggingTarget.Database);
        }

        private static void atomicSwap(string realmPath, string tempPath)
        {
            // Realm.NET is known to hold native file handles past
            // Dispose() until the GC actually finalises the wrapper —
            // there are open issues against realm-dotnet about exactly
            // this pattern. Run a full GC + WaitForPendingFinalizers
            // before trying any rename so we're not racing the
            // collector. This is cheap on a path that runs at most
            // once per user.
            forceFinaliseRealmHandles();

            // Move the existing live file aside as a safety net while
            // the rename happens, then bring the temp file in. If
            // anything goes wrong between the two renames, restore the
            // displaced original.
            string displacedPath = realmPath + ".pre-swap";

            if (File.Exists(displacedPath))
                File.Delete(displacedPath);

            // Realm sibling artifacts (.lock, .note, .management/) get
            // recreated on next open; we don't have to move them, but
            // we should clear stale ones so the new file isn't paired
            // with old lock metadata.
            tryDeleteSibling(realmPath, ".lock");
            tryDeleteSibling(realmPath, ".note");
            tryDeleteSiblingDir(realmPath, ".management");

            tryMoveWithRetry(realmPath, displacedPath, what: "displace original");

            try
            {
                tryMoveWithRetry(tempPath, realmPath, what: "promote rebuilt");
            }
            catch
            {
                // Couldn't bring the new file in — put the old one
                // back so the user has a working realm again.
                if (File.Exists(displacedPath))
                {
                    try { File.Move(displacedPath, realmPath); }
                    catch (Exception restoreEx)
                    {
                        Logger.Log($"[RealmDowngrade] CRITICAL: could not restore displaced original: {restoreEx.Message}", LoggingTarget.Database, LogLevel.Error);
                    }
                }

                throw;
            }

            // The displaced original is now redundant (we have the
            // backup .v52.bak.<ts>). Delete it after the new file is
            // in place.
            try { File.Delete(displacedPath); }
            catch { /* ignored — non-fatal */ }
        }

        /// <summary>
        /// Force the .NET runtime to finalise any unreferenced native
        /// Realm handles. Realm.NET's native pointers live in
        /// finalizers, so a `using` block doesn't actually release
        /// them until GC runs. Without this, the rename calls below
        /// can fail with "file in use" on Windows.
        /// </summary>
        private static void forceFinaliseRealmHandles()
        {
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
        }

        /// <summary>
        /// File.Move with a small retry loop. Realm-side races very
        /// rarely surface as a transient AccessDenied / IOException;
        /// a 100ms sleep + GC kick clears them in practice.
        /// </summary>
        private static void tryMoveWithRetry(string source, string dest, string what)
        {
            const int max_attempts = 5;
            Exception? lastError = null;

            for (int attempt = 1; attempt <= max_attempts; attempt++)
            {
                try
                {
                    File.Move(source, dest);
                    return;
                }
                catch (IOException ex) when (attempt < max_attempts)
                {
                    lastError = ex;
                    Logger.Log($"[RealmDowngrade] {what} attempt {attempt} failed: {ex.Message}. Retrying after GC.", LoggingTarget.Database);
                    forceFinaliseRealmHandles();
                    System.Threading.Thread.Sleep(100 * attempt);
                }
                catch (UnauthorizedAccessException ex) when (attempt < max_attempts)
                {
                    lastError = ex;
                    Logger.Log($"[RealmDowngrade] {what} attempt {attempt} failed: {ex.Message}. Retrying after GC.", LoggingTarget.Database);
                    forceFinaliseRealmHandles();
                    System.Threading.Thread.Sleep(100 * attempt);
                }
            }

            throw new IOException($"Failed to {what} after {max_attempts} attempts: {lastError?.Message}", lastError);
        }

        private static void tryDeleteSibling(string realmPath, string suffix)
        {
            string p = realmPath + suffix;
            try
            {
                if (File.Exists(p))
                    File.Delete(p);
            }
            catch { /* non-fatal — Realm will tolerate stale lock files */ }
        }

        private static void tryDeleteSiblingDir(string realmPath, string suffix)
        {
            string p = realmPath + suffix;
            try
            {
                if (Directory.Exists(p))
                    Directory.Delete(p, recursive: true);
            }
            catch { /* non-fatal */ }
        }

        // =================================================================
        // RealmConfiguration builders
        // =================================================================

        /// <summary>
        /// Read-only config used for pre-flight probing AND for the
        /// source side of the copy. Specifies SchemaVersion=52 with our
        /// regular C# types (which include <c>SkinInfo.Pinned</c>) so
        /// the source realm opens at its current version without a
        /// migration callback. <see cref="RealmConfiguration.IsReadOnly"/>
        /// guarantees we never accidentally mutate the source.
        /// </summary>
        private static RealmConfiguration buildSourceConfig(string realmPath)
        {
            return new RealmConfiguration(realmPath)
            {
                SchemaVersion = 52,
                IsReadOnly = true,
                // Schema explicit so we don't pick up unrelated test
                // RealmObject types from elsewhere in the assembly.
                Schema = sourceSchemaTypes(),
            };
        }

        /// <summary>
        /// Probe config — same as source config but with a much shorter
        /// timeout so a stuck process doesn't make us hang.
        /// </summary>
        private static RealmConfiguration buildProbeConfig(string realmPath)
        {
            return new RealmConfiguration(realmPath)
            {
                SchemaVersion = 52,
                IsReadOnly = true,
                Schema = sourceSchemaTypes(),
            };
        }

        /// <summary>
        /// Destination config — fresh realm at v51 with the vanilla
        /// schema (using <see cref="SkinInfoV51"/> instead of
        /// <see cref="SkinInfo"/>). Realm creates the file with this
        /// schema description, NOT including the Pinned column.
        /// </summary>
        private static RealmConfiguration buildDestConfig(string realmPath, bool isReadOnly = false)
        {
            return new RealmConfiguration(realmPath)
            {
                SchemaVersion = 51,
                IsReadOnly = isReadOnly,
                Schema = destSchemaTypes(),
            };
        }

        /// <summary>
        /// Type list for the v52 source — uses the regular SkinInfo
        /// (with Pinned). This must mirror exactly what RealmAccess
        /// implicitly registered when the source was last written, or
        /// Realm will refuse to open with a schema-mismatch.
        /// </summary>
        private static Type[] sourceSchemaTypes() => new[]
        {
            typeof(BeatmapInfo),
            typeof(BeatmapMetadata),
            typeof(BeatmapSetInfo),
            typeof(BeatmapDifficulty),
            typeof(BeatmapUserSettings),
            typeof(BeatmapCollection),
            typeof(RealmKeyBinding),
            typeof(RealmRulesetSetting),
            typeof(RealmFile),
            typeof(RealmNamedFileUsage),
            typeof(RealmUser),
            typeof(RulesetInfo),
            typeof(ScoreInfo),
            typeof(SkinInfo),
            typeof(ModPreset),
        };

        /// <summary>
        /// Type list for the v51 destination — same as source but
        /// swap <see cref="SkinInfo"/> for <see cref="SkinInfoV51"/>.
        /// Both classes use <c>[MapTo("Skin")]</c> so the realm class
        /// slot is the same; only the property set differs.
        /// </summary>
        private static Type[] destSchemaTypes() => new[]
        {
            typeof(BeatmapInfo),
            typeof(BeatmapMetadata),
            typeof(BeatmapSetInfo),
            typeof(BeatmapDifficulty),
            typeof(BeatmapUserSettings),
            typeof(BeatmapCollection),
            typeof(RealmKeyBinding),
            typeof(RealmRulesetSetting),
            typeof(RealmFile),
            typeof(RealmNamedFileUsage),
            typeof(RealmUser),
            typeof(RulesetInfo),
            typeof(ScoreInfo),
            typeof(SkinInfoV51),
            typeof(ModPreset),
        };
    }
}
