// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Microsoft.Win32;
using osu.Desktop.Performance;
using osu.Desktop.Security;
using osu.Framework.Platform;
using osu.Game;
using osu.Desktop.Updater;
using osu.Framework;
using osu.Framework.Configuration;
using osu.Framework.Logging;
using osu.Game.Updater;
using osu.Desktop.Windows;
using osu.Framework.Allocation;
using osu.Game.Configuration;
using osu.Game.IO;
using osu.Game.IPC;
using osu.Game.Online.Multiplayer;
using osu.Game.Performance;
using osu.Game.Utils;

namespace osu.Desktop
{
    internal partial class OsuGameDesktop : OsuGame
    {
        private OsuSchemeLinkIPCChannel? osuSchemeLinkIPCChannel;
        private ArchiveImportIPCChannel? archiveImportIPCChannel;

        [Cached(typeof(IHighPerformanceSessionManager))]
        private readonly HighPerformanceSessionManager highPerformanceSessionManager = new HighPerformanceSessionManager();

        public bool IsFirstRun { get; init; }

#if TORII_NOVA
        [Resolved]
        private FrameworkConfigManager frameworkConfig { get; set; } = null!;
#endif

        public OsuGameDesktop(string[]? args = null)
            : base(args)
        {
        }

        public override StableStorage? GetStorageForStableInstall()
        {
            try
            {
                if (Host is DesktopGameHost desktopHost)
                {
                    string? stablePath = getStableInstallPath();
                    if (!string.IsNullOrEmpty(stablePath))
                        return new StableStorage(stablePath, desktopHost);
                }
            }
            catch (Exception)
            {
                Logger.Log("Could not find a stable install", LoggingTarget.Runtime, LogLevel.Important);
            }

            return null;
        }

        private string? getStableInstallPath()
        {
            static bool checkExists(string p) => Directory.Exists(Path.Combine(p, "Songs")) || File.Exists(Path.Combine(p, "osu!.cfg"));

            string? stableInstallPath;

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    stableInstallPath = getStableInstallPathFromRegistry("osustable.File.osz");

                    if (!string.IsNullOrEmpty(stableInstallPath) && checkExists(stableInstallPath))
                        return stableInstallPath;

                    stableInstallPath = getStableInstallPathFromRegistry("osu!");

                    if (!string.IsNullOrEmpty(stableInstallPath) && checkExists(stableInstallPath))
                        return stableInstallPath;
                }
                catch
                {
                }
            }

            stableInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"osu!");
            if (checkExists(stableInstallPath))
                return stableInstallPath;

            stableInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".osu");
            if (checkExists(stableInstallPath))
                return stableInstallPath;

            return null;
        }

        [SupportedOSPlatform("windows")]
        private string? getStableInstallPathFromRegistry(string progId)
        {
            using (RegistryKey? key = Registry.ClassesRoot.OpenSubKey(progId))
                return key?.OpenSubKey(WindowsAssociationManager.SHELL_OPEN_COMMAND)?.GetValue(string.Empty)?.ToString()?.Split('"')[1].Replace("osu!.exe", "");
        }

        public static bool IsPackageManaged => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OSU_EXTERNAL_UPDATE_PROVIDER"));

        protected override UpdateManager CreateUpdateManager()
        {
            // If this is the first time we've run the game, ie it is being installed,
            // reset the user's release stream to Torii (stable).
            //
            // This ensures that if a user is trying to recover from a failed startup on an unstable release stream,
            // the game doesn't immediately try and update them back to the experimental stream after starting up.
            if (IsFirstRun)
                LocalConfig.SetValue(OsuSetting.ReleaseStream, ReleaseStream.Torii);

            if (IsPackageManaged)
                return new NoActionUpdateManager();

            return new VelopackUpdateManager();
        }

        public override bool RestartAppWhenExited()
        {
            Task.Run(() => Velopack.UpdateExe.Start(waitPid: (uint)Environment.ProcessId)).FireAndForget();
            return true;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

#if TORII_NOVA
            applyNovaRendererDefault();
#endif

            LoadComponentAsync(new DiscordRichPresence(), Add);

            if (RuntimeInfo.OS == RuntimeInfo.Platform.Windows)
                LoadComponentAsync(new GameplayWinKeyBlocker(), Add);

            LoadComponentAsync(new ElevatedPrivilegesChecker(), Add);

            osuSchemeLinkIPCChannel = new OsuSchemeLinkIPCChannel(Host, this);
            archiveImportIPCChannel = new ArchiveImportIPCChannel(Host, this);
        }

