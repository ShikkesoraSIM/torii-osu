// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Database.RealmDowngrader;
using SDL;
using Realms;

namespace osu.Desktop
{
    /// <summary>
    /// Detects whether the user's <c>client.realm</c> is on the now-defunct
    /// schema 52 (Torii's earlier "add SkinInfo.Pinned" change), and if so
    /// puts up a blocking SDL message box to walk them through the
    /// downgrade in-process before any of osu!'s own startup runs.
    ///
    /// Why this exists
    /// ---------------
    /// Schema 52 was a Torii-only addition that broke vanilla osu! lazer's
    /// ability to open shared-with-Torii realm folders. The fix is to drop
    /// back to schema 51 and move pin state to a side-car JSON file
    /// (<see cref="osu.Game.Skinning.PinnedSkinsStore"/>). New Torii builds
    /// run at v51, so a v52 file on disk would otherwise crash startup
    /// with a "schema version too new" exception. Catching it here and
    /// running the downgrade tool with user confirmation lets the
    /// migration happen without ever leaving the app, without a CLI flag,
    /// and without any data loss.
    ///
    /// Lifecycle
    /// ---------
    /// Called from <c>Program.Main</c> BEFORE any GameHost is constructed
    /// and BEFORE any other realm-touching code runs. The runner inside
    /// opens its own short-lived realms via explicit configurations, so
    /// it doesn't need any DI / framework state to operate.
    /// </summary>
    internal static class RealmDowngradeStartupPrompt
    {
        /// <summary>
        /// Probe the realm at <paramref name="folder"/> and run the
        /// downgrade flow if it's on a schema newer than this build
        /// expects. Returns when the realm is safe to open at v51, OR
        /// terminates the process via <see cref="Environment.Exit"/> if
        /// the migration fails.
        ///
        /// Returns silently (no UI) if there's no realm, the realm is
        /// already at v51 or older, or any pre-flight check fails — we
        /// only want to surface the modal when we KNOW the user has a
        /// migration to run.
        /// </summary>
        public static void RunIfNeeded(string folder)
        {
            string realmPath = Path.Combine(folder, "client.realm");

            // First-line defence: recover from an atomic-swap that was
            // interrupted between the two File.Move() calls. If the
            // process was killed (power loss, kill from task manager,
            // etc.) at exactly the wrong moment, we end up with the
            // displaced original sitting at <realm>.pre-swap and no
            // <realm> at all. Move the displaced file back into place
            // so startup proceeds with the user's data instead of an
            // empty fresh realm.
            recoverOrphanPreSwap(realmPath);

            if (!File.Exists(realmPath))
                return;

            // Sanity gate: if another osu! / Torii / lazer process is
            // currently holding the realm open, we can't migrate
            // (Realm uses exclusive write locks and the swap can't
            // rename a file held by another process). Detect this up
            // front so the user gets a clear message instead of a
            // confusing IOException mid-migration.
            string? otherProc = detectConflictingProcess();
            if (otherProc != null)
            {
                showAccessProblemModal(realmPath,
                    "Another osu! / Torii / lazer instance is already running ('" + otherProc + "').\n\n"
                    + "Close every other osu! window (and check Task Manager for stragglers) before retrying. "
                    + "The migration cannot run while the realm file is held open by another process.");
                Environment.Exit(1);
            }

            ProbeOutcome outcome;
            string probeError;
            try
            {
                (outcome, probeError) = probe(realmPath);
            }
            catch (Exception ex)
            {
                outcome = ProbeOutcome.AccessProblem;
                probeError = ex.Message;
            }

            switch (outcome)
            {
                case ProbeOutcome.AlreadyCompatible:
                    return;

                case ProbeOutcome.NeedsDowngrade:
                    promptAndRun(folder, realmPath);
                    return;

                case ProbeOutcome.AccessProblem:
                    // Probe couldn't open the realm in EITHER schema
                    // version. This used to be a silent return, which
                    // confused users who saw "no migration prompt" and
                    // assumed everything was fine — only to find
                    // their migration never actually ran. Surface it
                    // explicitly with actionable guidance.
                    showAccessProblemModal(realmPath, probeError);
                    Environment.Exit(1);
                    return;
            }
        }

        // -----------------------------------------------------------
        // Pre-swap recovery
        // -----------------------------------------------------------

