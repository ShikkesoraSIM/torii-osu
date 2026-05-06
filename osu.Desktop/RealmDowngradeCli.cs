// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Framework.Platform;
using osu.Game.Database.RealmDowngrader;

namespace osu.Desktop
{
    /// <summary>
    /// CLI entry point for ad-hoc testing of the v52 → v51 realm
    /// downgrade. Invoked when the desktop binary is launched with the
    /// <c>--realm-downgrade-test &lt;path-to-folder-containing-client.realm&gt;</c>
    /// flag. Operates on whatever <c>client.realm</c> is in that folder
    /// — caller is responsible for using a SCRATCH copy and not the
    /// real one.
    ///
    /// Bypasses the rest of osu!'s startup (no GameHost, no DI) so the
    /// test only exercises the downgrade pipeline itself.
    /// </summary>
    internal static class RealmDowngradeCli
    {
        public static int Run(string folder)
        {
            string realmPath = Path.Combine(folder, "client.realm");
            if (!File.Exists(realmPath))
            {
                Console.Error.WriteLine($"client.realm not found in {folder}");
                return 2;
            }

            Console.WriteLine($"Folder: {folder}");
            Console.WriteLine($"Realm: {realmPath} ({new FileInfo(realmPath).Length:N0} bytes)");

            var storage = new NativeStorage(folder);
            var runner = new RealmDowngradeRunner(storage, "client.realm");

            var result = runner.Run();

            Console.WriteLine();
            Console.WriteLine($"Result: success={result.Success} stoppedAt={result.StoppedAt}");
            if (!string.IsNullOrEmpty(result.BackupPath))
                Console.WriteLine($"Backup: {result.BackupPath}");
            if (!string.IsNullOrEmpty(result.ErrorMessage))
                Console.WriteLine($"Error: {result.ErrorMessage}");

            Console.WriteLine();
            Console.WriteLine("Per-class counts (source -> dest):");
            foreach (var (className, (src, dst)) in result.Counts)
            {
                string status = src == dst ? "OK" : "MISMATCH";
                Console.WriteLine($"  [{status}] {className}: {src} -> {dst}");
            }

            if (result.Success)
            {
                long resultSize = new FileInfo(realmPath).Length;
                Console.WriteLine();
                Console.WriteLine($"Final realm size: {resultSize:N0} bytes");
            }

            return result.Success ? 0 : 1;
        }
    }
}
