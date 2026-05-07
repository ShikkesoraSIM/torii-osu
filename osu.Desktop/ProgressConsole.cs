// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace osu.Desktop
{
    /// <summary>
    /// Tiny shim around a Windows console window used during the
    /// realm migration to give the user live feedback. The migration
    /// itself can take minutes for big libraries, and a pure
    /// blocking SDL message box during that period would feel like
    /// the app is frozen — so we open a fresh console (via
    /// <c>AllocConsole</c> on Windows) and stream progress lines
    /// from the runner there.
    ///
    /// On macOS / Linux this is a no-op for window allocation; if
    /// the user launched from a terminal they already see stdout,
    /// and if they launched from a GUI we silently swallow the
    /// writes (no worse than the silent UX we had before this
    /// helper existed).
    /// </summary>
    internal static class ProgressConsole
    {
        private static bool consoleAllocated;
        private static bool tryWriteEnabled = true;

        public static void Open()
        {
            if (OperatingSystem.IsWindows())
                tryAllocateConsoleWindows();
            else
                tryWriteEnabled = true;

            // Banner so the user immediately knows what they're
            // looking at when the console pops up out of nowhere.
            WriteLine("==========================================================");
            WriteLine("  Torii — database migration in progress");
            WriteLine("  Don't close this window. It'll close on its own when");
            WriteLine("  the migration finishes.");
            WriteLine("==========================================================");
            WriteLine("");
        }

        public static void Close()
        {
            if (!consoleAllocated)
                return;

            try
            {
                WriteLine("");
                WriteLine("This window will close in 5 seconds...");

                // Brief pause so the final lines are visible before
                // the console disappears. Synchronous on purpose —
                // the migration is finished by the time this runs.
                System.Threading.Thread.Sleep(5000);

                if (OperatingSystem.IsWindows())
                    freeConsoleWindows();
            }
            catch
            {
                // Cleanup is best-effort. Anything thrown here would
                // just be noise on top of whatever the migration
                // result already told the user about.
            }
            finally
            {
                consoleAllocated = false;
            }
        }

        public static void WriteLine(string line)
        {
            if (!tryWriteEnabled)
                return;

            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // Stdout was disposed, redirected to a closed handle,
                // or the console subsystem refused. Stop trying so we
                // don't spam exceptions on every progress callback.
                tryWriteEnabled = false;
            }
        }

        // ----------------------------------------------------------
        // Windows P/Invoke
        // ----------------------------------------------------------
        [SupportedOSPlatform("windows")]
        private static void tryAllocateConsoleWindows()
        {
            try
            {
                if (AllocConsole())
                {
                    consoleAllocated = true;

                    // Re-bind System.Console's stdout/stderr to the
                    // fresh console handles. Without this, .NET caches
                    // the original (null) handles from before the
                    // console existed and Console.WriteLine writes
                    // into the void.
                    Console.SetOut(new System.IO.StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                    Console.SetError(new System.IO.StreamWriter(Console.OpenStandardError()) { AutoFlush = true });

                    try
                    {
                        Console.Title = "Torii — database migration";
                    }
                    catch
                    {
                        // Console.Title can throw on some Windows
                        // setups (no console host attached); ignore.
                    }

                    tryWriteEnabled = true;
                }
                else
                {
                    // AllocConsole returns false if a console is
                    // already attached (e.g. user launched from cmd).
                    // That's fine — the existing console will work,
                    // we just don't get to free it later.
                    tryWriteEnabled = true;
                }
            }
            catch
            {
                tryWriteEnabled = false;
            }
        }

        [SupportedOSPlatform("windows")]
        private static void freeConsoleWindows()
        {
            try { FreeConsole(); }
            catch { /* nothing useful to do — the process is ending soon anyway */ }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FreeConsole();
    }
}
