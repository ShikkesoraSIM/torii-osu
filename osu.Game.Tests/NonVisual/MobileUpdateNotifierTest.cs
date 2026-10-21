// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Game.Configuration;
using osu.Game.Updater;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class MobileUpdateNotifierTest
    {
        [TestCase("2026.1020.0-torii", "v2026.1020.0-torii")]
        [TestCase("v2026.1020.0-torii", "2026.1020.0-torii")]
        [TestCase("2026.1020.1-nova", "v2026.1020.1-nova")]
        [TestCase("2026.1020.2-vanilla", "v2026.1020.2-vanilla")]
        public void CurrentReleaseIsNotAnUpdate(string current, string tag)
        {
            var stream = Enum.Parse<ReleaseStream>(current.Split('-')[1], true);
            Assert.That(MobileUpdateNotifier.FindUpdate([release(tag)], current, stream, ".apk"), Is.Null);
        }

        [TestCase(ReleaseStream.Torii, "2026.1020.0-torii", "v2026.1022.1-torii")]
        [TestCase(ReleaseStream.Nova, "2026.1020.1-nova", "v2026.1022.2-nova")]
        [TestCase(ReleaseStream.Vanilla, "2026.1020.2-vanilla", "v2026.1022.3-vanilla")]
        public void UpdateMustStayInItsStream(ReleaseStream stream, string current, string expected)
        {
            GitHubRelease[] releases =
            [
                release("v2027.101.0-torii"),
                release("v2027.101.1-nova"),
                release("v2027.101.2-vanilla"),
            ];
            string otherStream = stream == ReleaseStream.Torii ? "nova" : "torii";
            var valid = release(expected);
            Assert.That(MobileUpdateNotifier.FindUpdate([release($"v2027.101.9-{otherStream}"), valid], current, stream, ".apk"), Is.SameAs(valid));
            Assert.That(MobileUpdateNotifier.FindUpdate(releases, current, stream, ".apk")!.TagName, Does.EndWith($"-{stream.ToString().ToLowerInvariant()}"));
        }

        [Test]
        public void PublicationDateCannotCauseDowngrades()
        {
            var old = release("v2026.1019.12-nova");
            old.PublishedAt = DateTime.UtcNow.AddDays(1);
            var newer = release("v2026.1022.2-nova");
            newer.PublishedAt = DateTime.UtcNow.AddDays(-1);
            Assert.That(MobileUpdateNotifier.FindUpdate([old], "2026.1020.1-nova", ReleaseStream.Nova, ".apk"), Is.Null);
            Assert.That(MobileUpdateNotifier.FindUpdate([old, newer], "2026.1020.1-nova", ReleaseStream.Nova, ".apk"), Is.SameAs(newer));
        }

        [Test]
        public void VersionsAreOrderedNumerically()
        {
            var highest = release("v2026.1022.10-nova");
            Assert.That(MobileUpdateNotifier.FindUpdate([release("v2026.1022.9-nova"), highest], "2026.1022.8-nova", ReleaseStream.Nova, ".apk"), Is.SameAs(highest));
        }

        [Test]
        public void NeedsAnAssetForTheCurrentPlatform()
        {
            var desktopOnly = release("v2026.1022.4-nova", ".exe");
            var android = release("v2026.1022.2-nova");
            Assert.That(MobileUpdateNotifier.FindUpdate([desktopOnly, android], "2026.1020.1-nova", ReleaseStream.Nova, ".apk"), Is.SameAs(android));
            Assert.That(MobileUpdateNotifier.FindUpdate([android], "2026.1020.1-nova", ReleaseStream.Nova, ".ipa"), Is.Null);
        }

        [Test]
        public void InvalidAndIncompleteReleasesAreIgnored()
        {
            var noAssets = release("v2026.1022.2-nova");
            noAssets.Assets = null!;
            var noUrl = release("v2026.1022.3-nova");
            noUrl.HtmlUrl = null!;
            var noTag = release("v2026.1022.4-nova");
            noTag.TagName = null!;
            Assert.That(MobileUpdateNotifier.FindUpdate([noAssets, noUrl, noTag, release("invalid-nova")], "2026.1020.1-nova", ReleaseStream.Nova, ".apk"), Is.Null);
            Assert.That(MobileUpdateNotifier.FindUpdate([release("v2026.1022.2-nova")], "local release", ReleaseStream.Nova, ".apk"), Is.Null);
        }

        [Test]
        public void StableDoesNotAcceptPrereleases()
        {
            var candidate = release("v2026.1022.2-torii");
            candidate.Prerelease = true;
            Assert.That(MobileUpdateNotifier.FindUpdate([candidate], "2026.1020.0-torii", ReleaseStream.Torii, ".apk"), Is.Null);
        }

        private static GitHubRelease release(string tag, string extension = ".apk") => new GitHubRelease
        {
            TagName = tag,
            HtmlUrl = $"https://github.com/ShikkesoraSIM/torii-osu/releases/tag/{tag}",
            Prerelease = !tag.EndsWith("-torii", StringComparison.Ordinal),
            Assets = [new GitHubAsset { Name = $"torii{extension}" }],
        };
    }
}
