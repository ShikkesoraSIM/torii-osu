// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Runtime.Versioning;
using osu.Desktop.LegacyIpc;
using osu.Desktop.Windows;
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
        private const string base_game_name = @"osu-development";
#else
        // toriirefresh: isolated data directory so this fork never touches the
        // stock osu!lazer library (%APPDATA%/osu). Will become the Torii data
        // dir as the project matures.
        private const string base_game_name = @"osu-toriirefresh";
#endif

        /// <summary>
        /// Compute the path to the folder containing this install's storage,
        /// mirroring what osu.Framework's GameHost would produce
        /// (Roaming/{gameName} on Windows + macOS, LocalAppData/{gameName}
        /// elsewhere), with storage.ini's FullPath override honoured if the
        /// user pointed the game at an existing folder via the first-run wizard.
        /// Used by the pre-host SDL3 ini read to locate game.ini without
        /// spinning up the full game host.
        /// </summary>
        private static string resolveDefaultStorageFolder()
        {
            string defaultFolder;

            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), base_game_name);
            else
                defaultFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), base_game_name);

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
                    // Best-effort - fall through to the default folder.
                }
            }

            return defaultFolder;
        }

        /// <summary>
        /// Read the persisted ForceSDL3 setting straight from the on-disk
        /// game.ini without spinning up OsuConfigManager. Used at the top of
        /// Main because FrameworkEnvironment.UseSDL3 is a one-shot
        /// static-readonly: by the time the host is alive the SDL2-vs-SDL3
        /// decision is already baked in. The only way to flip it is to set
        /// OSU_SDL3 BEFORE any framework code runs, so we peek the user's
        /// preference using just the file system.
        /// </summary>
        private static bool readForceSDL3FromIni(string storageFolder)
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
                // If we can't read the ini for any reason, fall back to the
                // framework default for this platform (i.e. don't force SDL3).
            }

            return false;
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

            // Honour the user's "Force SDL3" setting before the host comes up.
            // FrameworkEnvironment.UseSDL3 is a one-shot static-readonly that's
            // evaluated the first time osu-framework touches it; setting
            // OSU_SDL3 here is the only way to flip the backend without
            // recompiling the framework. No-op on Windows/mobile where SDL3 is
            // already unconditional. We only ever SET the var (never clear it),
            // so an external override (e.g. someone exported OSU_SDL3=1) still
            // wins when the setting is off. Must run BEFORE
            // Host.GetSuitableDesktopHost() locks in the backend choice.
            try
            {
                if (!OperatingSystem.IsWindows() && readForceSDL3FromIni(resolveDefaultStorageFolder()))
                {
                    Environment.SetEnvironmentVariable("OSU_SDL3", "1");
                    Logger.Log("[Torii] OSU_SDL3=1 set from ForceSDL3 setting; SDL3 backend will be used.");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[Torii] Failed to read ForceSDL3 setting: {ex.Message}");
                // Fall through with the framework default.
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
                    host.Run(new OsuGameDesktop(args)
                    {
                        IsFirstRun = isFirstRun,
                        EnableWebSocketServer = Environment.GetEnvironmentVariable("OSU_WEBSOCKET_SERVER") == "1",
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
