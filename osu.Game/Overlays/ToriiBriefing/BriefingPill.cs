// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ToriiBriefing
{
    /// <summary>
    /// Compact uppercase capsule label used for kickers, section tags, and
    /// the "daily portal" badge in the briefing header.
    /// </summary>
    /// <remarks>
    /// Mirrors Apple's SF Caps treatment: a tinted translucent fill + bold
    /// uppercase text + the letter-spacing the system uses on its caption
    /// labels. Squircle corner profile keeps the corners feeling soft at
    /// any size.
    /// </remarks>
    internal partial class BriefingPill : CompositeDrawable
    {
        public BriefingPill(string text, Color4 accent)
        {
            AutoSizeAxes = Axes.Both;
            Masking = true;
            CornerRadius = BriefingTheme.CornerSm;
            CornerExponent = BriefingTheme.SquircleExponent;
            MaskingSmoothness = 1.2f;
            BorderThickness = 1f;
            BorderColour = accent.Opacity(0.22f);

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = accent.Opacity(0.14f),
                },
                new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Horizontal = 10, Vertical = 4 },
                    Child = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = text.ToUpperInvariant(),
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                        Spacing = new Vector2(BriefingTheme.CaptionTracking * BriefingTheme.TypeCaption, 0),
                        Colour = accent,
                    },
                },
            };
        }
    }
}
