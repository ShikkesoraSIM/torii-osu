// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Users.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    // ═════════════════════════════════════════════════════════════════
    // Carousel pages for ToriiServerPulsePopover
    // ═════════════════════════════════════════════════════════════════
    //
    // Four pages, each is a self-contained drawable that the popover
    // composes side-by-side in its swipeable carousel:
    //
    //   OverviewPage  – the at-a-glance dashboard (3 stats + sparkline
    //                   + a small top-map preview).
    //   HotMapsPage   – top 5 most-played beatmaps right now, vertical
    //                   list with cover thumbnails.
    //   LivePlaysPage – up to 8 most-recent in-flight plays with
    //                   player avatar, map title, ruleset chip, and a
    //                   "started Xs ago" timestamp.
    //   ModeSplitPage – per-ruleset breakdown of currently-playing
    //                   sessions, rendered as labelled horizontal bars.
    //
    // Layout strategy
    // ---------------
    // Every list row is a <see cref="GridContainer"/> with absolute
    // column dimensions for fixed-size elements (medal chip, cover
    // square, avatar, ruleset glyph) and ONE distributed column for
    // text. Last column is auto-sized for the right-side badge (plays
    // count / time chip). This is what makes the popover scale
    // correctly with UI scale settings — every dimension is in logical
    // pixels, GridContainer reflows automatically when the parent
    // resizes, and there are no fragile X-offset + RelativeSizeAxes
    // combinations that overflow the parent at non-default scales.
    //
    // Texture loading
    // ---------------
    // Avatars use lazer's <see cref="UpdateableAvatar"/> which inherits
    // from ModelBackedDrawable and handles async loading + fade
    // transitions natively. Covers use a small <see cref="LazyCoverImage"/>
    // helper that does the same shape via DelayedLoadUnloadWrapper —
    // image only appears once the texture is fully loaded, fading in
    // over a placeholder box. Fixes the "white square while loading"
    // visual bug on the previous iteration.

    // ═════════════════════════════════════════════════════════════════
    // OverviewPage
    // ═════════════════════════════════════════════════════════════════

    public partial class OverviewPage : CompositeDrawable
    {
        private static readonly Color4 torii_red = new Color4(204, 41, 41, 255);
        private static readonly Color4 muted_white = new Color4(255, 255, 255, 130);
        private static readonly Color4 placeholder_dark = new Color4(28, 24, 32, 255);

        private TweenedNumber playingNumber = null!;
        private TweenedNumber playsPerMinuteNumber = null!;
        private TweenedNumber onlineNumber = null!;
        private SparklineGraph sparkline = null!;
        private OverviewTopMapStrip topMapStrip = null!;

        public OverviewPage()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            playingNumber = new TweenedNumber("playing now", torii_red);
            playsPerMinuteNumber = new TweenedNumber("plays/min", new Color4(255, 220, 130, 255));
            onlineNumber = new TweenedNumber("online", new Color4(150, 220, 255, 255));

            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding { Horizontal = 18 },
                Spacing = new Vector2(0, 12),
                Children = new Drawable[]
                {
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        ColumnDimensions = new[]
                        {
                            new Dimension(),
                            new Dimension(),
                            new Dimension(),
                        },
                        RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                        Content = new[]
                        {
                            new Drawable[]
                            {
                                playingNumber,
                                playsPerMinuteNumber,
                                onlineNumber,
                            },
                        },
                    },
                    sparkline = new SparklineGraph
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 64,
                    },
                    topMapStrip = new OverviewTopMapStrip
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 78,
                    },
                },
            };
        }

        public void SetPlaying(int value) => playingNumber?.SetValue(value);
        public void SetPlaysPerMinute(int value) => playsPerMinuteNumber?.SetValue(value);
        public void SetOnline(int value) => onlineNumber?.SetValue(value);
        public void SetSparkline(IReadOnlyList<int>? buckets) => sparkline?.SetBuckets(buckets);
        public void SetTopMap(APIToriiServerPulseTopMap? map) => topMapStrip?.SetMap(map);

        // ─── TweenedNumber ───────────────────────────────────────────
        private partial class TweenedNumber : CompositeDrawable
        {
            private readonly string caption;
            private readonly Color4 accent;

            private OsuSpriteText valueText = null!;

            private double currentDisplayValue;
            private double targetValue;
            private double tweenStartValue;
            private double tweenStartTime = double.NegativeInfinity;
            private const double tween_duration_ms = 280;

            public TweenedNumber(string caption, Color4 accent)
            {
                this.caption = caption;
                this.accent = accent;
                AutoSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 1),
                    Children = new Drawable[]
                    {
                        valueText = new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = OsuFont.GetFont(size: 28, weight: FontWeight.Bold, fixedWidth: false),
                            Text = "0",
                            Colour = Color4.White,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                            Spacing = new Vector2(0.7f, 0),
                            Text = caption.ToUpperInvariant(),
                            Colour = accent.Opacity(0.85f),
                        },
                    }
                };
            }

            public void SetValue(int newValue)
            {
                if (valueText == null)
                {
                    targetValue = newValue;
                    currentDisplayValue = newValue;
                    return;
                }

                if (Math.Abs(newValue - currentDisplayValue) < 0.5)
                {
                    valueText.Text = newValue.ToString();
                    return;
                }

                tweenStartValue = currentDisplayValue;
                targetValue = newValue;
                tweenStartTime = Time.Current;

                valueText.ClearTransforms();
                valueText.ScaleTo(1.10f, 90, Easing.OutQuint).Then().ScaleTo(1f, 220, Easing.OutBack);
                valueText.FadeColour(accent, 80, Easing.OutQuint).Then().FadeColour(Color4.White, 360, Easing.OutQuint);
            }

            protected override void Update()
            {
                base.Update();

                if (valueText == null) return;
                if (double.IsNegativeInfinity(tweenStartTime)) return;
                if (Math.Abs(currentDisplayValue - targetValue) < 0.001 && Math.Abs(Time.Current - tweenStartTime) > tween_duration_ms) return;

                double elapsed = Time.Current - tweenStartTime;
                if (elapsed >= tween_duration_ms)
                {
                    currentDisplayValue = targetValue;
                }
                else
                {
                    double t = elapsed / tween_duration_ms;
                    double eased = 1 - Math.Pow(1 - t, 3);
                    currentDisplayValue = tweenStartValue + (targetValue - tweenStartValue) * eased;
                }

                valueText.Text = ((int)Math.Round(currentDisplayValue)).ToString();
            }
        }

        // ─── SparklineGraph ──────────────────────────────────────────
        private partial class SparklineGraph : CompositeDrawable
        {
            private FillFlowContainer barsFlow = null!;
            private OsuSpriteText emptyHint = null!;

            private readonly List<Box> bars = new List<Box>();
            private const float min_bar_height = 0.06f;

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 46,
                            Children = new Drawable[]
                            {
                                barsFlow = new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(3, 0),
                                    Anchor = Anchor.BottomLeft,
                                    Origin = Anchor.BottomLeft,
                                },
                                emptyHint = new OsuSpriteText
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Text = "the gates are quiet",
                                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.Regular),
                                    Colour = muted_white,
                                    Alpha = 0,
                                },
                            },
                        },
                        new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 14,
                            Padding = new MarginPadding { Top = 4 },
                            Child = new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = "last 12 minutes",
                                Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                                Spacing = new Vector2(0.7f, 0),
                                Colour = muted_white,
                            },
                        }
                    }
                };
            }

            public void SetBuckets(IReadOnlyList<int>? buckets)
            {
                if (barsFlow == null || emptyHint == null) return;

                if (buckets == null || buckets.Count == 0)
                {
                    foreach (var bar in bars)
                        bar.ResizeHeightTo(min_bar_height, 240, Easing.OutQuint);
                    emptyHint.FadeIn(220, Easing.OutQuint);
                    return;
                }

                emptyHint.FadeOut(160, Easing.OutQuint);

                if (bars.Count != buckets.Count)
                {
                    barsFlow.Clear();
                    bars.Clear();

                    for (int i = 0; i < buckets.Count; i++)
                    {
                        var bar = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            Width = 1f / buckets.Count - 0.01f,
                            Height = min_bar_height,
                            Colour = ColourInfo.GradientVertical(
                                colourFor(i, buckets.Count, 1f),
                                colourFor(i, buckets.Count, 0.5f)),
                        };
                        bars.Add(bar);
                        barsFlow.Add(bar);
                    }
                }

                int max = 0;
                for (int i = 0; i < buckets.Count; i++)
                    if (buckets[i] > max) max = buckets[i];

                for (int i = 0; i < bars.Count; i++)
                {
                    int value = i < buckets.Count ? buckets[i] : 0;
                    float ratio = max <= 0 ? 0f : (float)value / max;
                    float height = Math.Max(min_bar_height, ratio * 0.96f);
                    bars[i].ResizeHeightTo(height, 320, Easing.OutQuint);
                }
            }

            private static Color4 colourFor(int index, int total, float alpha)
            {
                float t = (float)index / Math.Max(1, total - 1);
                Color4 dim = new Color4(110, 80, 90, 200);
                Color4 hot = new Color4(214, 60, 70, 255);
                return new Color4(
                    (byte)(dim.R * 255 * (1 - t) + hot.R * 255 * t),
                    (byte)(dim.G * 255 * (1 - t) + hot.G * 255 * t),
                    (byte)(dim.B * 255 * (1 - t) + hot.B * 255 * t),
                    (byte)(255 * alpha));
            }
        }

        // ─── OverviewTopMapStrip ─────────────────────────────────────
        // Compact preview of the top map. GridContainer-based layout:
        // [64 cover | 12 gap | distributed text column]. Empty state
        // overlays the whole strip when no plays in the last 5 min.
        // ─────────────────────────────────────────────────────────────
        private partial class OverviewTopMapStrip : CompositeDrawable
        {
            private LazyCoverImage cover = null!;
            private TruncatingSpriteText titleText = null!;
            private TruncatingSpriteText artistText = null!;
            private OsuSpriteText metaText = null!;
            private Container contentContainer = null!;
            private OsuSpriteText emptyText = null!;

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    contentContainer = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Child = new GridContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            ColumnDimensions = new[]
                            {
                                new Dimension(GridSizeMode.Absolute, 64),
                                new Dimension(GridSizeMode.Absolute, 12),
                                new Dimension(),
                            },
                            RowDimensions = new[] { new Dimension() },
                            Content = new[]
                            {
                                new Drawable[]
                                {
                                    new Container
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Size = new Vector2(64, 64),
                                        Children = new Drawable[]
                                        {
                                            cover = new LazyCoverImage(placeholderColour: placeholder_dark)
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                CornerRadius = 8,
                                            },
                                            // Subtle vermillion gradient sheen
                                            // overlay so the cover reads as
                                            // part of the Torii panel rather
                                            // than a foreign square.
                                            new Container
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                Masking = true,
                                                CornerRadius = 8,
                                                Child = new Box
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    Colour = ColourInfo.GradientVertical(
                                                        torii_red.Opacity(0.0f),
                                                        torii_red.Opacity(0.18f)),
                                                    Blending = BlendingParameters.Additive,
                                                },
                                            },
                                        },
                                    },
                                    new Container(),
                                    new FillFlowContainer
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        AutoSizeAxes = Axes.Y,
                                        RelativeSizeAxes = Axes.X,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, 2),
                                        Children = new Drawable[]
                                        {
                                            new OsuSpriteText
                                            {
                                                Text = "TOP MAP RIGHT NOW",
                                                Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                                                Spacing = new Vector2(0.8f, 0),
                                                Colour = torii_red,
                                            },
                                            titleText = new TruncatingSpriteText
                                            {
                                                Text = "—",
                                                Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                                                Colour = Color4.White,
                                                RelativeSizeAxes = Axes.X,
                                            },
                                            artistText = new TruncatingSpriteText
                                            {
                                                Text = "—",
                                                Font = OsuFont.GetFont(size: 11, weight: FontWeight.Regular),
                                                Colour = muted_white,
                                                RelativeSizeAxes = Axes.X,
                                            },
                                            metaText = new OsuSpriteText
                                            {
                                                Text = "—",
                                                Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                                                Colour = torii_red.Opacity(0.85f),
                                                Spacing = new Vector2(0.4f, 0),
                                            },
                                        }
                                    },
                                }
                            }
                        }
                    },
                    emptyText = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = "no plays in the last 5 minutes",
                        Font = OsuFont.GetFont(size: 10, weight: FontWeight.Regular),
                        Colour = muted_white,
                        Alpha = 0,
                    },
                };
            }

            public void SetMap(APIToriiServerPulseTopMap? map)
            {
                if (contentContainer == null || emptyText == null) return;

                if (map == null)
                {
                    contentContainer.FadeOut(180, Easing.OutQuint);
                    emptyText.FadeIn(220, Easing.OutQuint);
                    return;
                }

                contentContainer.FadeIn(220, Easing.OutQuint);
                emptyText.FadeOut(180, Easing.OutQuint);

                titleText.Text = map.DisplayTitle;
                artistText.Text = map.DisplayArtist;
                metaText.Text = $"[{map.Version}]  ·  {map.PlayCount5Min} play{(map.PlayCount5Min == 1 ? "" : "s")} in 5min";

                cover.SetUrl(map.BestCoverUrl);
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // HotMapsPage
    // ═════════════════════════════════════════════════════════════════

    public partial class HotMapsPage : CompositeDrawable
    {
        private static readonly Color4 torii_red = new Color4(204, 41, 41, 255);
        private static readonly Color4 muted_white = new Color4(255, 255, 255, 130);
        private static readonly Color4 placeholder_dark = new Color4(28, 24, 32, 255);

        private FillFlowContainer rowsFlow = null!;
        private OsuSpriteText emptyText = null!;
        private OsuSpriteText headerText = null!;

        public HotMapsPage()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding { Horizontal = 18 },
                    Spacing = new Vector2(0, 6),
                    Children = new Drawable[]
                    {
                        headerText = new OsuSpriteText
                        {
                            Text = "HOT MAPS · LAST 5 MIN",
                            Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                            Spacing = new Vector2(0.8f, 0),
                            Colour = torii_red,
                        },
                        rowsFlow = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 4),
                        },
                    },
                },
                emptyText = new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "no plays in the last 5 minutes",
                    Font = OsuFont.GetFont(size: 11, weight: FontWeight.Regular),
                    Colour = muted_white,
                    Alpha = 0,
                },
            };
        }

        public void SetMaps(IReadOnlyList<APIToriiServerPulseTopMap>? maps)
        {
            if (rowsFlow == null || emptyText == null) return;

            if (maps == null || maps.Count == 0)
            {
                rowsFlow.Clear();
                emptyText.FadeIn(220, Easing.OutQuint);
                headerText.FadeTo(0.4f, 220, Easing.OutQuint);
                return;
            }

            emptyText.FadeOut(180, Easing.OutQuint);
            headerText.FadeIn(220, Easing.OutQuint);
            headerText.Text = $"HOT MAPS · TOP {maps.Count}";

            rowsFlow.Clear();
            for (int i = 0; i < maps.Count; i++)
                rowsFlow.Add(new HotMapRow(i + 1, maps[i]));
        }

        // ─── HotMapRow ───────────────────────────────────────────────
        // Layout: GridContainer with absolute columns
        //   [22 medal | 6 gap | 36 cover | 8 gap | flex text | autosize plays badge]
        // No more X-offset + RelativeSizeAxes overflow that was making
        // the title text overlap the cover at certain widths.
        // ─────────────────────────────────────────────────────────────
        private partial class HotMapRow : CompositeDrawable
        {
            private readonly int rank;
            private readonly APIToriiServerPulseTopMap map;

            public HotMapRow(int rank, APIToriiServerPulseTopMap map)
            {
                this.rank = rank;
                this.map = map;
                RelativeSizeAxes = Axes.X;
                Height = 40;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White.Opacity(0.025f),
                    },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Horizontal = 4 },
                        ColumnDimensions = new[]
                        {
                            new Dimension(GridSizeMode.Absolute, 22), // medal
                            new Dimension(GridSizeMode.Absolute, 8),  // gap
                            new Dimension(GridSizeMode.Absolute, 36), // cover
                            new Dimension(GridSizeMode.Absolute, 10), // gap
                            new Dimension(),                           // text — fills remainder
                            new Dimension(GridSizeMode.Absolute, 6),  // padding before badge
                            // Fixed width for the badge column. AutoSize
                            // here was the source of the "1× PLA" clipping
                            // upstream feedback flagged — at certain
                            // parent widths the GridContainer was
                            // computing a sub-pixel-narrow cell that
                            // clipped the badge content. 78px comfortably
                            // fits the largest expected badge ("999×
                            // PLAYS") plus padding.
                            new Dimension(GridSizeMode.Absolute, 78), // plays badge
                        },
                        RowDimensions = new[] { new Dimension() },
                        Content = new[]
                        {
                            new Drawable[]
                            {
                                new RankMedal(rank)
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                },
                                new Container(),
                                new Container
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Size = new Vector2(36, 36),
                                    Child = new LazyCoverImage(placeholderColour: placeholder_dark)
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        CornerRadius = 6,
                                        Url = map.BestCoverUrl,
                                    },
                                },
                                new Container(),
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    AutoSizeAxes = Axes.Y,
                                    RelativeSizeAxes = Axes.X,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, 1),
                                    Children = new Drawable[]
                                    {
                                        new TruncatingSpriteText
                                        {
                                            Text = map.DisplayTitle,
                                            Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                                            Colour = Color4.White,
                                            RelativeSizeAxes = Axes.X,
                                        },
                                        new TruncatingSpriteText
                                        {
                                            Text = $"{map.DisplayArtist}  ·  [{map.Version}]",
                                            Font = OsuFont.GetFont(size: 10, weight: FontWeight.Regular),
                                            Colour = new Color4(255, 255, 255, 155),
                                            RelativeSizeAxes = Axes.X,
                                        },
                                    }
                                },
                                new Container(),
                                new PlaysBadge(map.PlayCount5Min)
                                {
                                    Anchor = Anchor.CentreRight,
                                    Origin = Anchor.CentreRight,
                                },
                            }
                        }
                    },
                };
            }
        }

        // ─── RankMedal ───────────────────────────────────────────────
        private partial class RankMedal : CompositeDrawable
        {
            public RankMedal(int rank)
            {
                Size = new Vector2(22, 22);

                Color4 fillColour;
                Color4 textColour;

                switch (rank)
                {
                    case 1:
                        fillColour = new Color4(255, 200, 80, 255);
                        textColour = new Color4(40, 28, 8, 255);
                        break;
                    case 2:
                        fillColour = new Color4(192, 200, 220, 255);
                        textColour = new Color4(28, 32, 40, 255);
                        break;
                    case 3:
                        fillColour = new Color4(195, 130, 80, 255);
                        textColour = new Color4(40, 22, 10, 255);
                        break;
                    default:
                        fillColour = new Color4(70, 70, 90, 255);
                        textColour = new Color4(220, 220, 230, 220);
                        break;
                }

                Masking = true;
                CornerRadius = 11;
                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = fillColour,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = rank.ToString(),
                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                        Colour = textColour,
                    },
                };
            }
        }

        // ─── PlaysBadge ──────────────────────────────────────────────
        // Auto-size pill on the right. Plurals collapsed to "Nx PLAY"
        // / "Nx PLAYS" — no truncation needed because the AutoSize
        // column accommodates whatever width the badge needs.
        // ─────────────────────────────────────────────────────────────
        private partial class PlaysBadge : CompositeDrawable
        {
            public PlaysBadge(int plays)
            {
                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 10;
                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = torii_red.Opacity(0.18f),
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Padding = new MarginPadding { Horizontal = 8, Vertical = 3 },
                        Spacing = new Vector2(3, 0),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = $"{plays}×",
                                Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                                Colour = torii_red,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = "PLAY" + (plays == 1 ? "" : "S"),
                                Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                                Spacing = new Vector2(0.6f, 0),
                                Colour = torii_red.Opacity(0.85f),
                                Margin = new MarginPadding { Top = 1 },
                            },
                        }
                    },
                };
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // LivePlaysPage
    // ═════════════════════════════════════════════════════════════════

    public partial class LivePlaysPage : CompositeDrawable
    {
        private static readonly Color4 torii_red = new Color4(204, 41, 41, 255);
        private static readonly Color4 muted_white = new Color4(255, 255, 255, 130);

        private FillFlowContainer rowsFlow = null!;
        private OsuSpriteText emptyText = null!;
        private OsuSpriteText headerText = null!;

        public LivePlaysPage()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding { Horizontal = 18 },
                    Spacing = new Vector2(0, 4),
                    Children = new Drawable[]
                    {
                        headerText = new OsuSpriteText
                        {
                            Text = "LIVE PLAYS",
                            Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                            Spacing = new Vector2(0.8f, 0),
                            Colour = torii_red,
                        },
                        rowsFlow = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 3),
                        },
                    },
                },
                emptyText = new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = "nobody is playing right now",
                    Font = OsuFont.GetFont(size: 11, weight: FontWeight.Regular),
                    Colour = muted_white,
                    Alpha = 0,
                },
            };
        }

        public void SetPlays(IReadOnlyList<APIToriiServerPulseRecentPlay>? plays)
        {
            if (rowsFlow == null || emptyText == null) return;

            if (plays == null || plays.Count == 0)
            {
                rowsFlow.Clear();
                emptyText.FadeIn(220, Easing.OutQuint);
                headerText.FadeTo(0.4f, 220, Easing.OutQuint);
                return;
            }

            emptyText.FadeOut(180, Easing.OutQuint);
            headerText.FadeIn(220, Easing.OutQuint);
            headerText.Text = $"LIVE PLAYS · {plays.Count}";

            rowsFlow.Clear();
            foreach (var play in plays)
                rowsFlow.Add(new LivePlayRow(play));
        }

        // ─── LivePlayRow ─────────────────────────────────────────────
        // Two-line row with a clearer narrative. Layout:
        //   [28 avatar | 8 gap | flex text-column (username + map) | 6 pad | absolute 78 status badge]
        //
        // The previous "ruleset O/T/C/M chip" was dropped — upstream
        // feedback said it read as a mystery dot ("¿qué es ese
        // circulito?"). Status badge replaces it with something
        // readable: pp + rank for submitted scores, "PLAYING · 32s"
        // for in-flight. Status badge column is fixed-width (78px) so
        // there's no AutoSize-column-clipping artefact.
        // ─────────────────────────────────────────────────────────────
        private partial class LivePlayRow : CompositeDrawable
        {
            private readonly APIToriiServerPulseRecentPlay play;

            public LivePlayRow(APIToriiServerPulseRecentPlay play)
            {
                this.play = play;
                RelativeSizeAxes = Axes.X;
                Height = 36;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var apiUser = new APIUser
                {
                    Id = (int)play.UserId,
                    Username = play.Username,
                    AvatarUrl = play.AvatarUrl,
                };

                Drawable statusBadge = play.IsSubmitted
                    ? new SubmittedScoreBadge(play)
                    : (Drawable)new PlayingNowBadge(play.StartedSecondsAgo);

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White.Opacity(0.025f),
                    },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Horizontal = 6 },
                        ColumnDimensions = new[]
                        {
                            new Dimension(GridSizeMode.Absolute, 28), // avatar
                            new Dimension(GridSizeMode.Absolute, 8),  // gap
                            new Dimension(),                           // text — flex
                            new Dimension(GridSizeMode.Absolute, 6),  // pad before badge
                            new Dimension(GridSizeMode.Absolute, 78), // status badge — fixed width
                        },
                        RowDimensions = new[] { new Dimension() },
                        Content = new[]
                        {
                            new Drawable[]
                            {
                                new UpdateableAvatar(apiUser, isInteractive: false, showGuestOnNull: false)
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Size = new Vector2(28, 28),
                                    Masking = true,
                                    CornerRadius = 14,
                                },
                                new Container(),
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    AutoSizeAxes = Axes.Y,
                                    RelativeSizeAxes = Axes.X,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, 1),
                                    Children = new Drawable[]
                                    {
                                        new OsuSpriteText
                                        {
                                            Text = string.IsNullOrEmpty(play.Username) ? "—" : play.Username,
                                            Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                                            Colour = Color4.White,
                                        },
                                        new TruncatingSpriteText
                                        {
                                            Text = string.IsNullOrEmpty(play.DisplayTitle) ? "" : play.DisplayTitle,
                                            Font = OsuFont.GetFont(size: 10, weight: FontWeight.Regular),
                                            Colour = new Color4(255, 255, 255, 155),
                                            RelativeSizeAxes = Axes.X,
                                        },
                                    }
                                },
                                new Container(),
                                new Container
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    RelativeSizeAxes = Axes.Both,
                                    Child = statusBadge.With(d =>
                                    {
                                        d.Anchor = Anchor.Centre;
                                        d.Origin = Anchor.Centre;
                                    }),
                                },
                            }
                        }
                    },
                };
            }
        }

        // ─── PlayingNowBadge ─────────────────────────────────────────
        // For in-flight plays. "PLAYING · 32s" two-line. Vermillion
        // tint matches the "live" connotation.
        // ─────────────────────────────────────────────────────────────
        private partial class PlayingNowBadge : CompositeDrawable
        {
            public PlayingNowBadge(int secondsAgo)
            {
                AutoSizeAxes = Axes.Both;

                string secondsText = secondsAgo < 5 ? "now"
                    : secondsAgo < 60 ? $"{secondsAgo}s"
                    : secondsAgo < 3600 ? $"{secondsAgo / 60}m"
                    : "1h+";

                InternalChild = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 9,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = torii_red.Opacity(0.20f),
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 0),
                            Padding = new MarginPadding { Horizontal = 9, Vertical = 4 },
                            Children = new Drawable[]
                            {
                                new OsuSpriteText
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Text = "PLAYING",
                                    Font = OsuFont.GetFont(size: 8, weight: FontWeight.Bold),
                                    Spacing = new Vector2(0.6f, 0),
                                    Colour = torii_red,
                                },
                                new OsuSpriteText
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Text = secondsText,
                                    Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                                    Colour = Color4.White,
                                },
                            }
                        }
                    }
                };
            }
        }

        // ─── SubmittedScoreBadge ─────────────────────────────────────
        // For a submitted score. Two-line: rank letter + accuracy on
        // top, +Npp on bottom. Whole badge tinted by the rank colour
        // so the eye reads "they just got an S, look how big the pp
        // was". Replaces the "+1 PP" feedback the user was missing.
        // ─────────────────────────────────────────────────────────────
        private partial class SubmittedScoreBadge : CompositeDrawable
        {
            public SubmittedScoreBadge(APIToriiServerPulseRecentPlay play)
            {
                AutoSizeAxes = Axes.Both;

                Color4 rankColour = colourForRank(play.Rank);
                string rankText = string.IsNullOrEmpty(play.Rank) ? "—" : play.Rank;
                string accText = $"{play.Accuracy * 100:0.##}%";
                string ppText = play.Pp >= 1 ? $"+{play.Pp:0}pp" : "+0pp";

                InternalChild = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 9,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = rankColour.Opacity(0.18f),
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, -1),
                            Padding = new MarginPadding { Horizontal = 8, Vertical = 4 },
                            Children = new Drawable[]
                            {
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(4, 0),
                                    Children = new Drawable[]
                                    {
                                        new OsuSpriteText
                                        {
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                            Text = rankText,
                                            Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                                            Colour = rankColour,
                                        },
                                        new OsuSpriteText
                                        {
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                            Text = accText,
                                            Font = OsuFont.GetFont(size: 9, weight: FontWeight.Regular),
                                            Colour = new Color4(255, 255, 255, 175),
                                            Margin = new MarginPadding { Top = 2 },
                                        },
                                    },
                                },
                                new OsuSpriteText
                                {
                                    Anchor = Anchor.TopCentre,
                                    Origin = Anchor.TopCentre,
                                    Text = ppText,
                                    Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                                    Colour = Color4.White,
                                },
                            }
                        }
                    }
                };
            }

            // Standard osu! rank palette — gold for SS / S, descending
            // through silver, bronze, into muted greys for low ranks.
            private static Color4 colourForRank(string rank)
            {
                return (rank ?? string.Empty).ToUpperInvariant() switch
                {
                    "SS" or "X" or "XH" or "SSH" => new Color4(255, 215, 90, 255),
                    "S" or "SH"                  => new Color4(255, 200, 80, 255),
                    "A"                          => new Color4(110, 220, 130, 255),
                    "B"                          => new Color4(100, 175, 255, 255),
                    "C"                          => new Color4(195, 130, 220, 255),
                    "D" or "F"                   => new Color4(220, 100, 100, 255),
                    _                            => new Color4(180, 180, 200, 255),
                };
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // ModeSplitPage
    // ═════════════════════════════════════════════════════════════════

    public partial class ModeSplitPage : CompositeDrawable
    {
        private static readonly Color4 torii_red = new Color4(204, 41, 41, 255);
        private static readonly Color4 muted_white = new Color4(255, 255, 255, 130);

        private static readonly (int Id, string Name, Color4 Colour)[] rulesets =
        {
            (0, "osu!",        new Color4(255, 130, 195, 255)),  // pink
            (1, "osu!taiko",   new Color4(225, 80, 105, 255)),   // red
            (2, "osu!catch",   new Color4(255, 158, 60, 255)),   // orange
            (3, "osu!mania",   new Color4(110, 220, 130, 255)),  // green
        };

        private OsuSpriteText totalText = null!;
        private FillFlowContainer barsFlow = null!;

        private int currentTotal;
        private IReadOnlyDictionary<string, int> currentBreakdown = new Dictionary<string, int>();

        public ModeSplitPage()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding { Horizontal = 18 },
                Spacing = new Vector2(0, 8),
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Text = "MODE SPLIT · CURRENTLY PLAYING",
                        Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                        Spacing = new Vector2(0.8f, 0),
                        Colour = torii_red,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            totalText = new OsuSpriteText
                            {
                                Anchor = Anchor.BottomLeft,
                                Origin = Anchor.BottomLeft,
                                Text = "0",
                                Font = OsuFont.GetFont(size: 36, weight: FontWeight.Bold),
                                Colour = Color4.White,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.BottomLeft,
                                Origin = Anchor.BottomLeft,
                                Text = "playing across all rulesets",
                                Font = OsuFont.GetFont(size: 10, weight: FontWeight.Regular),
                                Colour = muted_white,
                                Margin = new MarginPadding { Bottom = 6 },
                            },
                        },
                    },
                    barsFlow = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 6),
                    },
                },
            };

            foreach (var (id, name, colour) in rulesets)
                barsFlow.Add(new ModeBar(id, name, colour));
        }

        public void SetTotal(int value)
        {
            currentTotal = value;
            applyData();
        }

        public void SetBreakdown(IReadOnlyDictionary<string, int>? breakdown)
        {
            currentBreakdown = breakdown ?? (IReadOnlyDictionary<string, int>)new Dictionary<string, int>();
            applyData();
        }

        private void applyData()
        {
            if (totalText == null || barsFlow == null) return;

            totalText.Text = currentTotal.ToString();

            int max = 1;
            foreach (var (id, _, _) in rulesets)
            {
                int c = countFor(id);
                if (c > max) max = c;
            }

            foreach (var bar in barsFlow.Children.OfType<ModeBar>())
                bar.SetCount(countFor(bar.RulesetId), max);
        }

        private int countFor(int rulesetId)
            => currentBreakdown.TryGetValue(rulesetId.ToString(), out int v) ? v : 0;

        // ─── ModeBar ─────────────────────────────────────────────────
        private partial class ModeBar : CompositeDrawable
        {
            public int RulesetId { get; }

            private readonly Color4 colour;
            private OsuSpriteText nameText = null!;
            private OsuSpriteText countText = null!;
            private Box fill = null!;

            public ModeBar(int rulesetId, string name, Color4 colour)
            {
                RulesetId = rulesetId;
                this.colour = colour;
                _name = name;

                RelativeSizeAxes = Axes.X;
                Height = 26;
            }

            private readonly string _name;

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 2),
                        Children = new Drawable[]
                        {
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 12,
                                Children = new Drawable[]
                                {
                                    nameText = new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Text = _name,
                                        Font = OsuFont.GetFont(size: 10, weight: FontWeight.SemiBold),
                                        Colour = colour,
                                    },
                                    countText = new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreRight,
                                        Origin = Anchor.CentreRight,
                                        Text = "0",
                                        Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                                        Colour = Color4.White,
                                    },
                                },
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 8,
                                Masking = true,
                                CornerRadius = 4,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = colour.Opacity(0.18f),
                                    },
                                    fill = new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Width = 0f,
                                        Colour = colour,
                                    },
                                },
                            },
                        }
                    },
                };
            }

            public void SetCount(int count, int max)
            {
                if (countText == null || fill == null) return;

                countText.Text = count.ToString();

                float ratio = max > 0 ? (float)count / max : 0f;
                fill.ResizeWidthTo(ratio, 360, Easing.OutQuint);

                nameText.FadeTo(count == 0 ? 0.45f : 1f, 220, Easing.OutQuint);
                countText.FadeTo(count == 0 ? 0.45f : 1f, 220, Easing.OutQuint);
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // LazyCoverImage
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Async-loading beatmap-cover image with rounded corners and a
    /// dark placeholder. Loads the texture on a worker thread via
    /// <see cref="CompositeDrawable.LoadComponentAsync{TLoadable}"/>
    /// so the placeholder shows immediately and the loaded sprite
    /// fades in once its <see cref="Drawable.LoadAsync"/> completes
    /// (the texture's GPU upload is part of that load step).
    ///
    /// Replaces the previous DelayedLoadWrapper approach. That one
    /// gated on viewport detection, which didn't fire reliably for
    /// covers inside the masked carousel viewport on the popover —
    /// users saw solid coloured / white placeholder squares forever
    /// even on the visible page.
    /// </summary>
    internal partial class LazyCoverImage : CompositeDrawable
    {
        private readonly Color4 placeholderColour;
        private string? url;
        private Container coverHolder = null!;

        public string? Url
        {
            get => url;
            set
            {
                if (url == value) return;
                url = value;
                rebuild();
            }
        }

        public new float CornerRadius
        {
            get => base.CornerRadius;
            set => base.CornerRadius = value;
        }

        public LazyCoverImage(Color4 placeholderColour)
        {
            this.placeholderColour = placeholderColour;
            Masking = true;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = placeholderColour,
                },
                coverHolder = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                },
            };

            rebuild();
        }

        public void SetUrl(string? newUrl) => Url = newUrl;

        private void rebuild()
        {
            if (coverHolder == null) return;
            coverHolder.Clear(true);

            if (string.IsNullOrEmpty(url)) return;

            string capturedUrl = url;

            // LoadComponentAsync runs the sprite's LoadAsync on a worker
            // thread (the texture's GPU upload happens there too) and
            // calls our continuation on the update thread when the
            // sprite is fully loaded — at which point we add it and
            // fade it in over the placeholder.
            var sprite = new CoverSprite(capturedUrl);
            LoadComponentAsync(sprite, loaded =>
            {
                // url may have changed between scheduling and
                // completion. If it has, drop this stale load.
                if (capturedUrl != url || coverHolder == null)
                    return;

                coverHolder.Add(loaded);
                loaded.FadeInFromZero(280, Easing.OutQuint);
            });
        }

        /// <summary>
        /// Inner sprite that resolves the texture on the worker thread
        /// in its own <see cref="BackgroundDependencyLoaderAttribute"/>
        /// pass. Failure to resolve (404, blocked domain, etc.) leaves
        /// the inner child empty so the placeholder shows through.
        /// </summary>
        private partial class CoverSprite : CompositeDrawable
        {
            private readonly string url;

            public CoverSprite(string url)
            {
                this.url = url;
                RelativeSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load(LargeTextureStore textures)
            {
                Texture? tex = null;
                try { tex = textures.Get(url); } catch { tex = null; }

                if (tex == null) return;

                InternalChild = new Sprite
                {
                    Texture = tex,
                    RelativeSizeAxes = Axes.Both,
                    FillMode = FillMode.Fill,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };
            }
        }
    }
}
