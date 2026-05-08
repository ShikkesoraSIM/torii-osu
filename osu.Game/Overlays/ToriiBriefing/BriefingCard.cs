// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ToriiBriefing
{
    /// <summary>
    /// The standard briefing card — kicker, headline, detail. Used for the
    /// rank pulse, dojo whispers, dojo radar, and session-mode rows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The previous implementation used a circular icon "puck" floating
    /// against the card surface, plus a 1.8% wide accent strip on the left
    /// edge, plus a horizontal accent gradient overlay, plus per-card
    /// border + shadow tints. Five accent treatments per card meant the
    /// accent never quite read as a single signal.
    /// </para>
    /// <para>
    /// This version keeps a single category cue: a small accent-tinted
    /// icon tile (the same shape vocabulary Apple uses on macOS / iOS
    /// Settings rows). The card surface itself is unified across all
    /// cards — same neutral glass — and the only thing that changes per
    /// category is the tile colour, the kicker colour, and the
    /// shadow tint inherited from the underlying <see cref="BriefingGlass"/>.
    /// </para>
    /// <para>
    /// Card height is fully driven by content: kicker + headline + however
    /// many lines of detail. No more 126 vs 142 magic numbers.
    /// </para>
    /// </remarks>
    internal partial class BriefingCard : CompositeDrawable, IHasTooltip
    {
        public LocalisableString TooltipText { get; set; }

        public BriefingCard(IconUsage icon, string kicker, string headline, string detail, Color4 accent)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChild = new BriefingGlass
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Accent = accent,
                AccentMix = 0.05f,
                ShadowOpacity = 0.16f,
                Child = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding
                    {
                        Horizontal = BriefingTheme.SpacingLg,
                        Vertical = BriefingTheme.SpacingMd + 2,
                    },
                    Children = new Drawable[]
                    {
                        // Icon tile — soft colored square.
                        // Anchored top-left so multi-line detail flows down rather
                        // than pushing the icon to the centre of the growing card.
                        new Container
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Size = new Vector2(36),
                            Masking = true,
                            CornerRadius = BriefingTheme.CornerSm,
                            CornerExponent = BriefingTheme.SquircleExponent,
                            MaskingSmoothness = 1.2f,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = accent.Opacity(0.18f),
                                },
                                new SpriteIcon
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Size = new Vector2(16),
                                    Icon = icon,
                                    Colour = accent,
                                },
                            },
                        },
                        // Text column. Left-padded by tile size + gap.
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Margin = new MarginPadding { Left = 36 + BriefingTheme.SpacingMd },
                            Padding = new MarginPadding { Right = 24 }, // leave room for the info chevron
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, BriefingTheme.SpacingXs + 1),
                            Children = new Drawable[]
                            {
                                new OsuSpriteText
                                {
                                    Text = kicker.ToUpperInvariant(),
                                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                                    Spacing = new Vector2(BriefingTheme.CaptionTracking * BriefingTheme.TypeCaption, 0),
                                    Colour = accent,
                                },
                                new OsuSpriteText
                                {
                                    Text = headline,
                                    Font = OsuFont.GetFont(size: BriefingTheme.TypeHeadline, weight: FontWeight.Bold),
                                    Colour = Color4.White.Opacity(BriefingTheme.InkPrimary),
                                },
                                new OsuTextFlowContainer(t =>
                                {
                                    t.Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold);
                                    t.Colour = Color4.White.Opacity(BriefingTheme.InkSecondary);
                                })
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Text = detail,
                                },
                            },
                        },
                        // Info hint — small icon at top-right of the card. Anchored top so
                        // it doesn't drift as the card grows with multi-line detail.
                        new SpriteIcon
                        {
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            Y = 6,
                            Size = new Vector2(12),
                            Icon = FontAwesome.Solid.InfoCircle,
                            Colour = Color4.White.Opacity(BriefingTheme.InkTertiary - 0.10f),
                        },
                    },
                },
            };
        }
    }
}
