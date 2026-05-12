// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.IO;

namespace Torii.Shim
{
    /// <summary>
    /// Legacy launcher shim for <c>osu-torii.exe</c> → <c>torii.exe</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// During the May 2026 rebrand we renamed the primary binary from
    /// <c>osu-torii.exe</c> to <c>torii.exe</c> (peppy explicitly asked
    /// forks to drop the "osu!" name from distributed binaries). Users
    /// with the old name pinned to their taskbar / desktop / Start menu
    /// would have a dead shortcut after auto-update.
    /// </para>
    /// <para>
    /// This program is that old name. It locates <c>torii.exe</c> in
    /// the same directory it was launched from, forwards every CLI
    /// argument verbatim, and exits before the GC even warms. Users
    /// never see a console window; the only visible effect is that
    /// pinning to the OLD shortcut still opens the game.
    /// </para>
    /// <para>
    /// Important behaviour details:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// We do <strong>NOT</strong> wait for <c>torii.exe</c> to exit.
    /// Velopack auto-update + the realm-downgrade CLI mode both expect
    /// the bootstrapper to return control quickly so their own
    /// process management works. Spawning and detaching matches the
    /// behaviour of the prior native bootstrapper.
    /// </item>
    /// <item>
    /// Working directory is preserved (<see cref="ProcessStartInfo.WorkingDirectory"/>)
    /// so realm path resolution / storage.ini handling continues to
    /// work — those paths are computed relative to CWD when the
    /// user picks a custom osu! folder via the first-run wizard.
    /// </item>
    /// <item>
    /// On Windows we use <c>UseShellExecute = false</c> so the
    /// spawned process inherits our integrity level + console
    /// handles cleanly. The shim itself uses <c>WinExe</c> so there
    /// is no console window to inherit.
    /// </item>
    /// <item>
    /// If <c>torii.exe</c> is missing (e.g. corrupted install) we
    /// fall through to a one-line stderr message and exit 1 so a
    /// debugger / user looking at exit codes can tell what happened.
    /// We deliberately do <strong>NOT</strong> pop a MessageBox —
    /// adding a UI dependency to a 10-line shim defeats the point.
    /// Velopack's own error reporting will pick this up via the
    /// update-failed code path on the next launch attempt.
    /// </item>
    /// </list>
    /// </remarks>
    internal static class Program
    {
        private const string TARGET_BINARY = "torii.exe";

        private static int Main(string[] args)
        {
            string baseDir = AppContext.BaseDirectory;
            string target = Path.Combine(baseDir, TARGET_BINARY);

            if (!File.Exists(target))
            {
                Console.Error.WriteLine(
                    $"Torii: legacy launcher could not find '{TARGET_BINARY}' next to itself.\n"
                  + $"  Expected at: {target}\n"
                  + $"  This usually means the install is corrupted. Re-run the Torii installer."
                );
                return 1;
            }

            var psi = new ProcessStartInfo
            {
                FileName = target,
                WorkingDirectory = baseDir,
                UseShellExecute = false,
                CreateNoWindow = false,
            };

            // Forward every argument verbatim. ProcessStartInfo's
            // ArgumentList handles quoting / escaping correctly per
            // platform, so we don't have to worry about args with
            // spaces, quotes, or shell metacharacters.
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);

            try
            {
                using var proc = Process.Start(psi);
                // Detach immediately. The shim's job is done — torii.exe
                // owns the lifecycle from here.
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Torii: legacy launcher failed to start '{TARGET_BINARY}': {ex.Message}"
                );
                return 1;
            }
        }
    }
}
