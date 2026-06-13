// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
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
    /// The standard briefing card — kicker, headline, detail. Used for
    /// rank pulse, dojo whispers, dojo radar, and session-mode rows.
    /// </summary>
    /// <remarks>
    /// Polish through restraint: the card surface is a clean elevated dark
    /// rectangle with a neutral black drop shadow, the only accent is a
    /// saturated icon tile + the matching kicker text colour. No accent
    /// strip on the edge, no accent-tinted shadow (which used to bleed into
    /// adjacent cards as a multi-coloured halo when stacked vertically),
    /// no accent wash, no ribbon — the icon tile carries the category
    /// signal on its own. This is the macOS Settings vocabulary applied
    /// to a dark theme.
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
                SurfaceLift = 1.4f,
                // Tight, low-opacity contact shadow only — radius small enough that
                // the shadow falls inside the inter-card gutter rather than bleeding
                // onto the next card and creating a horizontal "rectangular halo"
                // band. Apple's dark-mode lists barely use shadow at all (relying on
                // surface contrast + hairline border for separation); this is the
                // minimum elevation cue that still reads.
                ShadowOpacity = 0.18f,
                ShadowRadius = 8f,
                ShadowRoundness = 4f,
                ShadowOffset = new Vector2(0, 2),
                Child = new Container
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding
                    {
                        Horizontal = BriefingTheme.SpacingMd + 4,
                        Vertical = BriefingTheme.SpacingMd - 2,
                    },
                    Children = new Drawable[]
                    {
                        // Saturated colour tile — single solid colour, white icon.
                        // The only accent treatment on the card; everything else is
                        // neutral so a stack of cards reads as one calm system.
                        buildIconTile(icon, accent),

                        // Text column.
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Margin = new MarginPadding { Left = icon_tile_size + BriefingTheme.SpacingMd - 2 },
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 2),
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
                                    Font = OsuFont.GetFont(size: BriefingTheme.TypeHeadline, weight: FontWeight.SemiBold),
                                    Colour = Color4.White.Opacity(BriefingTheme.InkPrimary),
                                    Margin = new MarginPadding { Top = 1 },
                                },
                                new OsuTextFlowContainer(t =>
                                {
                                    t.Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.Regular);
                                    t.Colour = Color4.White.Opacity(BriefingTheme.InkSecondary);
                                })
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Text = detail,
                                    Margin = new MarginPadding { Top = 1 },
                                },
                            },
                        },
                    },
                },
            };
        }

        private const float icon_tile_size = 36f;

        private static Drawable buildIconTile(IconUsage icon, Color4 accent)
        {
            // iOS-Settings vocabulary: full-saturation accent tile with subtle
            // top-to-bottom shading (lighter top → accent bottom) for a hint of
            // dimensionality, white icon for guaranteed contrast.
            return new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Size = new Vector2(icon_tile_size),
                Masking = true,
                CornerRadius = BriefingTheme.CornerSm - 1,
                CornerExponent = BriefingTheme.SquircleExponent,
                MaskingSmoothness = 1.2f,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientVertical(
                            accent.Lighten(0.1f),
                            accent.Darken(0.1f)),
                    },
                    // Soft inner top highlight — a thin band of brightness at the
                    // top edge that mirrors the panel-scale specular ribbon. Sells
                    // the "lit from above" feeling at small scale.
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 14,
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = ColourInfo.GradientVertical(
                                Color4.White.Opacity(0.18f),
                                Color4.White.Opacity(0)),
                        },
                    },
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(16),
                        Icon = icon,
                        Colour = Color4.White,
                    },
                },
            };
        }
    }
}