        /// <summary>
        /// The atomic swap inside <see cref="osu.Game.Database.RealmDowngrader.RealmDowngradeRunner"/>
        /// moves the live realm to <c>&lt;realm&gt;.pre-swap</c>, then
        /// moves the rebuilt v51 file into the live path. If the
        /// process is killed between those two moves, we end up with
        /// no live realm. On next launch, restore from the pre-swap.
        /// This is a recovery for an extremely narrow window but the
        /// alternative is the user staring at an empty fresh osu!
        /// install with all their data hidden in a misnamed file.
        /// </summary>
        private static void recoverOrphanPreSwap(string realmPath)
        {
            string preSwap = realmPath + ".pre-swap";
            if (!File.Exists(preSwap))
                return;

            // If the live realm exists too, the swap probably finished
            // and someone forgot to clean up. Don't overwrite — log
            // and leave it for the user to inspect. (The runner's
            // post-success cleanup deletes pre-swap, so seeing one
            // here means SOMETHING went weird.)
            if (File.Exists(realmPath))
            {
                Logger.Log($"[RealmDowngrade] Found leftover '{preSwap}' alongside an existing live realm — leaving both for manual inspection.", LoggingTarget.Database, LogLevel.Important);
                return;
            }

            try
            {
                File.Move(preSwap, realmPath);
                Logger.Log($"[RealmDowngrade] Recovered orphan pre-swap file (interrupted swap from previous run): '{preSwap}' -> '{realmPath}'.", LoggingTarget.Database, LogLevel.Important);
            }
            catch (Exception ex)
            {
                Logger.Log($"[RealmDowngrade] Failed to recover orphan pre-swap: {ex.Message}. User must manually rename '{preSwap}' to '{realmPath}'.", LoggingTarget.Database, LogLevel.Error);
            }
        }

        // -----------------------------------------------------------
        // Conflicting-process detection
        // -----------------------------------------------------------

        /// <summary>
        /// Returns the name of the first conflicting osu!-family
        /// process that's currently running OTHER than ours, or null
        /// if the field is clear. The migration's atomic-swap step
        /// renames the realm file — that fails if anyone else has it
        /// open with a write lock, and Realm uses write locks for
        /// even read-only opens. So we have to be the only osu!
        /// process to migrate safely.
        /// </summary>
        private static string? detectConflictingProcess()
        {
            // Process names (no extension) that we treat as competing
            // osu! installs. Includes vanilla osu! lazer, current
            // Torii, and the legacy stable client just in case.
            string[] competitors =
            {
                "osu!", "osu", "osu-torii", "osulazer", "osu-lazer",
            };

            int self;
            try
            {
                self = System.Diagnostics.Process.GetCurrentProcess().Id;
            }
            catch
            {
                return null;
            }

            foreach (string name in competitors)
            {
                System.Diagnostics.Process[] procs;
                try
                {
                    procs = System.Diagnostics.Process.GetProcessesByName(name);
                }
                catch
                {
                    continue;
                }

                foreach (var p in procs)
                {
                    try
                    {
                        if (p.Id == self) continue;
                        // We don't try to verify they're actually
                        // touching the same realm folder — even if
                        // they aren't, the user's safer being told to
                        // close them first. Conservative-by-default.
                        return p.ProcessName;
                    }
                    catch
                    {
                        // Process exited mid-iteration; ignore.
                    }
                    finally
                    {
                        try { p.Dispose(); } catch { /* ignored */ }
                    }
                }
            }

            return null;
        }

        // -----------------------------------------------------------
        // Access-problem modal
        // -----------------------------------------------------------

        /// <summary>
        /// Block on a modal that explains we couldn't even probe the
        /// realm. Common causes are antivirus blocking unsigned
        /// builds (Smart App Control / Defender heuristics), the file
        /// being locked by another process we couldn't detect, or a
        /// genuinely corrupt realm. The message is intentionally long
        /// and explicit — the alternative was the previous behaviour
        /// (silent return), which left users with a non-running
        /// migration and no idea why.
        /// </summary>
        private static void showAccessProblemModal(string realmPath, string detail)
        {
            string body =
                "Torii detected a database that needs migration but couldn't open it for inspection.\n\n"
                + "Most common causes (in order of likelihood):\n"
                + "  1. Another osu! / Torii / lazer instance is still running. Open Task Manager and end every osu-related process, then retry.\n"
                + "  2. Windows Smart App Control or antivirus is blocking this build. If this is the unsigned test build, right-click osu-torii.exe -> Run as administrator.\n"
                + "  3. The realm file is on a cloud-synced folder (OneDrive, Google Drive, etc.) currently mid-sync. Pause sync and retry.\n"
                + "  4. The realm file is genuinely corrupt or unreadable. Restore from your most recent backup before retrying.\n\n"
                + "Realm path: " + realmPath + "\n"
                + "Underlying error: " + detail + "\n\n"
                + "The migration was NOT performed. Your existing data is untouched. Close this dialog to exit Torii without starting up.";

            Logger.Log("[RealmDowngrade] Access problem modal: " + detail, LoggingTarget.Database, LogLevel.Error);
            showMessageBox(SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR, "Torii — couldn't read your database", body);
        }

