// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game;
using osu.Game.Configuration;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Screens.Play;
using osuTK.Graphics;
using Velopack;
using Velopack.Sources;
using UpdateManager = osu.Game.Updater.UpdateManager;

namespace osu.Desktop.Updater
{
    public partial class VelopackUpdateManager : UpdateManager
    {
        [Resolved]
        private INotificationOverlay notificationOverlay { get; set; } = null!;

        [Resolved]
        private OsuGameBase game { get; set; } = null!;

        [Resolved]
        private ILocalUserPlayInfo? localUserInfo { get; set; }

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private bool isInGameplay => localUserInfo?.PlayingState.Value != LocalUserPlayingState.NotPlaying;

        private ScheduledDelegate? scheduledBackgroundCheck;

        private void scheduleNextUpdateCheck()
        {
            scheduledBackgroundCheck?.Cancel();
            scheduledBackgroundCheck = Scheduler.AddDelayed(() =>
            {
                log("Running scheduled background update check...");
                CheckForUpdate();
            }, 60000 * 30);
        }

        protected override async Task<bool> PerformUpdateCheck(CancellationToken cancellationToken)
        {
            scheduledBackgroundCheck?.Cancel();

            if (isInGameplay)
            {
                log("Update check cancelled - user is in gameplay");
                scheduleNextUpdateCheck();
                return false;
            }

            try
            {
                // Pull desktop updates from the Torii repository releases.
                //
                // Stream selection mapping:
                // - Torii (stable) → plain `GithubSource` with
                //   `includePrereleases = false`. The `-torii` releases are
                //   non-prereleases, and `-nova` releases are tagged
                //   prereleases (see build-gu.yml), so the upstream
                //   exclusion naturally pins stable users to stable tags.
                //   Legacy `-lazer` releases also count as stable because
                //   they were published non-prerelease too.
                // - Torii Nova → `ToriiUpdateSource` with `requiredTagSuffix
                //   = "nova"` so only `-nova` tagged releases are
                //   considered. This prevents the silent reverse-downgrade
                //   path where a later semver-higher stable release would
                //   otherwise "update" a Nova user back to stable.
                // Nova and Vanilla are both GitHub prereleases, so each pins to its
                // own `-<suffix>` tag via ToriiUpdateSource. This stops the silent
                // reverse-downgrade where a later semver-higher stable release would
                // otherwise "update" a Nova/Vanilla user back to stable.
                IUpdateSource updateSource;

                switch (ReleaseStream.Value)
                {
                    case Game.Configuration.ReleaseStream.Nova:
                        updateSource = new ToriiUpdateSource(@"https://github.com/ShikkesoraSIM/torii-osu", prerelease: true, requiredTagSuffix: "nova");
                        break;

                    case Game.Configuration.ReleaseStream.Vanilla:
                        updateSource = new ToriiUpdateSource(@"https://github.com/ShikkesoraSIM/torii-osu", prerelease: true, requiredTagSuffix: "vanilla");
                        break;

                    default:
                        // Stable also goes through ToriiUpdateSource (no suffix filter,
                        // prereleases excluded) purely to inherit its deeper pagination.
                        // A run of nova/vanilla prereleases can otherwise push the latest
                        // stable release past GithubSource's 10-release window and stall
                        // stable updates. Behaviour is otherwise identical to GithubSource:
                        // includePrereleases = false keeps -nova/-vanilla out, and the null
                        // suffix keeps legacy -lazer stable builds in view.
                        updateSource = new ToriiUpdateSource(@"https://github.com/ShikkesoraSIM/torii-osu", prerelease: false, requiredTagSuffix: null);
                        break;
                }
                Velopack.UpdateManager updateManager = new Velopack.UpdateManager(updateSource, new UpdateOptions
                {
                    AllowVersionDowngrade = true
                });

                UpdateInfo? update = await updateManager.CheckForUpdatesAsync().ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    log("Update check cancelled");
                    scheduleNextUpdateCheck();
                    return true;
                }

                if (update == null)
                {
                    // No update is available.
                    log("No update found");
                    scheduleNextUpdateCheck();
                    return false;
                }

                // Download update in the background while notifying awaiters of the update being available.
                log($"New update available: {update.TargetFullRelease.Version}");
                downloadUpdate(updateManager, update, cancellationToken);
                return true;
            }
            catch (Exception e)
            {
                log($"Update check failed with error ({e.Message})");
                runOutsideOfGameplay(() => notificationOverlay.Post(new SimpleNotification
                {
                    Text = "Update check failed. If you're using a portable build, download the latest portable package manually.",
                    Icon = FontAwesome.Solid.ExclamationTriangle,
                    IconColour = Color4.OrangeRed,
                }), cancellationToken);

                // we shouldn't crash on a web failure. or any failure for the matter.
                scheduleNextUpdateCheck();
                return true;
            }
        }

        private void downloadUpdate(Velopack.UpdateManager updateManager, UpdateInfo update, CancellationToken cancellationToken) => Task.Run(async () =>
        {
            log($"Beginning download of update {update.TargetFullRelease.Version}...");

            UpdateDownloadProgressNotification progressNotification = new UpdateDownloadProgressNotification(cancellationToken)
            {
                CompletionClickAction = () =>
                {
                    restartToApplyUpdate(updateManager, update);
                    return true;
                }
            };

            try
            {
                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(progressNotification.CancellationToken, cancellationToken))
                {
                    progressNotification.StartDownload();
                    runOutsideOfGameplay(() => notificationOverlay.Post(progressNotification), cts.Token);

                    await updateManager.DownloadUpdatesAsync(update, p => progressNotification.Progress = p / 100f, cts.Token).ConfigureAwait(false);
                    runOutsideOfGameplay(() => progressNotification.State = ProgressNotificationState.Completed, cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                progressNotification.FailDownload();
                log(@"Update cancelled");
            }
            catch (Exception e)
            {
                // In the case of an error, a separate notification will be displayed.
                progressNotification.FailDownload();
                Logger.Error(e, @"Update failed!");
            }

            return true;
        }, cancellationToken);

        private void runOutsideOfGameplay(Action action, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            if (isInGameplay)
            {
                Scheduler.AddDelayed(() => runOutsideOfGameplay(action, cancellationToken), 1000);
                return;
            }

            action();
        }

        private void restartToApplyUpdate(Velopack.UpdateManager updateManager, UpdateInfo update) => Task.Run(async () =>
        {
            await updateManager.WaitExitThenApplyUpdatesAsync(update.TargetFullRelease).ConfigureAwait(false);
            Schedule(() => game.AttemptExit());
        });

        private static void log(string text) => Logger.Log($"VelopackUpdateManager: {text}");
    }
}
