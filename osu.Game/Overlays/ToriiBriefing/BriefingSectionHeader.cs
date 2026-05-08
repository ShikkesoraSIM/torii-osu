// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ToriiBriefing
{
    /// <summary>
    /// Section divider used between the two briefing groups
    /// (your session / dojo radar). A horizontal hairline rule with the
    /// section title sitting on top of it as a pill, plus a one-liner
    /// subtitle.
    /// </summary>
    /// <remarks>
    /// The previous layout drew the rule as a solid 8%-opacity line edge to
    /// edge, which fought visually with the floating pill that sat on it.
    /// Here the rule fades out at both ends (so the pill feels like it's
    /// "lifting off" the line rather than punching a hole in it) and the
    /// subtitle uses tertiary ink to keep the eye on the pill first.
    /// </remarks>
    internal partial class BriefingSectionHeader : CompositeDrawable
    {
        public BriefingSectionHeader(string title, string subtitle, Color4? accent = null)
        {
            var tint = accent ?? BriefingTheme.AccentCyan;

            RelativeSizeAxes = Axes.X;
            Height = 36;
            Margin = new MarginPadding { Top = BriefingTheme.SpacingXs };

            InternalChildren = new Drawable[]
            {
                // Hairline rule that fades out at both ends
                new Box
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = 1,
                    Colour = ColourInfo.GradientHorizontal(
                        Color4.White.Opacity(0.10f),
                        Color4.White.Opacity(0)),
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(BriefingTheme.SpacingSm, 0),
                    Children = new Drawable[]
                    {
                        new BriefingPill(title, tint),
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = subtitle,
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeBody - 1.5f, weight: FontWeight.SemiBold),
                            Colour = Color4.White.Opacity(BriefingTheme.InkTertiary),
                        },
                    },
                },
            };
        }
    }
}
