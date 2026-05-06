// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using NUnit.Framework;
using osu.Framework.Platform;
using osu.Game.Database.RealmDowngrader;

namespace osu.Game.Tests.Database
{
    /// <summary>
    /// Integration tests for the v52 → v51 realm downgrade. The
    /// "real-data" test is gated on an env var so it only runs when
    /// the developer explicitly provides a path — the file gets
    /// copied into a scratch directory before any work is done so
    /// the source is never touched.
    /// </summary>
    [TestFixture]
    public class TestRealmDowngradeRunner
    {
        /// <summary>
        /// Expects <c>TORII_REALM_DOWNGRADE_TEST_PATH</c> to point at a
        /// .realm file. The test copies it into a scratch directory and
        /// runs the runner against the copy. Use to validate against a
        /// real production realm without ever touching the original.
        /// </summary>
        [Test]
        public void RunAgainstRealRealm()
        {
            string? sourcePath = Environment.GetEnvironmentVariable("TORII_REALM_DOWNGRADE_TEST_PATH");

            if (string.IsNullOrEmpty(sourcePath))
            {
                Assert.Ignore("Set TORII_REALM_DOWNGRADE_TEST_PATH to a .realm file to enable this test.");
                return;
            }

            if (!File.Exists(sourcePath))
            {
                Assert.Fail($"Source realm not found: {sourcePath}");
                return;
            }

            string scratchDir = Path.Combine(Path.GetTempPath(), $"torii-realm-downgrade-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(scratchDir);

            try
            {
                string scratchRealm = Path.Combine(scratchDir, "client.realm");
                File.Copy(sourcePath, scratchRealm);

                Console.WriteLine($"Source: {sourcePath}");
                Console.WriteLine($"Scratch: {scratchRealm}");
                Console.WriteLine($"Source size: {new FileInfo(sourcePath).Length:N0} bytes");

                var storage = new NativeStorage(scratchDir);
                var runner = new RealmDowngradeRunner(storage, "client.realm");

                var result = runner.Run();

                Console.WriteLine($"Result: success={result.Success} stoppedAt={result.StoppedAt}");
                Console.WriteLine($"Backup: {result.BackupPath}");
                if (!string.IsNullOrEmpty(result.ErrorMessage))
                    Console.WriteLine($"Error: {result.ErrorMessage}");

                Console.WriteLine("Per-class counts (source -> dest):");
                foreach (var (className, (src, dst)) in result.Counts)
                {
                    string status = src == dst ? "OK" : "MISMATCH";
                    Console.WriteLine($"  [{status}] {className}: {src} -> {dst}");
                }

                Assert.That(result.Success, Is.True, $"Downgrade failed at {result.StoppedAt}: {result.ErrorMessage}");
                Assert.That(result.BackupPath, Is.Not.Null.And.Not.Empty);
                Assert.That(File.Exists(result.BackupPath!), "Backup file should exist after success.");

                // Verify resulting file size is comparable (not 0, not orders of magnitude off)
                long resultSize = new FileInfo(scratchRealm).Length;
                long sourceSize = new FileInfo(sourcePath).Length;
                Console.WriteLine($"Result size: {resultSize:N0} bytes ({resultSize * 100.0 / sourceSize:F1}% of source)");

                Assert.That(resultSize, Is.GreaterThan(sourceSize / 10), "Result file is suspiciously small.");
            }
            finally
            {
                Console.WriteLine($"Scratch directory left at: {scratchDir} for forensic inspection.");
                // Don't auto-delete — let the dev poke at the artifacts.
            }
        }
    }
}
