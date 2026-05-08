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
    /// Specialised briefing card that lists the top-play recalcs since the
    /// previous snapshot — gains on top, losses underneath, both capped at
    /// five rows with an "+ N more" overflow line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The previous implementation duplicated the best gain / worst loss as
    /// "BEST GAIN" / "WORST LOSS" highlight rows in a footer below the lists,
    /// even though the same scores were already the first entries in their
    /// respective sections (since both lists are sorted by absolute delta).
    /// That redundancy doubled the footprint of the card without adding
    /// information, so this version drops it.
    /// </para>
    /// <para>
    /// Each row now uses a coloured caret icon + a fixed-width delta column
    /// + the truncated score title, which scans top-to-bottom as a clean
    /// table even with the auto-sized text flow layout.
    /// </para>
    /// </remarks>
    internal partial class BriefingRecalcCard : CompositeDrawable, IHasTooltip
    {
        public LocalisableString TooltipText { get; set; }

        private const int max_rows_per_section = 5;
        private const float delta_column_width = 84f;

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
                Margin = new MarginPadding { Left = 36 + BriefingTheme.SpacingMd },
                Padding = new MarginPadding { Right = BriefingTheme.SpacingLg },
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, BriefingTheme.SpacingXs + 1),
            };

            // Kicker
            body.Add(new OsuSpriteText
            {
                Text = "RECALCULATION WATCH",
                Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                Spacing = new Vector2(BriefingTheme.CaptionTracking * BriefingTheme.TypeCaption, 0),
                Colour = accent,
            });

            // Headline
            string headline = hasChanges
                ? $"{changes.Count} top {(changes.Count == 1 ? "score" : "scores")} recalculated"
                : "No top play recalcs detected";

            body.Add(new OsuSpriteText
            {
                Text = headline,
                Font = OsuFont.GetFont(size: BriefingTheme.TypeHeadline, weight: FontWeight.Bold),
                Colour = Color4.White.Opacity(BriefingTheme.InkPrimary),
            });

            if (!hasChanges)
            {
                body.Add(new OsuSpriteText
                {
                    Text = "Your top plays match the last briefing snapshot.",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                });
            }
            else
            {
                var gains = changes.Where(c => c.Delta > 0).OrderByDescending(c => c.Delta).ToList();
                var losses = changes.Where(c => c.Delta < 0).OrderBy(c => c.Delta).ToList();

                // Slim divider above the rows so the sections feel grouped.
                body.Add(buildSeparator(top: BriefingTheme.SpacingSm - 2, bottom: BriefingTheme.SpacingXs));

                if (gains.Count > 0)
                    addSection(body, "TOP GAINS", FontAwesome.Solid.CaretUp, BriefingTheme.AccentGain, gains);

                if (gains.Count > 0 && losses.Count > 0)
                    body.Add(buildSeparator(top: BriefingTheme.SpacingSm, bottom: BriefingTheme.SpacingXs));

                if (losses.Count > 0)
                    addSection(body, "TOP LOSSES", FontAwesome.Solid.CaretDown, BriefingTheme.AccentLoss, losses);
            }

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
                        // Icon tile (matches BriefingCard for visual consistency).
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
                                    Icon = FontAwesome.Solid.Sync,
                                    Colour = accent,
                                },
                            },
                        },
                        body,
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
                    Colour = ColourInfo.GradientHorizontal(
                        Color4.White.Opacity(0.10f),
                        Color4.White.Opacity(0)),
                },
            };
        }

        private static void addSection(FillFlowContainer body, string title, IconUsage caret, Color4 colour, List<BriefingScoreChange> rows)
        {
            // Section title (e.g. "TOP GAINS") — aligned with the body kicker but tinted gain/loss colour.
            body.Add(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(BriefingTheme.SpacingXs, 0),
                Margin = new MarginPadding { Bottom = BriefingTheme.SpacingXs - 2 },
                Children = new Drawable[]
                {
                    new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(BriefingTheme.TypeCaption - 1f),
                        Icon = caret,
                        Colour = colour.Opacity(0.85f),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = title,
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                        Spacing = new Vector2(BriefingTheme.CaptionTracking * BriefingTheme.TypeCaption, 0),
                        Colour = colour.Opacity(0.85f),
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
                    Spacing = new Vector2(BriefingTheme.SpacingSm, 0),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = formatSignedPP(change.Delta),
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeBody - 1f, weight: FontWeight.Bold),
                            Colour = colour,
                            Width = delta_column_width,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = trim(change.Title, 46),
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeBody - 1f, weight: FontWeight.SemiBold),
                            Colour = Color4.White.Opacity(BriefingTheme.InkPrimary - 0.16f),
                        },
                    },
                });
            }

            if (rows.Count > max_rows_per_section)
            {
                body.Add(new OsuSpriteText
                {
                    Text = $"+ {rows.Count - max_rows_per_section} more",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeBody - 2f, weight: FontWeight.SemiBold),
                    Colour = Color4.White.Opacity(BriefingTheme.InkTertiary),
                    Margin = new MarginPadding { Left = delta_column_width + BriefingTheme.SpacingSm, Top = 1 },
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
