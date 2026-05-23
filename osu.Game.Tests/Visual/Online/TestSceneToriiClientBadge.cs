// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Users.Drawables;
using osuTK;

namespace osu.Game.Tests.Visual.Online
{
    /// <summary>
    /// Visual matrix for the Torii client badge: every (stream, platform)
    /// combination side by side so we can eyeball that the colour swap +
    /// platform icon resolution works without having to spin up the full
    /// stack and connect with a second account.
    ///
    /// Open via the test browser → Online → ToriiClientBadge.
    /// </summary>
    [TestFixture]
    public partial class TestSceneToriiClientBadge : OsuTestScene
    {
        public TestSceneToriiClientBadge()
        {
            // Every value the spectator might forward. Two sections:
            //   1. Rich format "<brand>|<os>" — the post-fix CI output.
            //   2. Legacy brand-only strings — what the registry stored
            //      before the encoding change. Verifies backward compat
            //      (no platform icon, default torii-red colour, "torii"
            //      label even when the brand is older).
            (string label, string raw)[] richVariants =
            {
                ("Torii / Windows",   "Torii|Windows"),
                ("Torii / Linux",     "Torii|Linux"),
                ("Torii / macOS",     "Torii|macOS"),
                ("Torii / Android",   "Torii|Android"),
                ("Torii Nova / Windows", "Torii Nova|Windows"),
                ("Torii Nova / Linux",   "Torii Nova|Linux"),
                ("Torii Nova / macOS",   "Torii Nova|macOS"),
                ("Torii Nova / Android", "Torii Nova|Android"),
            };

            (string label, string raw)[] legacyVariants =
            {
                ("Legacy 'torii'",     "torii"),
                ("Legacy 'osu! Torii'", "osu! Torii"),
                ("Brand-only 'Torii Nova'", "Torii Nova"),
            };

            // Sanity: hidden state. The badge should also handle empty /
            // non-Torii input by fading to Alpha=0, but we don't render
            // it in this test scene because a hidden drawable doesn't
            // verify much visually.

            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Colour4.FromHex("#1a1a1a"),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 24),
                    Children = new Drawable[]
                    {
                        section("Rich format (post-fix CI output)", richVariants),
                        section("Legacy brand-only", legacyVariants),
                    },
                },
            };
        }

        private static Drawable section(string title, (string label, string raw)[] variants)
        {
            var rows = new Drawable[variants.Length];
            for (int i = 0; i < variants.Length; i++)
                rows[i] = variantRow(variants[i].label, variants[i].raw);

            return new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 6),
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Text = title,
                        Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                        Colour = Colour4.White.Opacity(0.65f),
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 4),
                        ChildrenEnumerable = rows,
                    },
                },
            };
        }

        private static Drawable variantRow(string label, string raw)
        {
            var badge = new ToriiClientBadge
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
            };

            badge.OnLoadComplete += d => ((ToriiClientBadge)d).UpdateClientName(raw);

            return new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(14, 0),
                Children = new Drawable[]
                {
                    new Container
                    {
                        AutoSizeAxes = Axes.Y,
                        Width = 220,
                        Child = new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = label,
                            Font = OsuFont.GetFont(size: 12),
                            Colour = Colour4.White.Opacity(0.55f),
                        },
                    },
                    new Container
                    {
                        AutoSizeAxes = Axes.Y,
                        Width = 180,
                        Child = badge,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = $"raw: \"{raw}\"",
                        Font = OsuFont.GetFont(size: 10),
                        Colour = Colour4.White.Opacity(0.30f),
                    },
                },
            };
        }
    }
}
