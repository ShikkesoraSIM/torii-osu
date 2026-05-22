// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using osu.Desktop.LegacyIpc;
using osu.Desktop.Windows;
using osu.Desktop.LowLatency;
using osu.Framework;
using osu.Framework.Development;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game;
using osu.Game.IPC;
using osu.Game.Tournament;
using SDL;
using Velopack;

namespace osu.Desktop
{
    public static class Program
    {
#if DEBUG
        private const string base_game_name = @"osu-torii-development";
#else
        private const string base_game_name = @"osu-torii";
#endif

        /// <summary>
        /// Compute the path to the folder containing the active
        /// <c>client.realm</c> for this Torii install, mirroring what
        /// osu.Framework's GameHost + osu! Game's OsuStorage would
        /// produce — Roaming/{gameName} on Windows, with
        /// <c>storage.ini</c>'s <c>FullPath</c> override applied if the
        /// user pointed Torii at the vanilla osu! folder via the
        /// first-run wizard.
        ///
        /// Used by the pre-host SDL3-backend ini read and the legacy
        /// ReleaseStream value migration to locate the user's actual
        /// realm folder without spinning up the full game host.
        /// </summary>
        private static string ResolveDefaultRealmFolder()
        {
            string defaultFolder;

            if (OperatingSystem.IsWindows())
                defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), base_game_name);
            else if (OperatingSystem.IsMacOS())
                defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), base_game_name);
            else
                defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), base_game_name);

            // storage.ini override — the first-run wizard writes
            // FullPath = ... when the user points Torii at an existing
            // osu! folder. We have to honour it because that's where
            // client.realm actually lives in that case.
            string storageIni = Path.Combine(defaultFolder, "storage.ini");
            if (File.Exists(storageIni))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(storageIni))
                    {
                        string trimmed = line.Trim();
                        if (trimmed.StartsWith("FullPath", StringComparison.OrdinalIgnoreCase))
                        {
                            int eq = trimmed.IndexOf('=');
                            if (eq > 0)
                            {
                                string custom = trimmed[(eq + 1)..].Trim();
                                if (!string.IsNullOrEmpty(custom) && Directory.Exists(custom))
                                    return custom;
                            }
                        }
                    }
                }
                catch
                {
                    // Best-effort — fall through to default folder.
                }
            }

            return defaultFolder;
        }

        private static LegacyTcpIpcProvider? legacyIpc;

        private static bool isFirstRun;

        /// <summary>
        /// Read the persisted <c>ForceSDL3</c> setting straight from the
        /// on-disk <c>game.ini</c> without spinning up a full
        /// OsuConfigManager. Used at the very top of <see cref="Main"/>
        /// because the framework's <see cref="FrameworkEnvironment.UseSDL3"/>
        /// is a one-shot static-readonly: by the time GameHost is alive,
        /// the SDL2-vs-SDL3 decision has already been baked in. The only
        /// way to flip it is to set the OSU_SDL3 env var BEFORE any
        /// framework code runs, which means we have to peek the user's
        /// preference using just the file system.
        /// </summary>
        /// <remarks>
        /// We accept the small amount of duplicated parsing logic over
        /// instantiating Storage + OsuConfigManager twice (once here,
        /// once again when the host comes up). The .ini format is
        /// trivial (<c>key = value</c> per line) and the file is a few
        /// KB at most.
        /// </remarks>
        private static bool ReadForceSDL3FromIni(string storageFolder)
        {
            string iniPath = Path.Combine(storageFolder, "game.ini");
            if (!File.Exists(iniPath))
                return false;

            try
            {
                foreach (string rawLine in File.ReadAllLines(iniPath))
                {
                    string line = rawLine.Trim();
                    if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                        continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string key = line[..eq].Trim();
                    if (!string.Equals(key, "ForceSDL3", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string value = line[(eq + 1)..].Trim();
                    return value.Equals("1", StringComparison.Ordinal)
                           || value.Equals("True", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                // If we can't read the ini for any reason (perms, partial
                // write mid-flight, locked by another process), fall back
                // to the framework default for this platform — i.e. don't
                // force SDL3.
            }

            return false;
        }

        /// <summary>
        /// Pre-init migration for the <c>ReleaseStream</c> config key.
        /// Pre-May-2026 builds persisted the enum values <c>Lazer</c> and
        /// <c>Tachyon</c>; the May 2026 rename mapped these to
        /// <c>Torii</c> (stable) and <c>Nova</c> (experimental). The
        /// framework's bindable load throws ArgumentException when it
        /// can't parse an unknown enum value, which surfaces as a
        /// recurring "Unable to parse config key ReleaseStream"
        /// notification at every launch.
        /// </summary>
        /// <remarks>
        /// We rewrite the config file in place before OsuConfigManager
        /// gets a chance to read it. The format is the trivial
        /// <c>key = value</c> per line we already parse for ForceSDL3,
        /// so this is a small line-scan + string-replace. Idempotent:
        /// a file already containing the new values is left untouched.
        ///
        /// Future enum renames in this file should add their own line
        /// here; the migration list is intentionally short + explicit
        /// rather than a generic "all unknown enum values reset to
        /// default" so that an enum rename in a *different* config key
        /// can't accidentally silently clobber a user's setting.
        /// </remarks>
        private static void migrateLegacyReleaseStream(string storageFolder)
        {
            string iniPath = Path.Combine(storageFolder, "osu!.cfg");
            if (!File.Exists(iniPath))
                return;

            string[] lines = File.ReadAllLines(iniPath);
            bool changed = false;

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();
                if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
                    continue;

                int eq = trimmed.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = trimmed[..eq].Trim();
                if (!string.Equals(key, "ReleaseStream", StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = trimmed[(eq + 1)..].Trim();
                string? mapped = value switch
                {
                    "Lazer"   => "Torii",
                    "Tachyon" => "Nova",
                    _         => null,
                };

                if (mapped == null)
                    continue;

                // Preserve original indentation / spacing around the `=`
                // by reconstructing from the position of the `=` in the
                // raw (untrimmed) line, so the file diff is minimal.
                int rawEq = lines[i].IndexOf('=');
                string prefix = lines[i][..(rawEq + 1)];
                lines[i] = $"{prefix} {mapped}";
                changed = true;
                Logger.Log($"[Torii] Migrated legacy ReleaseStream={value} → {mapped} in osu!.cfg");
            }

            if (changed)
                File.WriteAllLines(iniPath, lines);
        }

        [STAThread]
        public static void Main(string[] args)
        {
            // IMPORTANT DON'T IGNORE: For general sanity, velopack's setup needs to run before anything else.
            // This has bitten us in the rear before (bricked updater), and although the underlying issue from
            // last time has been fixed, let's not tempt fate.
            setupVelopack(args);

            if (OperatingSystem.IsWindows())
            {
                var windowsVersion = Environment.OSVersion.Version;

                // While .NET 8 only supports Windows 10 and above, running on Windows 7/8.1 may still work. We are limited by realm currently, as they choose to only support 8.1 and higher.
                // See https://www.mongodb.com/docs/realm/sdk/dotnet/compatibility/
                if (windowsVersion.Major < 6 || (windowsVersion.Major == 6 && windowsVersion.Minor <= 2))
                {
                    unsafe
                    {
                        // If users running in compatibility mode becomes more of a common thing, we may want to provide better guidance or even consider
                        // disabling it ourselves.
                        // We could also better detect compatibility mode if required:
                        // https://stackoverflow.com/questions/10744651/how-i-can-detect-if-my-application-is-running-under-compatibility-mode#comment58183249_10744730
                        SDL3.SDL_ShowSimpleMessageBox(SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR,
                            "Your operating system is too old to run osu!"u8,
                            "This version of osu! requires at least Windows 8.1 to run.\n"u8
                            + "Please upgrade your operating system or consider using an older version of osu!.\n\n"u8
                            + "If you are running a newer version of windows, please check you don't have \"Compatibility mode\" turned on for osu!"u8, null);
                        return;
                    }
                }
            }

            // NVIDIA profiles are based on the executable name of a process.
            // Lazer and stable share the same executable name.
            // Stable sets this setting to "Off", which may not be what we want, so let's force it back to the default "Auto" on startup.
            if (OperatingSystem.IsWindows())
                NVAPI.ThreadedOptimisations = NvThreadControlSetting.OGL_THREAD_CONTROL_DEFAULT;

            // Back up the cwd before DesktopGameHost changes it
            string cwd = Environment.CurrentDirectory;

            // Honour the user's "Force SDL3" setting before the host comes
            // up. FrameworkEnvironment.UseSDL3 is a one-shot static-readonly
            // that's evaluated the first time anything in osu-framework
            // touches it; setting OSU_SDL3 here is the only way to flip the
            // backend without recompiling the framework. No-op on Windows /
            // mobile where SDL3 is already unconditional. The "user toggled
            // this off" path is also covered: if false, we don't touch the
            // env var at all so any external override (e.g. someone manually
            // exported OSU_SDL3=1 in their shell) still wins.
            //
            // We do this BEFORE Host.GetSuitableDesktopHost() instantiates
            // the game host — once the host comes up the SDL3 vs legacy
            // backend selection is locked in.
            try
            {
                if (!OperatingSystem.IsWindows() && ReadForceSDL3FromIni(ResolveDefaultRealmFolder()))
                {
                    Environment.SetEnvironmentVariable("OSU_SDL3", "1");
                    Logger.Log("[Torii] OSU_SDL3=1 set from ForceSDL3 setting; SDL3 backend will be used.");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[Torii] Failed to read ForceSDL3 setting: {ex.Message}");
                // Fall through with framework default.
            }

            // ReleaseStream legacy-value migration. Before May 2026 the
            // enum was { Lazer, Tachyon }; we renamed to { Torii, Nova }.
            // Users updating from a pre-rename build have
            // `ReleaseStream = Lazer` (or `Tachyon`) persisted in
            // `osu!.cfg`, which the framework's bindable loader can't
            // parse — it logs an ArgumentException and toasts an
            // ugly "Unable to parse config key ReleaseStream"
            // notification at every launch until the user happens to
            // change the value through the dropdown.
            //
            // Rewrite the file in-place before OsuConfigManager sees it,
            // mapping Lazer → Torii and Tachyon → Nova. This makes the
            // migration silent and one-shot: subsequent launches see the
            // already-correct values and skip the rewrite.
            try
            {
                migrateLegacyReleaseStream(ResolveDefaultRealmFolder());
            }
            catch (Exception ex)
            {
                Logger.Log($"[Torii] ReleaseStream legacy migration failed: {ex.Message}");
                // Non-fatal — framework's own fallback will kick in and the
                // user just sees the existing parse-failed notification.
            }

            string gameName = base_game_name;
            bool tournamentClient = false;

            foreach (string arg in args)
            {
                string[] split = arg.Split('=');

                string key = split[0];
                string val = split.Length > 1 ? split[1] : string.Empty;

                switch (key)
                {
                    case "--tournament":
                        tournamentClient = true;
                        break;

                    case "--debug-client-id":
                        if (!DebugUtils.IsDebugBuild)
                            throw new InvalidOperationException("Cannot use this argument in a non-debug build.");

                        if (!int.TryParse(val, out int clientID))
                            throw new ArgumentException("Provided client ID must be an integer.");

                        gameName = $"{base_game_name}-{clientID}";
                        break;
                }
            }

            var hostOptions = new HostOptions
            {
                IPCPipeName = !tournamentClient ? OsuGame.IPC_PIPE_NAME : null,
                FriendlyGameName = OsuGameBase.GAME_NAME,
            };

            using (DesktopGameHost host = Host.GetSuitableDesktopHost(gameName, hostOptions))
            {
                if (!host.IsPrimaryInstance)
                {
                    if (trySendIPCMessage(host, cwd, args))
                        return;

                    // we want to allow multiple instances to be started when in debug.
                    if (!DebugUtils.IsDebugBuild)
                    {
                        Logger.Log(@"osu! does not support multiple running instances.", LoggingTarget.Runtime, LogLevel.Error);
                        return;
                    }
                }

                if (host.IsPrimaryInstance)
                {
                    try
                    {
                        Logger.Log("Starting legacy IPC provider...");
                        legacyIpc = new LegacyTcpIpcProvider();
                        legacyIpc.Bind();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to start legacy IPC provider");
                    }
                }

                if (tournamentClient)
                    host.Run(new TournamentGame());
                else
                {
                    // Initialize low latency provider based on GPU vendor
                    if (NVAPI.Available)
                    {
                        host.SetLowLatencyProvider(new NVAPIDirect3D11LowLatencyProvider());
                        Logger.Log("NVIDIA Reflex low latency provider initialized.");
                    }
                    else if (AMDAPI.Available)
                    {
                        if (AMDAPI.HasAntiLag2Support)
                        {
                            host.SetLowLatencyProvider(new AMDAntiLag2Direct3D11LowLatencyProvider());
                            Logger.Log($"AMD Anti-Lag 2 low latency provider initialized for {AMDAPI.GPUName}.");
                        }
                        else
                        {
                            Logger.Log($"AMD GPU detected ({AMDAPI.GPUName}) but Anti-Lag 2 is not available. This requires AMD RDNA 1-based products (RX 5000 Series and newer) with recent drivers containing amd_antilag_dx11.dll.");
                        }
                    }
                    else
                    {
                        Logger.Log("No compatible low latency provider available (requires NVIDIA or AMD GPU with recent drivers).");
                    }

                    // Torii: shutdown watchdog. Fires when the host's
                    // ExitRequested event triggers (user clicked Exit, X
                    // button, Alt+F4, etc.). Waits 5 seconds for the
                    // natural shutdown chain to complete, then force-
                    // terminates the process regardless of state.
                    //
                    // This is a stronger safety net than the
                    // Environment.Exit at the end of Main: that one only
                    // fires AFTER host.Run() returns, so if the game
                    // thread hangs DURING the exit sequence (e.g., a
                    // screen-stack unwind deadlock, an async load that
                    // doesn't complete, a Wait on a Task that never
                    // finishes), host.Run never returns and the post-Run
                    // Exit never fires. This watchdog runs on a separate
                    // background thread so it's immune to whatever the
                    // game thread is doing.
                    //
                    // 5 seconds is more than enough for a healthy
                    // shutdown (typically <500ms after exit click); on
                    // a stuck shutdown the user gets back to their
                    // desktop within 5 seconds instead of having to
                    // open Task Manager to kill the process.
                    host.ExitRequested += () =>
                    {
                        Task.Run(async () =>
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                            try
                            {
                                Logger.Log("[Shutdown watchdog] host.Run did not complete within 5s of ExitRequested; force-terminating process.", LoggingTarget.Runtime, LogLevel.Important);
                            }
                            catch
                            {
                                // Logger may already be torn down. Don't let that stop the exit.
                            }

                            // Round 5: use Process.Kill instead of Environment.Exit.
                            // Remi tested v2026.522.7-nova (round 3) and the log
                            // shows "Host execution state changed to Stopped"
                            // followed by silence — host fully stopped, BUT the
                            // process still didn't terminate.
                            //
                            // Working theory: Environment.Exit(0) runs finalizers
                            // before exiting, and a finalizer in some native
                            // interop (Realm Core scheduler, BASS native audio,
                            // etc.) was hanging indefinitely. Environment.Exit
                            // waits for that finalizer to complete, which never
                            // happens, so the process stays alive forever.
                            //
                            // Process.Kill bypasses finalizers entirely — it's
                            // the OS-level equivalent of Task Manager's "End
                            // Task". By the time the watchdog fires (5s after
                            // exit), the user has already had ample opportunity
                            // for graceful cleanup. If we're still alive at
                            // that point, just kill the process.
                            try
                            {
                                System.Diagnostics.Process.GetCurrentProcess().Kill();
                            }
                            catch
                            {
                                // Fallback: Exit if Kill somehow fails. Don't
                                // let anything stop the shutdown.
                                Environment.Exit(0);
                            }
                        });
                    };

                    host.Run(new OsuGameDesktop(args)
                    {
                        IsFirstRun = isFirstRun
                    });
                }
            }

            // Torii: secondary kill watchdog. Fires 2 seconds from now,
            // independent of whether Environment.Exit (below) succeeds or
            // hangs in a finalizer. If the finalizers complete cleanly
            // first, Process.Kill is a no-op (process already gone). If
            // they hang, this kills the process at 2s instead of forcing
            // the user to wait the full 5s for the ExitRequested watchdog.
            //
            // The "host fully stopped but process won't exit" path Remi
            // hits goes: user clicks Exit → host completes shutdown in
            // ~500ms → host.Run returns → Environment.Exit(0) called →
            // finalizer hangs. With just the ExitRequested watchdog the
            // process dies at 5s. With this second watchdog it dies at
            // ~2.5s. Half the wait for the same robustness.
            Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                try { System.Diagnostics.Process.GetCurrentProcess().Kill(); } catch { }
            });

            // Torii: force termination after the host has been disposed.
            // The `using` block above triggers host.Dispose() which runs the
            // game's IDisposable chain (network clients close, file writers
            // flush, realm closes its update-thread instance, BASS frees
            // devices, etc.). After that, returning from Main would leave
            // the CLR waiting for any non-background thread to finish
            // before terminating the process.
            //
            // In practice, native interop threads spawned by Realm Core's
            // scheduler, BASS audio (especially WASAPI exclusive mode), and
            // a couple of other libraries don't always get torn down (or
            // aren't marked as background threads), which leaves torii.exe
            // pinned at ~5% CPU on the user's task list after they clicked
            // Exit, with online presence stuck "online" because the OS
            // hasn't reaped the sockets yet either. Reported by Remi on
            // RDNA 4 / WASAPI / wireless headset — wouldn't terminate even
            // after 60+ seconds.
            //
            // Environment.Exit(0) calls finalizers briefly and then exits,
            // guaranteeing the process disappears from task manager when
            // the user expects it to. The game-state shutdown already
            // completed above, so this is purely a "kill the remaining
            // leaked native threads" safety net, not a way to skip cleanup.
            Environment.Exit(0);
        }

        private static bool trySendIPCMessage(IIpcHost host, string cwd, string[] args)
        {
            if (args.Length == 1 && args[0].StartsWith(OsuGameBase.OSU_PROTOCOL, StringComparison.Ordinal))
            {
                var osuSchemeLinkHandler = new OsuSchemeLinkIPCChannel(host);
                if (!osuSchemeLinkHandler.HandleLinkAsync(args[0]).Wait(3000))
                    throw new IPCTimeoutException(osuSchemeLinkHandler.GetType());

                return true;
            }

            if (args.Length > 0 && args[0].Contains('.')) // easy way to check for a file import in args
            {
                var importer = new ArchiveImportIPCChannel(host);

                foreach (string file in args)
                {
                    Console.WriteLine(@"Importing {0}", file);
                    if (!importer.ImportAsync(Path.GetFullPath(file, cwd)).Wait(3000))
                        throw new IPCTimeoutException(importer.GetType());
                }

                return true;
            }

            return false;
        }

        private static void setupVelopack(string[] args)
        {
            // Arguments being present indicate the user is either starting the game in a special (aka tournament) mode,
            // or is running with pending imports via file association or otherwise.
            //
            // In both these scenarios, we'd hope the game does not attempt to update.
            //
            // Special consideration for velopack startup arguments, which must be handled during update.
            // See https://docs.velopack.io/integrating/hooks#command-line-hooks.
            if (args.Length > 0 && !args[0].StartsWith("--velo", StringComparison.Ordinal))
            {
                Logger.Log("Handling arguments, skipping velopack setup.");
                return;
            }

            if (OsuGameDesktop.IsPackageManaged)
            {
                Logger.Log("Updates are being managed by an external provider. Skipping Velopack setup.");
                return;
            }

            var app = VelopackApp.Build();

            app.OnFirstRun(_ => isFirstRun = true);

            if (OperatingSystem.IsWindows())
                configureWindows(app);

            app.Run();
        }

        [SupportedOSPlatform("windows")]
        private static void configureWindows(VelopackApp app)
        {
            app.OnFirstRun(_ => WindowsAssociationManager.InstallAssociations());
            app.OnAfterUpdateFastCallback(_ => WindowsAssociationManager.UpdateAssociations());
            app.OnBeforeUninstallFastCallback(_ => WindowsAssociationManager.UninstallAssociations());
        }
    }
}
