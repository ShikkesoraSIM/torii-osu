// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Screens.Play;
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
        private Game.Configuration.OsuConfigManager config { get; set; } = null!;

        private bool isInGameplay => localUserInfo?.PlayingState.Value != LocalUserPlayingState.NotPlaying;

        private ScheduledDelegate? scheduledBackgroundCheck;

        protected override void LoadComplete()
        {
            // torii (arreglo del downgrade cruzado accidental): el build sabe en que stream
            // es por el suffix de la version (-nova/-torii/-vanilla). alineamos el setting
            // ReleaseStream a ese stream ANTES de que el base dispare el primer update check,
            // asi el updater nunca te saca del stream en el que realmente estas.
            //
            // sin esto pasaba: instalabas Nova pero el setting quedaba en el default (Torii),
            // y con AllowVersionDowngrade=true el check te downgradeaba Nova->Torii solo.
            //
            // solo re-alineamos cuando el BUILD cambio de stream desde la ultima corrida (o en
            // la primera). si el build no cambio, NO tocamos el setting, asi respetamos un
            // cambio de stream que el usuario hizo por el dropdown y todavia no se aplico.
            alignReleaseStreamToBuild();
            base.LoadComplete();
        }

        private void alignReleaseStreamToBuild()
        {
            if (!game.IsDeployedBuild)
                return;

            string? suffix = streamSuffixFromVersion(game.Version);
            if (suffix == null)
                return;

            if (config.Get<string>(Game.Configuration.OsuSetting.LastKnownBuildStream) == suffix)
                return;

            config.SetValue(Game.Configuration.OsuSetting.ReleaseStream, streamFromSuffix(suffix));
            config.SetValue(Game.Configuration.OsuSetting.LastKnownBuildStream, suffix);
            log($"Release stream alineado al stream del build: {suffix}");
        }

        // version ej: "2026.702.1-nova" -> "nova". null si no hay suffix reconocible.
        private static string? streamSuffixFromVersion(string version)
        {
            int dash = version.LastIndexOf('-');
            if (dash < 0 || dash >= version.Length - 1)
                return null;

            switch (version[(dash + 1)..].ToLowerInvariant())
            {
                case "nova": return "nova";
                case "vanilla": return "vanilla";
                case "torii": return "torii";
                case "lazer": return "torii"; // builds -lazer viejos = stable
                default: return null;
            }
        }

        private static Game.Configuration.ReleaseStream streamFromSuffix(string suffix)
        {
            switch (suffix)
            {
                case "nova": return Game.Configuration.ReleaseStream.Nova;
                case "vanilla": return Game.Configuration.ReleaseStream.Vanilla;
                default: return Game.Configuration.ReleaseStream.Torii;
            }
        }

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
                        // estable tambien pasa por ToriiUpdateSource (sin filtro de sufijo,
                        // prereleases afuera) solo para heredar la paginacion mas honda. una
                        // racha de prereleases nova/vanilla puede empujar el ultimo estable
                        // fuera de la ventana de 10 releases del GithubSource y frenar los
                        // updates estables. el comportamiento es identico al GithubSource:
                        // includePrereleases=false deja afuera -nova/-vanilla y el sufijo null
                        // mantiene a la vista los builds -lazer estables viejos.
                        updateSource = new ToriiUpdateSource(@"https://github.com/ShikkesoraSIM/torii-osu", prerelease: false, requiredTagSuffix: null);
                        break;
                }

                Velopack.UpdateManager updateManager = new Velopack.UpdateManager(updateSource, new UpdateOptions
                {
                    // torii: permitimos downgrade porque es lo que hace andar el switch de stream.
                    // el usuario que esta en Nova (version mas alta) y elige Vanilla necesita que el
                    // updater lo BAJE al build de Vanilla, aunque su version sea menor. no hay loop:
                    // Velopack converge - despues de bajar una vez, el instalado pasa a ser igual al
                    // ultimo del stream elegido, y el proximo check ya no encuentra update. sumado a
                    // la paginacion honda de ToriiUpdateSource, el feed del stream destino siempre
                    // aparece, asi que el downgrade cruzado entre streams funciona.
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

        private void restartToApplyUpdate(Velopack.UpdateManager updateManager, UpdateInfo update)
        {
            game.RestartOnExitAction = () => updateManager.WaitExitThenApplyUpdates(update.TargetFullRelease);
            game.AttemptExit();
        }

        private static void log(string text) => Logger.Log($"VelopackUpdateManager: {text}");
    }
}
