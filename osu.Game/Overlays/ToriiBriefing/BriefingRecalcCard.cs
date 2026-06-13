// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ToriiBriefing
{
    /// <summary>
    /// Specialised briefing card listing the top-play recalcs since the
    /// previous snapshot — gains on top, losses underneath, both capped
    /// at five rows with an "+ N more" overflow line.
    /// </summary>
    /// <remarks>
    /// Same surface vocabulary as <see cref="BriefingCard"/> (clean
    /// elevated dark surface + black drop shadow + saturated icon tile).
    /// The body is a tight two-column table: a fixed-width signed-pp
    /// column on the left, the truncated score title on the right. Each
    /// section is captioned by a tinted caret + uppercase label.
    /// </remarks>
    internal partial class BriefingRecalcCard : CompositeDrawable, IHasTooltip
    {
        public LocalisableString TooltipText { get; set; }

        private const int max_rows_per_section = 5;
        private const float delta_column_width = 64f;
        private const float icon_tile_size = 36f;

        public BriefingRecalcCard(List<BriefingScoreChange> changes)
        {
            bool hasChanges = changes.Count > 0;
            var accent = hasChanges ? BriefingTheme.AccentPink : BriefingTheme.AccentCyan;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            var body = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Margin = new MarginPadding { Left = icon_tile_size + BriefingTheme.SpacingMd - 2 },
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
            };

            body.Add(new OsuSpriteText
            {
                Text = "RECALCULATION WATCH",
                Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                Spacing = new Vector2(BriefingTheme.CaptionTracking * BriefingTheme.TypeCaption, 0),
                Colour = accent,
            });

            string headline = hasChanges
                ? $"{changes.Count} top {(changes.Count == 1 ? "score" : "scores")} recalculated"
                : "No top play recalcs detected";

            body.Add(new OsuSpriteText
            {
                Text = headline,
                Font = OsuFont.GetFont(size: BriefingTheme.TypeHeadline, weight: FontWeight.SemiBold),
                Colour = Color4.White.Opacity(BriefingTheme.InkPrimary),
                Margin = new MarginPadding { Top = 1 },
            });

            if (!hasChanges)
            {
                body.Add(new OsuSpriteText
                {
                    Text = "Your top plays match the last briefing snapshot.",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.Regular),
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                    Margin = new MarginPadding { Top = 1 },
                });
            }
            else
            {
                var gains = changes.Where(c => c.Delta > 0).OrderByDescending(c => c.Delta).ToList();
                var losses = changes.Where(c => c.Delta < 0).OrderBy(c => c.Delta).ToList();

                body.Add(buildSeparator(top: BriefingTheme.SpacingSm, bottom: BriefingTheme.SpacingXs - 1));

                if (gains.Count > 0)
                    addSection(body, "TOP GAINS", FontAwesome.Solid.CaretUp, BriefingTheme.AccentGain, gains);

                if (gains.Count > 0 && losses.Count > 0)
                    body.Add(buildSeparator(top: BriefingTheme.SpacingSm, bottom: BriefingTheme.SpacingXs - 1));

                if (losses.Count > 0)
                    addSection(body, "TOP LOSSES", FontAwesome.Solid.CaretDown, BriefingTheme.AccentLoss, losses);
            }

            InternalChild = new BriefingGlass
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                SurfaceLift = 1.4f,
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
                        buildIconTile(FontAwesome.Solid.Sync, accent),
                        body,
                    },
                },
            };
        }

        private static Drawable buildIconTile(IconUsage icon, Color4 accent)
        {
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

        private static Drawable buildSeparator(float top, float bottom)
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Margin = new MarginPadding { Top = top, Bottom = bottom },
                Child = new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 1,
                    Colour = Color4.White.Opacity(0.07f),
                },
            };
        }

        private static void addSection(FillFlowContainer body, string title, IconUsage caret, Color4 colour, List<BriefingScoreChange> rows)
        {
            body.Add(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(BriefingTheme.SpacingXs, 0),
                Margin = new MarginPadding { Bottom = 1 },
                Children = new Drawable[]
                {
                    new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(BriefingTheme.TypeCaption - 1f),
                        Icon = caret,
                        Colour = colour,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = title,
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                        Spacing = new Vector2(BriefingTheme.CaptionTracking * BriefingTheme.TypeCaption, 0),
                        Colour = colour,
                    },
                },
            });

            int rowsToShow = Math.Min(max_rows_per_section, rows.Count);

            for (int i = 0; i < rowsToShow; i++)
            {
                var change = rows[i];
                body.Add(new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(BriefingTheme.SpacingMd - 4, 0),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = formatSignedPP(change.Delta),
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeBody - 1f, weight: FontWeight.SemiBold),
                            Colour = colour,
                            Width = delta_column_width,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = trim(change.Title, 50),
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeBody - 1f, weight: FontWeight.Regular),
                            Colour = Color4.White.Opacity(BriefingTheme.InkPrimary - 0.10f),
                        },
                    },
                });
            }

            if (rows.Count > max_rows_per_section)
            {
                body.Add(new OsuSpriteText
                {
                    Text = $"+ {rows.Count - max_rows_per_section} more",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeBody - 2f, weight: FontWeight.Regular),
                    Colour = Color4.White.Opacity(BriefingTheme.InkTertiary),
                    Margin = new MarginPadding { Left = delta_column_width + BriefingTheme.SpacingMd - 4, Top = 1 },
                });
            }
        }

        private static string formatSignedPP(double pp) => $"{(pp >= 0 ? "+" : string.Empty)}{pp:N2}pp";

        private static string trim(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return $"{text[..Math.Max(0, maxLength - 3)]}...";
        }
    }
}
