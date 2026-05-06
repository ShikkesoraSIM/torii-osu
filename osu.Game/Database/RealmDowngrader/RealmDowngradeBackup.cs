// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using osu.Framework.Logging;

namespace osu.Game.Database.RealmDowngrader
{
    /// <summary>
    /// Atomic, verifiable backup + restore primitive used by the v52→v51
    /// realm downgrade.
    ///
    /// What "atomic + verifiable" means here
    /// -------------------------------------
    /// 1. The backup is written to a temporary path first
    ///    (<c>client.realm.v52.bak.tmp.&lt;ts&gt;</c>), then renamed to
    ///    its final path. Rename is atomic on every common filesystem
    ///    so a power loss during the copy leaves either no backup or a
    ///    complete one — never a half-written file under the final name.
    /// 2. The SHA-256 of the source is computed BEFORE the copy and the
    ///    SHA-256 of the backup is computed AFTER the rename. They must
    ///    match exactly or the backup is rejected and the operation
    ///    aborts. We do not use the rename-result as proof of integrity.
    /// 3. We capture sibling Realm artifacts (<c>.lock</c>, <c>.note</c>,
    ///    <c>.management/</c>) into the same backup directory so a
    ///    full-state rollback is possible if any of those got into a
    ///    weird state during the operation. Realm itself recreates
    ///    these on next open if they're missing, so it's belt-and-
    ///    braces safety more than a hard requirement.
    /// 4. <see cref="Restore"/> is itself atomic via the same
    ///    write-temp-then-rename trick on the live path.
    ///
    /// File operations only — does not open any Realm. The downgrade
    /// runner is responsible for ensuring no Realm instance is holding
    /// the source file when this is invoked.
    /// </summary>
    public sealed class RealmDowngradeBackup
    {
        private readonly string sourceRealmPath;
        private readonly string backupRealmPath;
        private readonly List<string> siblingBackups = new List<string>();

        /// <summary>
        /// SHA-256 of the source realm at the moment <see cref="Create"/>
        /// completed. Available after a successful create; used by
        /// <see cref="Verify"/> to confirm the backup is uncorrupted.
        /// </summary>
        public string? SourceHash { get; private set; }

        /// <summary>
        /// Absolute path of the backup file. Available after a
        /// successful create.
        /// </summary>
        public string BackupPath => backupRealmPath;

        public RealmDowngradeBackup(string sourceRealmPath)
        {
            if (string.IsNullOrWhiteSpace(sourceRealmPath))
                throw new ArgumentException("Source realm path is required.", nameof(sourceRealmPath));

            this.sourceRealmPath = sourceRealmPath;

            string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
            backupRealmPath = sourceRealmPath + $".v52.bak.{timestamp}";
        }

