// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Localisation;
using osu.Game.Online.API;

namespace osu.Game.Updater
{
    /// <summary>
    /// An update manager that shows notifications if a newer release is detected.
    /// This is a case where updates are handled externally by a package manager or other means, so no action is performed on clicking the notification.
    /// </summary>
    public partial class NoActionUpdateManager : UpdateManager
    {
        public override ReleaseStream? FixedReleaseStream => externalReleaseStream;

        private static ReleaseStream? externalReleaseStream => Enum.TryParse(Environment.GetEnvironmentVariable("OSU_EXTERNAL_UPDATE_STREAM"), true, out ReleaseStream stream) ? stream : null;

        private string version = string.Empty;

        [BackgroundDependencyLoader]
        private void load(OsuGameBase game)
        {
            version = game.Version.Split('-').First();
        }

        protected override async Task<bool> PerformUpdateCheck(CancellationToken cancellationToken)
        {
            try
            {
                ReleaseStream stream = externalReleaseStream ?? ReleaseStream.Value;
                bool includePrerelease = stream == Configuration.ReleaseStream.Tachyon;

                // Same Torii-self-feed reasoning as MobileUpdateNotifier — this is
                // the desktop fallback path that fires when Velopack isn't available
                // (e.g. portable / unsupported package format). Pointing at upstream
                // ppy/osu would prompt Torii users to "update" to vanilla lazer.
                OsuJsonWebRequest<GitHubRelease[]> releasesRequest = new OsuJsonWebRequest<GitHubRelease[]>("https://api.github.com/repos/ShikkesoraSIM/torii-osu/releases?per_page=10&page=1");
                await releasesRequest.PerformAsync(cancellationToken).ConfigureAwait(false);

                GitHubRelease[] releases = releasesRequest.ResponseObject;
                GitHubRelease? latest = releases.OrderByDescending(r => r.PublishedAt).FirstOrDefault(r => includePrerelease || !r.Prerelease);

                if (latest == null)
                    return false;

                // Strip the leading "v" from the GitHub tag (e.g. "v2026.511.0-lazer"
                // -> "2026.511.0") to match the Version-property format that
                // OsuGameAndroid / OsuGameDesktop hand back from the running app.
                // Without this, the comparison below always thinks the app is out of
                // date because "v2026.511.0" never equals "2026.511.0".
                string latestTagName = latest.TagName.TrimStart('v').Split('-').First();

                if (latestTagName != version)
                {
                    Notifications.Post(new UpdateAvailableNotification(cancellationToken)
                    {
                        Text = LocalisableString.Interpolate($"{NotificationsStrings.UpdateAvailable(version, latestTagName)}\n\n{NotificationsStrings.UpdateAvailablePackageManaged}"),
                    });

                    return true;
                }
            }
            catch
            {
                // we shouldn't crash on a web failure. or any failure for the matter.
                return true;
            }

            return false;
        }
    }
}
