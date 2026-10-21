// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Localisation;
using osu.Game.Online.API;

namespace osu.Game.Updater
{
    /// <summary>
    /// An update manager that shows notifications if a newer release is detected for mobile platforms.
    /// Installation is left up to the user.
    /// </summary>
    public partial class MobileUpdateNotifier : UpdateManager
    {
        public override ReleaseStream? FixedReleaseStream => stream;

        private string version = null!;
        private ReleaseStream stream;

        [Resolved]
        private GameHost host { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load(OsuGameBase game)
        {
            version = game.Version;
            stream = Enum.TryParse(game.Version.Split('-').Last(), true, out ReleaseStream s) ? s : Configuration.ReleaseStream.Torii;
        }

        protected override async Task<bool> PerformUpdateCheck(CancellationToken cancellationToken)
        {
            try
            {
                string? assetExtension = RuntimeInfo.OS switch
                {
                    RuntimeInfo.Platform.Android => ".apk",
                    RuntimeInfo.Platform.iOS => ".ipa",
                    _ => null,
                };
                if (assetExtension == null)
                    return false;

                var releases = new List<GitHubRelease>();
                for (int page = 1; page <= 5; page++)
                {
                    var request = new OsuJsonWebRequest<GitHubRelease[]>($"https://api.github.com/repos/ShikkesoraSIM/torii-osu/releases?per_page=100&page={page}");
                    await request.PerformAsync(cancellationToken).ConfigureAwait(false);
                    releases.AddRange(request.ResponseObject);
                    if (request.ResponseObject.Length < 100)
                        break;
                }

                GitHubRelease? latest = FindUpdate(releases, version, stream, assetExtension);

                if (latest == null)
                    return false;

                string latestTagName = latest.TagName.TrimStart('v');

                if (tryGetBestUrl(latest, out string? url))
                {
                    Notifications.Post(new UpdateAvailableNotification(cancellationToken)
                    {
                        Text = LocalisableString.Interpolate($"{NotificationsStrings.UpdateAvailable(version, latestTagName)}\n\n{NotificationsStrings.UpdateAvailableManualInstall}"),
                        Icon = FontAwesome.Solid.Download,
                        Activated = () =>
                        {
                            host.OpenUrlExternally(url);
                            return true;
                        }
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

        internal static GitHubRelease? FindUpdate(IEnumerable<GitHubRelease> releases, string currentVersion, ReleaseStream stream, string assetExtension)
        {
            if (!tryParseVersion(currentVersion, out Version? current))
                return null;

            string suffix = $"-{stream.ToString().ToLowerInvariant()}";
            return releases.Where(release => release.TagName?.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) == true
                                             && (stream != Configuration.ReleaseStream.Torii || !release.Prerelease)
                                             && !string.IsNullOrEmpty(release.HtmlUrl)
                                             && release.Assets?.Any(asset => asset.Name?.EndsWith(assetExtension, StringComparison.OrdinalIgnoreCase) == true) == true)
                           .Select(release => (Release: release, Version: tryParseVersion(release.TagName, out Version? parsed) ? parsed : null))
                           .Where(candidate => candidate.Version > current)
                           .OrderByDescending(candidate => candidate.Version)
                           .Select(candidate => candidate.Release)
                           .FirstOrDefault();
        }

        private static bool tryParseVersion(string version, [NotNullWhen(true)] out Version? parsed)
            => Version.TryParse(version.TrimStart('v').Split('-').First(), out parsed);

        private bool tryGetBestUrl(GitHubRelease release, [NotNullWhen(true)] out string? url)
        {
            url = null;
            GitHubAsset? bestAsset = null;

            switch (RuntimeInfo.OS)
            {
                case RuntimeInfo.Platform.iOS:
                    if (release.Assets?.Exists(f => f.Name.EndsWith(".ipa", StringComparison.Ordinal)) == true)
                        // iOS releases are available via testflight. this link seems to work well enough for now.
                        // see https://stackoverflow.com/a/32960501
                        url = "itms-beta://beta.itunes.apple.com/v1/app/1447765923";

                    break;

                case RuntimeInfo.Platform.Android:
                    if (release.Assets?.Exists(f => f.Name.EndsWith(".apk", StringComparison.Ordinal)) == true)
                        // on our testing device using the .apk URL causes the download to magically disappear.
                        url = release.HtmlUrl;

                    break;
            }

            url ??= bestAsset?.BrowserDownloadUrl;
            return url != null;
        }
    }
}
