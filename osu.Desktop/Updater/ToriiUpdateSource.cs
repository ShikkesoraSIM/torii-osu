// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using Velopack.Sources;

namespace osu.Desktop.Updater
{
    /// <summary>
    /// Wraps <see cref="GithubSource"/> with a tag-suffix filter so a given
    /// Torii release stream only ever sees its own builds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The motivating problem: Velopack's <see cref="GithubSource"/> picks
    /// the semver-latest release from the configured GitHub repo, optionally
    /// including prereleases. For a Nova user
    /// (<c>ReleaseStream.Nova</c> → <c>includePrereleases = true</c>),
    /// that means BOTH the latest <c>-nova</c> prerelease AND the latest
    /// <c>-torii</c> stable release are visible. If a stable release ships
    /// later in real time and ends up with a higher numeric version than
    /// the latest Nova, Velopack would silently "update" the Nova user
    /// into the stable binary — effectively a reverse-downgrade across
    /// streams, which is not what the user opted into.
    /// </para>
    /// <para>
    /// This subclass overrides <see cref="GithubSource.GetReleases(bool)"/>
    /// to post-filter the returned releases by tag suffix. Pass
    /// <see cref="requiredTagSuffix"/> = <c>"nova"</c> for the Nova stream;
    /// stable users keep the un-filtered <see cref="GithubSource"/> because
    /// their <c>includePrereleases = false</c> already excludes <c>-nova</c>
    /// builds (which ship as GitHub prereleases — see
    /// <c>.github/workflows/build-gu.yml</c>).
    /// </para>
    /// <para>
    /// All other <see cref="IUpdateSource"/> methods (release-feed
    /// generation, asset download) inherit directly from
    /// <see cref="GithubSource"/> — we only narrow the upstream view, we
    /// don't reimplement the wire protocol.
    /// </para>
    /// </remarks>
    public class ToriiUpdateSource : GithubSource
    {
        private readonly string requiredTagSuffix;

        /// <param name="repoUrl">
        /// The GitHub repository URL (e.g.
        /// <c>https://github.com/ShikkesoraSIM/torii-osu</c>).
        /// </param>
        /// <param name="prerelease">
        /// Whether Velopack should ask GitHub for prereleases too. Nova
        /// stream needs this <c>true</c> because <c>-nova</c> tags are
        /// published as prereleases; stable streams pass <c>false</c>.
        /// </param>
        /// <param name="requiredTagSuffix">
        /// Tag-name suffix that must match for a release to be considered.
        /// Compared case-insensitively against the part after the last
        /// hyphen. Pass <c>"nova"</c> to lock Nova users to <c>-nova</c>
        /// tags. Leave null / empty to disable the filter (behaves
        /// identically to the base <see cref="GithubSource"/>).
        /// </param>
        public ToriiUpdateSource(string repoUrl, bool prerelease, string? requiredTagSuffix)
            : base(repoUrl, null, prerelease, null)
        {
            this.requiredTagSuffix = requiredTagSuffix ?? string.Empty;
        }

        protected override async Task<GithubRelease[]> GetReleases(bool includePrereleases)
        {
            GithubRelease[] all = await base.GetReleases(includePrereleases).ConfigureAwait(false);

            if (string.IsNullOrEmpty(requiredTagSuffix))
                return all;

            // Velopack's GithubRelease doesn't expose `tag_name` publicly
            // (only Name / Prerelease / PublishedAt / Assets), but our
            // release titles follow the contract set in build-gu.yml:
            //   name: osu! Torii ${{ needs.version.outputs.version }}
            // So `release.Name` ends in `vYYYY.MDD.N-{torii|nova|lazer}`
            // and the suffix we want is the part after the last hyphen.
            //
            // Doing the parse defensively: if a hand-edited release title
            // or a renamed GitHub Release doesn't follow the convention,
            // we just exclude it rather than crashing the update check.
            return all.Where(release =>
            {
                if (release?.Name == null)
                    return false;

                int lastDash = release.Name.LastIndexOf('-');
                if (lastDash < 0 || lastDash >= release.Name.Length - 1)
                    return false;

                ReadOnlySpan<char> suffix = release.Name.AsSpan(lastDash + 1);
                return suffix.Equals(requiredTagSuffix.AsSpan(), StringComparison.OrdinalIgnoreCase);
            }).ToArray();
        }
    }
}