        /// <summary>
        /// Creates the backup. On success, <see cref="SourceHash"/> and
        /// <see cref="BackupPath"/> are populated. On failure, throws —
        /// the caller MUST treat any exception as "no backup exists" and
        /// abort the downgrade.
        ///
        /// Any partial files left behind by a failure are cleaned up
        /// before the exception propagates.
        /// </summary>
        public void Create()
        {
            if (!File.Exists(sourceRealmPath))
                throw new FileNotFoundException("Realm file not found.", sourceRealmPath);

            // Defensive: if the destination already exists for any
            // reason (collision on the timestamp from a tight retry
            // loop), refuse rather than overwrite a possibly-good
            // existing backup.
            if (File.Exists(backupRealmPath))
                throw new IOException($"Backup target already exists: {backupRealmPath}. Refusing to overwrite.");

            string tempPath = backupRealmPath + ".tmp";

            try
            {
                // 1. Hash the source up-front so we have a fixed
                //    reference even if someone races us by writing to
                //    the source mid-copy (shouldn't happen — caller
                //    holds the realm closed — but verify defends
                //    against silent corruption).
                string sourceHash = computeSha256(sourceRealmPath);

                // 2. Stream-copy source -> temp. Using FileStream with
                //    a fresh handle on each side rather than
                //    File.Copy(...) to be explicit about flush + close
                //    semantics and to allow large-file progress later.
                using (var src = new FileStream(sourceRealmPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var dst = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    src.CopyTo(dst);
                    dst.Flush(flushToDisk: true);
                }

                // 3. Verify the temp matches before renaming. If the
                //    source got modified mid-copy or the disk lied
                //    about the write, this catches it.
                string tempHash = computeSha256(tempPath);
                if (!string.Equals(tempHash, sourceHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Backup hash mismatch: source={sourceHash}, copy={tempHash}");

                // 4. Atomic rename to the final path.
                File.Move(tempPath, backupRealmPath);

                // 5. Re-verify after the rename in case the rename
                //    itself caused any FS-level oddity. Cheap, paranoid.
                string finalHash = computeSha256(backupRealmPath);
                if (!string.Equals(finalHash, sourceHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Backup hash drifted after rename: expected={sourceHash}, found={finalHash}");

                // 6. Best-effort capture of sibling Realm artifacts.
                //    These are cheap to copy (mostly empty / kilobytes)
                //    and let us roll back to the EXACT pre-downgrade
                //    state, not just a structurally-equivalent one.
                copySiblingsBestEffort();

                SourceHash = sourceHash;

                Logger.Log($"[RealmDowngrade] Backup created: {backupRealmPath} ({new FileInfo(backupRealmPath).Length} bytes, sha256={sourceHash[..16]}...)", LoggingTarget.Database);
            }
            catch
            {
                // Clean up partial state on any failure so a retry
                // starts from a known-empty slate.
                tryDelete(tempPath);
                tryDelete(backupRealmPath);
                foreach (string sibling in siblingBackups)
                    tryDelete(sibling);
                siblingBackups.Clear();
                SourceHash = null;
                throw;
            }
        }

        /// <summary>
        /// Re-verifies that the backup file still matches the hash
        /// captured at create time. Use this immediately before doing
        /// anything destructive to the source — it's cheap, and if it
        /// fails the backup is no longer trustworthy and the operation
        /// must abort.
        /// </summary>
        public void Verify()
        {
            if (SourceHash == null)
                throw new InvalidOperationException("Backup has not been successfully created yet.");

            if (!File.Exists(backupRealmPath))
                throw new FileNotFoundException("Backup file is gone.", backupRealmPath);

            string current = computeSha256(backupRealmPath);
            if (!string.Equals(current, SourceHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Backup integrity check failed: expected={SourceHash}, found={current}");
        }

        /// <summary>
        /// Restores the backup over the live realm path. Atomic: writes
        /// to a temp first then renames. On failure, the live path is
        /// left in whatever state it was — the backup itself is never
        /// modified by this operation.
        ///
        /// Returns true if the restore completed and the post-restore
        /// hash matches the captured backup hash.
        /// </summary>
        public bool Restore()
        {
            if (SourceHash == null)
                throw new InvalidOperationException("Backup has not been successfully created yet.");

            Verify();

            string tempPath = sourceRealmPath + ".restore.tmp";

            try
            {
                using (var src = new FileStream(backupRealmPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    src.CopyTo(dst);
                    dst.Flush(flushToDisk: true);
                }

                string tempHash = computeSha256(tempPath);
                if (!string.Equals(tempHash, SourceHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Restore intermediate hash mismatch: expected={SourceHash}, copy={tempHash}");

                // Move the existing source out of the way so we can
                // atomically swap. If the new file's rename succeeds
                // we delete this; if it fails we put it back.
                string displacedPath = sourceRealmPath + ".pre-restore";
                if (File.Exists(displacedPath))
                    File.Delete(displacedPath);

                if (File.Exists(sourceRealmPath))
                    File.Move(sourceRealmPath, displacedPath);

                try
                {
                    File.Move(tempPath, sourceRealmPath);
                }
                catch
                {
                    // Restore the displaced original on failure.
                    if (File.Exists(displacedPath))
                        File.Move(displacedPath, sourceRealmPath);
                    throw;
                }

                tryDelete(displacedPath);

                string restoredHash = computeSha256(sourceRealmPath);
                bool ok = string.Equals(restoredHash, SourceHash, StringComparison.OrdinalIgnoreCase);

                Logger.Log(ok
                    ? $"[RealmDowngrade] Restored backup successfully ({SourceHash[..16]}...)"
                    : $"[RealmDowngrade] Restore hash mismatch: expected={SourceHash}, got={restoredHash}", LoggingTarget.Database);

                return ok;
            }
            finally
            {
                tryDelete(tempPath);
            }
        }

        private void copySiblingsBestEffort()
        {
            // .lock and .note are tiny status files Realm rebuilds on
            // its own. .management/ is a directory. We capture them as
            // a defensive measure — none of them are essential for the
            // realm's data, but a perfectly clean rollback preserves
            // whatever in-flight state Realm had when we shut it down.
            string[] siblingSuffixes = { ".note", ".lock" };

            foreach (string suffix in siblingSuffixes)
            {
                string siblingSrc = sourceRealmPath + suffix;
                if (!File.Exists(siblingSrc))
                    continue;

                string siblingDst = backupRealmPath + suffix;
                try
                {
                    File.Copy(siblingSrc, siblingDst, overwrite: false);
                    siblingBackups.Add(siblingDst);
                }
                catch (Exception e)
                {
                    Logger.Log($"[RealmDowngrade] Skipped sibling {suffix} backup: {e.Message}", LoggingTarget.Database, LogLevel.Verbose);
                }
            }

            string mgmtSrc = sourceRealmPath + ".management";
            string mgmtDst = backupRealmPath + ".management";

            if (Directory.Exists(mgmtSrc))
            {
                try
                {
                    Directory.CreateDirectory(mgmtDst);
                    foreach (string file in Directory.GetFiles(mgmtSrc))
                    {
                        File.Copy(file, Path.Combine(mgmtDst, Path.GetFileName(file)), overwrite: false);
                    }
                    siblingBackups.Add(mgmtDst);
                }
                catch (Exception e)
                {
                    Logger.Log($"[RealmDowngrade] Skipped .management/ backup: {e.Message}", LoggingTarget.Database, LogLevel.Verbose);
                }
            }
        }

        private static string computeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                byte[] hash = sha.ComputeHash(stream);
                return string.Concat(hash.Select(b => b.ToString("x2")));
            }
        }

        private static void tryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
                else if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
            }
            catch (Exception e)
            {
                Logger.Log($"[RealmDowngrade] Cleanup of {path} failed (non-fatal): {e.Message}", LoggingTarget.Database, LogLevel.Verbose);
            }
        }
    }
}
