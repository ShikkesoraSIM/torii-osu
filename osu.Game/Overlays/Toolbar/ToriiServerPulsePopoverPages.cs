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
    // Each page exposes setter methods (SetPlaying, SetMaps, SetPlays,
    // SetBreakdown) that the popover wires up in LoadComplete (after
    // children have loaded — see the popover for the load-order
    // rationale). All setters are defensive: if the page hasn't loaded
    // yet they no-op, so the bind-with-immediate-fire pattern never
    // dereferences null internals.
    //
    // Brand palette is duplicated here from the popover so each page is
    // independently styleable / testable; if a third Torii surface ever
    // needs the same shades they'll move to a shared constants class.

    // ═════════════════════════════════════════════════════════════════
    // OverviewPage
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Page 0 of the carousel — the original "at a glance" view: three
    /// big stat blocks (playing now / plays per minute / online) and a
    /// 12-minute sparkline below. The top-map preview is left to its
    /// own dedicated page (HotMapsPage) since this page is the
    /// "headline numbers, no scroll required" variant.
    /// </summary>
    public partial class OverviewPage : CompositeDrawable
    {
        private static readonly Color4 torii_red = new Color4(204, 41, 41, 255);
        private static readonly Color4 muted_white = new Color4(255, 255, 255, 130);

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
        // Big stat block (number + small caption). Smooth tween between
        // values so the eye registers the change without a snap; brief
        // accent-colour flash on update for an extra "something just
        // happened" cue.
        // ─────────────────────────────────────────────────────────────
        private partial class TweenedNumber : CompositeDrawable
        {
            private readonly string caption;
            private readonly Color4 accent;

            private OsuSpriteText valueText = null!;
            private OsuSpriteText captionText = null!;

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
                        captionText = new OsuSpriteText
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
                // Defensive load-order guard. If [BackgroundDependencyLoader]
                // hasn't run yet (caller bound a value-changed handler with
                // immediateFire=true before our load completed), valueText
                // is still null. Stash the target so it shows correctly
                // when load completes; skip transforms.
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

                // Subtle pulse + accent flash on update.
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
                    // Ease-out cubic — snappy start, soft landing.
                    double eased = 1 - Math.Pow(1 - t, 3);
                    currentDisplayValue = tweenStartValue + (targetValue - tweenStartValue) * eased;
                }

                valueText.Text = ((int)Math.Round(currentDisplayValue)).ToString();
            }
        }

        // ─── SparklineGraph ──────────────────────────────────────────
        // 12 vertical bars, gradient older→newer, height proportional to
        // play-count in that minute bucket. Empty hint when all zeros.
        // ─────────────────────────────────────────────────────────────
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
        // Compact preview of the top map at the bottom of the overview
        // page. Smaller than the dedicated HotMapsPage rows; just a
        // teaser with cover + title and a "swipe → for more" affordance.
        // ─────────────────────────────────────────────────────────────
        private partial class OverviewTopMapStrip : CompositeDrawable
        {
            private Container coverContainer = null!;
            private Sprite? currentCover;
            private TruncatingSpriteText titleText = null!;
            private TruncatingSpriteText artistText = null!;
            private OsuSpriteText metaText = null!;
            private Container contentContainer = null!;
            private OsuSpriteText emptyText = null!;

            [Resolved(canBeNull: true)]
            private LargeTextureStore? textures { get; set; }

            private string? lastLoadedCoverUrl;

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    contentContainer = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            new Container
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(64, 64),
                                Masking = true,
                                CornerRadius = 8,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = new Color4(28, 24, 32, 255),
                                    },
                                    coverContainer = new Container { RelativeSizeAxes = Axes.Both },
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = ColourInfo.GradientVertical(
                                            torii_red.Opacity(0.0f),
                                            torii_red.Opacity(0.18f)),
                                        Blending = BlendingParameters.Additive,
                                    },
                                },
                            },
                            new Container
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                X = 76,
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Width = 1f,
                                Padding = new MarginPadding { Right = 76 },
                                Child = new FillFlowContainer
                                {
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
                            },
                        },
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

                string? coverUrl = map.BestCoverUrl;
                if (coverUrl == lastLoadedCoverUrl) return;
                lastLoadedCoverUrl = coverUrl;
                loadCover(coverUrl);
            }

            private void loadCover(string? url)
            {
                if (string.IsNullOrEmpty(url) || textures == null)
                {
                    currentCover?.FadeOut(180, Easing.OutQuint);
                    return;
                }

                Texture? tex = null;
                try { tex = textures.Get(url); } catch { tex = null; }
                if (tex == null) return;

                var newCover = new Sprite
                {
                    Texture = tex,
                    RelativeSizeAxes = Axes.Both,
                    FillMode = FillMode.Fill,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Alpha = 0,
                };

                coverContainer.Add(newCover);
                newCover.FadeIn(280, Easing.OutQuint);

                var oldCover = currentCover;
                currentCover = newCover;
                if (oldCover != null)
                {
                    oldCover.FadeOut(280, Easing.OutQuint);
                    oldCover.Expire();
                }
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // HotMapsPage
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Page 1 of the carousel — top 5 most-played beatmaps right now,
    /// vertical list with cover thumbnails. Slot ranks (#1, #2, ...) are
    /// rendered as small medals on each row to give the leaderboard
    /// feel.
    /// </summary>
    public partial class HotMapsPage : CompositeDrawable
    {
        private static readonly Color4 torii_red = new Color4(204, 41, 41, 255);
        private static readonly Color4 muted_white = new Color4(255, 255, 255, 130);

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

            // Rebuild rows. With only 5 rows max, churn is cheap; we
            // don't bother with reuse semantics because the maps shift
            // around between polls anyway.
            rowsFlow.Clear();
            for (int i = 0; i < maps.Count; i++)
                rowsFlow.Add(new HotMapRow(i + 1, maps[i]));
        }

        // ─── HotMapRow ───────────────────────────────────────────────
        private partial class HotMapRow : CompositeDrawable
        {
            private readonly int rank;
            private readonly APIToriiServerPulseTopMap map;

            [Resolved(canBeNull: true)]
            private LargeTextureStore? textures { get; set; }

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
                Container coverContainer;

                InternalChildren = new Drawable[]
                {
                    // Hover-friendly background panel — subtle so the row
                    // reads as a list item, not a button. (Click handling
                    // intentionally NOT implemented here yet; can wire up
                    // ShowBeatmap via OsuGame in a follow-up.)
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White.Opacity(0.025f),
                    },
                    new RankMedal(rank)
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 4,
                    },
                    coverContainer = new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 30,
                        Size = new Vector2(36, 36),
                        Masking = true,
                        CornerRadius = 6,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(28, 24, 32, 255),
                            },
                        },
                    },
                    new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 76,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Width = 1f,
                        Padding = new MarginPadding { Right = 88 },  // leave room for plays badge
                        Child = new FillFlowContainer
                        {
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
                    },
                    new PlaysBadge(map.PlayCount5Min)
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        X = -4,
                    },
                };

                // Async-load the cover into the container to avoid
                // blocking the page swipe-in animation.
                if (textures != null)
                {
                    string? url = map.BestCoverUrl;
                    if (!string.IsNullOrEmpty(url))
                    {
                        Texture? tex = null;
                        try { tex = textures.Get(url); } catch { tex = null; }
                        if (tex != null)
                        {
                            coverContainer.Add(new Sprite
                            {
                                Texture = tex,
                                RelativeSizeAxes = Axes.Both,
                                FillMode = FillMode.Fill,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                            });
                        }
                    }
                }
            }
        }

        // ─── RankMedal ───────────────────────────────────────────────
        // Small chip showing #1 / #2 / #3 (gold/silver/bronze tinted)
        // and #4 / #5 in muted grey. Quick visual rank discrimination
        // without leaning on the row order alone.
        // ─────────────────────────────────────────────────────────────
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
        // Right-side counter showing "Nx plays" with a soft pink tint.
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

    /// <summary>
    /// Page 2 of the carousel — recent in-flight plays, the closest
    /// thing to "who's online and what they're playing right now".
    /// Most engaging page for community feel.
    /// </summary>
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
        private partial class LivePlayRow : CompositeDrawable
        {
            private readonly APIToriiServerPulseRecentPlay play;

            [Resolved(canBeNull: true)]
            private LargeTextureStore? textures { get; set; }

            public LivePlayRow(APIToriiServerPulseRecentPlay play)
            {
                this.play = play;
                RelativeSizeAxes = Axes.X;
                Height = 26;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Container avatarContainer;

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White.Opacity(0.025f),
                    },
                    avatarContainer = new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 4,
                        Size = new Vector2(22, 22),
                        Masking = true,
                        CornerRadius = 11,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(40, 36, 48, 255),
                            },
                        },
                    },
                    new RulesetGlyph(play.RulesetId)
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 30,
                    },
                    new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 50,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Width = 1f,
                        Padding = new MarginPadding { Right = 70 },
                        Child = new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Y,
                            RelativeSizeAxes = Axes.X,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(6, 0),
                            Children = new Drawable[]
                            {
                                new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Text = string.IsNullOrEmpty(play.Username) ? "—" : play.Username,
                                    Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                                    Colour = Color4.White,
                                },
                                new TruncatingSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Text = string.IsNullOrEmpty(play.DisplayTitle) ? "" : $"· {play.DisplayTitle}",
                                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.Regular),
                                    Colour = new Color4(255, 255, 255, 145),
                                    RelativeSizeAxes = Axes.X,
                                    Width = 1f,
                                },
                            }
                        },
                    },
                    new TimeAgoChip(play.StartedSecondsAgo)
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        X = -4,
                    },
                };

                if (textures != null && !string.IsNullOrEmpty(play.AvatarUrl))
                {
                    Texture? tex = null;
                    try { tex = textures.Get(play.AvatarUrl); } catch { tex = null; }
                    if (tex != null)
                    {
                        avatarContainer.Add(new Sprite
                        {
                            Texture = tex,
                            RelativeSizeAxes = Axes.Both,
                            FillMode = FillMode.Fill,
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                        });
                    }
                }
            }
        }

        // ─── RulesetGlyph ────────────────────────────────────────────
        // Small letter chip indicating the ruleset of the play. Colour
        // matches the standard osu! ruleset accents (pink osu, red taiko,
        // orange catch, green mania).
        // ─────────────────────────────────────────────────────────────
        private partial class RulesetGlyph : CompositeDrawable
        {
            public RulesetGlyph(int rulesetId)
            {
                Size = new Vector2(16, 16);
                Masking = true;
                CornerRadius = 8;

                Color4 fill;
                string label;
                switch (rulesetId)
                {
                    case 1:
                        fill = new Color4(225, 80, 105, 255);
                        label = "T";
                        break;
                    case 2:
                        fill = new Color4(255, 158, 60, 255);
                        label = "C";
                        break;
                    case 3:
                        fill = new Color4(110, 220, 130, 255);
                        label = "M";
                        break;
                    default:
                        fill = new Color4(255, 130, 195, 255);
                        label = "O";
                        break;
                }

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = fill.Opacity(0.85f),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = label,
                        Font = OsuFont.GetFont(size: 9, weight: FontWeight.Bold),
                        Colour = Color4.White,
                    },
                };
            }
        }

        // ─── TimeAgoChip ─────────────────────────────────────────────
        // "Xs ago" / "Xm ago" pill on the right. Vermillion-tinted so it
        // visually groups with the Hot Maps page's plays badge — same
        // family of "this is a number that updates" chip.
        // ─────────────────────────────────────────────────────────────
        private partial class TimeAgoChip : CompositeDrawable
        {
            public TimeAgoChip(int secondsAgo)
            {
                AutoSizeAxes = Axes.Both;

                string text = secondsAgo < 5 ? "just now"
                    : secondsAgo < 60 ? $"{secondsAgo}s"
                    : secondsAgo < 3600 ? $"{secondsAgo / 60}m"
                    : "1h+";

                InternalChild = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 8,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = torii_red.Opacity(0.18f),
                        },
                        new OsuSpriteText
                        {
                            Text = text,
                            Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                            Colour = torii_red,
                            Margin = new MarginPadding { Horizontal = 7, Vertical = 2 },
                        }
                    }
                };
            }
        }
    }

    // ═════════════════════════════════════════════════════════════════
    // ModeSplitPage
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Page 3 of the carousel — per-ruleset breakdown of who's playing
    /// what. Big total at the top, four labelled horizontal bars below
    /// (osu / taiko / catch / mania), each showing its share of the
    /// total currently-playing count.
    /// </summary>
    public partial class ModeSplitPage : CompositeDrawable
    {
        private static readonly Color4 torii_red = new Color4(204, 41, 41, 255);
        private static readonly Color4 muted_white = new Color4(255, 255, 255, 130);

        // Ruleset visual identity — colour + label + ID. Order matters
        // because we render the bars in this order (osu first; the
        // "default" mode at the top reads as the most prominent slot).
        private static readonly (int Id, string Name, Color4 Colour)[] rulesets =
        {
            (0, "osu!",        new Color4(255, 130, 195, 255)),  // pink
            (1, "osu!taiko",   new Color4(225, 80, 105, 255)),   // red
            (2, "osu!catch",   new Color4(255, 158, 60, 255)),   // orange
            (3, "osu!mania",   new Color4(110, 220, 130, 255)),  // green
        };

        private OsuSpriteText totalText = null!;
        private OsuSpriteText headerText = null!;
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
                    headerText = new OsuSpriteText
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

            // Pre-populate one bar per ruleset; we update fill values
            // in-place rather than rebuilding so transitions tween
            // smoothly.
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
        // One labelled horizontal bar — ruleset name + count + filled
        // proportion of the maximum across all rulesets.
        // ─────────────────────────────────────────────────────────────
        private partial class ModeBar : CompositeDrawable
        {
            public int RulesetId { get; }

            private readonly Color4 colour;
            private OsuSpriteText nameText = null!;
            private OsuSpriteText countText = null!;
            private Box fill = null!;
            private Box track = null!;

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
                                    track = new Box
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

                // Subtle dim when 0 — communicates "this ruleset is
                // currently empty" without being a punishing red badge.
                nameText.FadeTo(count == 0 ? 0.45f : 1f, 220, Easing.OutQuint);
                countText.FadeTo(count == 0 ? 0.45f : 1f, 220, Easing.OutQuint);
            }
        }
    }
}