        // -----------------------------------------------------------
        // Probe — opens the realm read-only at v51 and decides what to
        // do based on the exception (if any).
        // -----------------------------------------------------------

        private enum ProbeOutcome
        {
            /// <summary>File opens at v51 cleanly, nothing to do.</summary>
            AlreadyCompatible,

            /// <summary>File is on a schema newer than 51 — needs downgrade.</summary>
            NeedsDowngrade,

            /// <summary>
            /// Couldn't open the file in EITHER schema version —
            /// access denied, lock contention, corrupt file, or some
            /// other Realm.NET error we can't classify. Caller MUST
            /// surface this to the user instead of silently skipping
            /// migration. The (string, string) tuple's second member
            /// carries a one-line description of the underlying error.
            /// </summary>
            AccessProblem,
        }

        private static (ProbeOutcome outcome, string error) probe(string realmPath)
        {
            // Two-stage probe so we don't have to rely on matching a
            // specific Realm.NET exception message string (the wording
            // differs across Realm releases and across Realm.NET vs
            // realm-core errors). The logic is just:
            //
            //   1. Try to open at v51. If that succeeds, the file is
            //      already on the schema we ship — nothing to do.
            //   2. Otherwise, try to open at v52 with the v52 schema
            //      (which includes SkinInfoV52). If that succeeds,
            //      the file is the legacy v52 shape and we know we
            //      can downgrade it.
            //   3. If neither attempt succeeds, the file is corrupt
            //      / unreadable / locked — let the normal startup
            //      path surface that error rather than triggering a
            //      misleading migration prompt.
            //
            // Both attempts are read-only so the source file is
            // guaranteed not to be modified by the probe.

            try
            {
                var v51Config = new RealmConfiguration(realmPath)
                {
                    IsReadOnly = true,
                    SchemaVersion = 51,
                    Schema = vanillaSchemaTypes(),
                };

                using (Realm.GetInstance(v51Config))
                    return (ProbeOutcome.AlreadyCompatible, string.Empty);
            }
            catch (Exception v51Ex)
            {
                Logger.Log($"[RealmDowngrade] v51 probe failed ({v51Ex.GetType().Name}): {v51Ex.Message}", LoggingTarget.Database);

                try
                {
                    var v52Config = new RealmConfiguration(realmPath)
                    {
                        IsReadOnly = true,
                        SchemaVersion = 52,
                        Schema = legacyV52SchemaTypes(),
                    };

                    using (Realm.GetInstance(v52Config))
                    {
                        Logger.Log("[RealmDowngrade] v52 probe succeeded — migration required.", LoggingTarget.Database);
                        return (ProbeOutcome.NeedsDowngrade, string.Empty);
                    }
                }
                catch (Exception v52Ex)
                {
                    // Neither version opens. Could be corrupt, locked,
                    // or some completely different shape. The earlier
                    // version of this code returned AlreadyCompatible
                    // here ("don't trigger a migration prompt"), but
                    // that left users staring at no UI feedback when
                    // an unsigned-build Defender block silently failed
                    // both probes. Now we return AccessProblem so the
                    // caller can surface it explicitly.
                    string detail = $"v51 probe: {v51Ex.GetType().Name} '{v51Ex.Message}'. v52 probe: {v52Ex.GetType().Name} '{v52Ex.Message}'.";
                    Logger.Log($"[RealmDowngrade] Both probes failed. {detail}", LoggingTarget.Database, LogLevel.Error);
                    return (ProbeOutcome.AccessProblem, detail);
                }
            }
        }

