// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Runtime.Versioning;
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
        /// Used by the realm-downgrade CLI mode to find the user's
        /// actual realm without spinning up the full game host.
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

        [STAThread]
        public static void Main(string[] args)
        {
            // IMPORTANT DON'T IGNORE: For general sanity, velopack's setup needs to run before anything else.
            // This has bitten us in the rear before (bricked updater), and although the underlying issue from
            // last time has been fixed, let's not tempt fate.
            setupVelopack(args);

            // Realm downgrade CLI mode. Closes the app immediately after
            // running, so users invoke this when osu! is fully closed and
            // they want to make their realm vanilla-osu!-lazer-readable
            // again.
            //
            // Usage:
            //   osu!.exe --downgrade-realm-to-v51 [<folder>]
            //
            // If <folder> is omitted, the runner operates on the standard
            // osu! storage folder (Roaming/osu/ on Windows by default,
            // honouring storage.ini's CustomStoragePath if the user has
            // pointed Torii at vanilla's folder via the first-run wizard).
            //
            // The legacy flag --realm-downgrade-test <folder> is kept as
            // an internal alias for ad-hoc testing against a scratch
            // copy.
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--downgrade-realm-to-v51" || args[i] == "--realm-downgrade-test")
                {
                    string folder;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        folder = args[i + 1];
                    }
                    else
                    {
                        // Resolve the user's actual realm folder from the
                        // base game name + storage.ini's CustomStoragePath
                        // override, exactly the way OsuStorage does.
                        folder = ResolveDefaultRealmFolder();
                    }

                    Environment.Exit(RealmDowngradeCli.Run(folder));
                    return;
                }
            }

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

            // Detect a v52 realm left behind by an earlier Torii build and
            // walk the user through the in-process downgrade BEFORE any
            // realm-touching code runs. If the realm is already on v51 or
            // doesn't exist, this returns silently. If it's on v52 the
            // user gets an SDL message box, the migration runs, and on
            // success we continue with the rest of startup. On failure
            // RealmDowngradeStartupPrompt terminates the process with a
            // clear error dialog rather than letting RealmAccess crash
            // later with a confusing "schema too new" exception.
            try
            {
                RealmDowngradeStartupPrompt.RunIfNeeded(ResolveDefaultRealmFolder());
            }
            catch (Exception ex)
            {
                Logger.Log($"Realm downgrade startup prompt failed: {ex}", level: LogLevel.Important);
                // Fall through — let normal startup surface the issue if
                // the realm is genuinely on a too-new schema.
            }

            // NVIDIA profiles are based on the executable name of a process.
            // Lazer and stable share the same executable name.
            // Stable sets this setting to "Off", which may not be what we want, so let's force it back to the default "Auto" on startup.
            if (OperatingSystem.IsWindows())
                NVAPI.ThreadedOptimisations = NvThreadControlSetting.OGL_THREAD_CONTROL_DEFAULT;

            // Back up the cwd before DesktopGameHost changes it
            string cwd = Environment.CurrentDirectory;

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

                    host.Run(new OsuGameDesktop(args)
                    {
                        IsFirstRun = isFirstRun
                    });
                }
            }
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