#if TORII_NOVA
        /// <summary>
        /// On Torii Nova builds, prefer the platform's Deferred renderer
        /// variant when the user hasn't explicitly picked one yet. Detection
        /// is: <see cref="FrameworkSetting.Renderer"/> still at the framework
        /// default (<see cref="RendererType.Automatic"/>) → safe to override;
        /// any other value → respect the user's choice.
        /// </summary>
        /// <remarks>
        /// Renderer is already initialised by the time <see cref="LoadComplete"/>
        /// runs, so the write to <c>framework.ini</c> takes effect on the next
        /// start. First Nova session uses whatever the framework auto-picked;
        /// every session after uses the Deferred variant. We deliberately don't
        /// hot-swap renderers mid-flight — that path isn't supported cleanly
        /// in osu-framework and the failure modes (lost GL context, mis-cached
        /// textures) are worse than just letting the next launch handle it.
        ///
        /// Platform mapping is conservative:
        /// <list type="bullet">
        /// <item>Windows  → <see cref="RendererType.Deferred_Direct3D11"/></item>
        /// <item>macOS    → <see cref="RendererType.Deferred_Metal"/></item>
        /// <item>Linux    → left at Automatic (Deferred_Vulkan / Deferred_OpenGL
        /// stability varies a lot by driver; the framework's auto-pick is the
        /// safer floor until we have concrete reports from Linux Nova users).</item>
        /// </list>
        /// </remarks>
        private void applyNovaRendererDefault()
        {
            try
            {
                var rendererBindable = frameworkConfig.GetBindable<RendererType>(FrameworkSetting.Renderer);

                // Only override if the user is still on the framework default.
                // Any explicit pick (including someone who picked Deferred
                // themselves before) is left alone.
                if (rendererBindable.Value != RendererType.Automatic)
                    return;

                RendererType? target = null;
                if (RuntimeInfo.OS == RuntimeInfo.Platform.Windows)
                    target = RendererType.Deferred_Direct3D11;
                else if (RuntimeInfo.OS == RuntimeInfo.Platform.macOS)
                    target = RendererType.Deferred_Metal;
                // Linux deliberately omitted — see XML doc above.

                if (target != null)
                {
                    rendererBindable.Value = target.Value;
                    Logger.Log(
                        $"[Torii Nova] Renderer default switched from Automatic to {target.Value}. "
                        + "Restart the game for the change to take effect.",
                        LoggingTarget.Runtime, LogLevel.Important);
                }
            }
            catch (System.Exception ex)
            {
                // Defensive: don't crash startup if the framework config
                // surface changes shape under us. The dropdown in Settings
                // → Graphics still lets the user switch manually.
                Logger.Log($"[Torii Nova] applyNovaRendererDefault failed: {ex.Message}",
                    LoggingTarget.Runtime, LogLevel.Important);
            }
        }
#endif

        public override void SetHost(GameHost host)
        {
            base.SetHost(host);

            // Apple operating systems use a better icon provided via external assets.
            if (!RuntimeInfo.IsApple)
            {
                var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(GetType(), "lazer.ico");
                if (iconStream != null)
                    host.Window.SetIconFromStream(iconStream);
            }

            host.Window.Title = Name;
        }

        protected override BatteryInfo CreateBatteryInfo() => FrameworkEnvironment.UseSDL3 ? new SDL3BatteryInfo() : new SDL2BatteryInfo();

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            osuSchemeLinkIPCChannel?.Dispose();
            archiveImportIPCChannel?.Dispose();
        }
    }
}