        /// <summary>
        /// Schema list for the legacy v52 probe. Mirrors the v52
        /// production schema with <see cref="SkinInfoV52"/> standing
        /// in for the SkinInfo of that era (which had a Pinned
        /// property; see <see cref="osu.Game.Database.RealmDowngrader.SkinInfoV52"/>).
        /// </summary>
        private static Type[] legacyV52SchemaTypes() => new[]
        {
            typeof(osu.Game.Beatmaps.BeatmapInfo),
            typeof(osu.Game.Beatmaps.BeatmapMetadata),
            typeof(osu.Game.Beatmaps.BeatmapSetInfo),
            typeof(osu.Game.Beatmaps.BeatmapDifficulty),
            typeof(osu.Game.Beatmaps.BeatmapUserSettings),
            typeof(osu.Game.Collections.BeatmapCollection),
            typeof(osu.Game.Input.Bindings.RealmKeyBinding),
            typeof(osu.Game.Configuration.RealmRulesetSetting),
            typeof(osu.Game.Models.RealmFile),
            typeof(osu.Game.Models.RealmNamedFileUsage),
            typeof(osu.Game.Models.RealmUser),
            typeof(osu.Game.Rulesets.RulesetInfo),
            typeof(osu.Game.Scoring.ScoreInfo),
            typeof(osu.Game.Database.RealmDowngrader.SkinInfoV52),
            typeof(osu.Game.Rulesets.Mods.ModPreset),
        };

        // -----------------------------------------------------------
        // UI flow
        // -----------------------------------------------------------

        private static void promptAndRun(string folder, string realmPath)
        {
            // Pre-migration explanation. SDL_ShowSimpleMessageBox blocks
            // until the user clicks OK or closes the window with the X
            // button. There's no real "cancel" — closing the window
            // just terminates the process, which is the same as not
            // running the migration. The dialog text makes that
            // explicit so users aren't surprised.
            unsafe
            {
                SDL3.SDL_ShowSimpleMessageBox(
                    SDL_MessageBoxFlags.SDL_MESSAGEBOX_WARNING,
                    "Torii — one-time database migration"u8,
                    "Your local osu! database is on a newer schema than this build of Torii expects.\n\n"u8
                    + "An earlier Torii update added a column that turned out to break vanilla osu! lazer's ability to open your folder. To restore that compatibility we need to rebuild your database now.\n\n"u8
                    + "BEFORE YOU CONTINUE — please read:\n"u8
                    + "  - There's a chance you'll need to re-open Torii AS ADMINISTRATOR for the migration to run. If this dialog disappears and nothing happens after clicking OK, that's the cause: close Torii, right-click osu-torii.exe -> Run as administrator.\n"u8
                    + "  - Make sure you have at least 2-3x the size of your client.realm file free on the drive holding your osu! folder. The migration creates a verified backup plus a working copy of the realm. (For most users this is well under 1 GB total.) If there isn't enough space, the migration will abort cleanly before touching anything.\n\n"u8
                    + "What this does:\n"u8
                    + "  - Creates an automatic backup before doing anything\n"u8
                    + "  - Rebuilds your database (~30 seconds for most libraries, up to a few minutes for very large ones)\n"u8
                    + "  - Preserves all your beatmaps, scores, skins, settings, and pinned skins\n\n"u8
                    + "When you click OK a small console window will open with live progress so you can see exactly what's happening. Don't close that window — it'll close on its own when the migration finishes.\n\n"u8
                    + "The migration cannot be skipped: Torii cannot start without it. To opt out, close this window with the X button."u8,
                    null);
            }

            // Surface a console window so the migration isn't a long
            // silent black hole — users have a real-time view of which
            // class is being copied and roughly how long is left. On
            // Windows this Allocates a fresh console attached to our
            // process; on macOS / Linux launching from a terminal
            // already gives us stdout, and launching from a GUI
            // launcher just sends the writes to nowhere (which is at
            // worst the same UX we'd had before this change).
            ProgressConsole.Open();

            try
            {
                var storage = new NativeStorage(folder);
                var runner = new RealmDowngradeRunner(storage, "client.realm")
                {
                    OnProgress = ProgressConsole.WriteLine,
                };
                var result = runner.Run();

                if (result.Success)
                {
                    ProgressConsole.WriteLine("");
                    ProgressConsole.WriteLine("Migration complete. You can close this console window — Torii will continue starting.");
                    showSuccess(result.BackupPath);
                    return;
                }

                ProgressConsole.WriteLine("");
                ProgressConsole.WriteLine($"Migration FAILED at phase: {result.StoppedAt}");
                ProgressConsole.WriteLine($"Error: {result.ErrorMessage}");
                if (!string.IsNullOrEmpty(result.ActionableHint))
                {
                    ProgressConsole.WriteLine("");
                    ProgressConsole.WriteLine("How to fix:");
                    ProgressConsole.WriteLine(result.ActionableHint);
                }
                showFailureAndExit(result.StoppedAt.ToString(), result.ErrorMessage ?? "(unknown error)", result.BackupPath, result.ActionableHint);
            }
            finally
            {
                ProgressConsole.Close();
            }
        }

