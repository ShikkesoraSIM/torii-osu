// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ToriiBriefing
{
    /// <summary>
    /// Section divider used between the two briefing groups
    /// (your session / dojo radar). Just an uppercase tracked label
    /// followed by a one-liner subtitle — no horizontal rule, no pill
    /// chrome. The label and subtitle do all the work; the empty space
    /// above and below the row is what visually separates the sections.
    /// </summary>
    /// <remarks>
    /// The previous version drew a faded horizontal rule under a pill
    /// label. The pill chrome competed with the cards' chrome (cards have
    /// their own borders + shadows + tiles), turning the section divider
    /// into another piece of UI noise rather than the calm separator it
    /// should be. This version keeps only the typography, which is what
    /// macOS Settings / iOS use for grouped-list section headers.
    /// </remarks>
    internal partial class BriefingSectionHeader : CompositeDrawable
    {
        public BriefingSectionHeader(string title, string subtitle, Color4? accent = null)
        {
            var tint = accent ?? BriefingTheme.AccentCyan;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
            Margin = new MarginPadding { Top = BriefingTheme.SpacingSm, Bottom = BriefingTheme.SpacingXs };
            Padding = new MarginPadding { Left = 2 };

            InternalChild = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(BriefingTheme.SpacingSm, 0),
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = title.ToUpperInvariant(),
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                        Spacing = new Vector2(BriefingTheme.CaptionTracking * BriefingTheme.TypeCaption, 0),
                        Colour = tint,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = subtitle,
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeBody - 1.5f, weight: FontWeight.Regular),
                        Colour = Color4.White.Opacity(BriefingTheme.InkTertiary),
                    },
                },
            };
        }
    }
}