        private static unsafe void showSuccess(string? backupPath)
        {
            // Only show the backup path if it actually exists on disk —
            // we never want to send the user looking for a file that
            // wasn't written.
            string backupLine = !string.IsNullOrEmpty(backupPath) && File.Exists(backupPath)
                ? $"A backup of the previous version was saved at:\n{backupPath}\n\nYou can keep this backup as a safety net, or delete it once you've confirmed everything works.\n\n"
                : string.Empty;

            string body =
                "Your database has been migrated successfully and Torii will continue starting up.\n\n"
                + backupLine
                + "HEADS UP — first launch may be slow:\n"
                + "After a migration, the next time osu! lazer (or Torii) opens, it may take 1-5 minutes to reach the main menu while it recalculates beatmap star ratings and rebuilds caches against the new schema. The window may look frozen during that time — DON'T close it. This only happens once, and only on the first launch after a migration.\n\n"
                + "Click OK to continue.";

            showMessageBox(SDL_MessageBoxFlags.SDL_MESSAGEBOX_INFORMATION, "Torii — migration complete", body);
        }

        private static unsafe void showFailureAndExit(string stoppedAt, string errorMessage, string? backupPath, string actionableHint)
        {
            // ONLY mention the backup path if the file genuinely exists.
            // The runner already validates this in RunResult.BackupPath,
            // but we double-check here so a stale property never sends
            // the user looking for a file that isn't there.
            bool backupActuallyExists = !string.IsNullOrEmpty(backupPath) && File.Exists(backupPath);

            string backupLine = backupActuallyExists
                ? $"An automatic backup of your original realm is at:\n{backupPath}\n\n"
                : "No backup was created — the migration failed before that step. Your original realm is untouched.\n\n";

            string hintLine = string.IsNullOrEmpty(actionableHint)
                ? string.Empty
                : "How to fix:\n" + actionableHint + "\n\n";

            string body =
                $"The database migration could not finish (stopped at {stoppedAt}).\n\n"
                + $"Error: {errorMessage}\n\n"
                + "Your original database is intact and was not modified.\n\n"
                + backupLine
                + hintLine
                + "After fixing the underlying issue, run Torii again to retry. Click OK to exit.";

            showMessageBox(SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR, "Torii — migration failed", body);

            Environment.Exit(1);
        }

        /// <summary>
        /// Wrapper around <c>SDL_ShowSimpleMessageBox</c> that takes
        /// runtime <see cref="string"/> values. The native API expects
        /// null-terminated UTF-8 byte pointers, so we encode + pin
        /// here. Used for the success / failure dialogs which contain
        /// dynamic content (backup paths, error messages); the
        /// up-front prompt uses u8 literals directly because nothing
        /// in it varies at runtime.
        /// </summary>
        private static unsafe void showMessageBox(SDL_MessageBoxFlags flags, string title, string body)
        {
            byte[] titleUtf8 = System.Text.Encoding.UTF8.GetBytes(title + "\0");
            byte[] bodyUtf8 = System.Text.Encoding.UTF8.GetBytes(body + "\0");

            fixed (byte* tp = titleUtf8)
            fixed (byte* bp = bodyUtf8)
            {
                SDL3.SDL_ShowSimpleMessageBox(flags, tp, bp, null);
            }
        }

        // -----------------------------------------------------------
        // Schema for the probe — must match the production v51 shape
        // (no SkinInfo.Pinned). We list the same classes RealmAccess
        // would auto-discover, just made explicit so this method
        // doesn't accidentally pull in unrelated assembly types.
        // -----------------------------------------------------------
        private static Type[] vanillaSchemaTypes() => new[]
        {
            typeof(osu.Game.Beatmaps.BeatmapInfo),
            typeof(osu.Game.Beatmaps.BeatmapMetadata),
            typeof(osu.Game.Beatmaps.BeatmapSetInfo),
            typeof(osu.Game.Beatmaps.BeatmapDifficulty),
            typeof(osu.Game.Beatmaps.BeatmapUserSettings),
            typeof(osu.Game.Collections.BeatmapCollection),
            typeof(osu.Game.Input.Bindings.RealmKeyBinding),
            typeof(osu.Game.Configuration.RealmRulesetSetting),
            typeof(osu.Game.Models.RealmFile),
            typeof(osu.Game.Models.RealmNamedFileUsage),
            typeof(osu.Game.Models.RealmUser),
            typeof(osu.Game.Rulesets.RulesetInfo),
            typeof(osu.Game.Scoring.ScoreInfo),
            typeof(osu.Game.Skinning.SkinInfo),
            typeof(osu.Game.Rulesets.Mods.ModPreset),
        };
    }
}
