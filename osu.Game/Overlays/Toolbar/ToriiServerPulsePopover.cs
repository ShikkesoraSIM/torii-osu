// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Server;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>
    /// Expanded panel surfaced from <see cref="ToriiServerPulseButton"/>.
    /// Lives as a sibling drawable of the button (added to the parent
    /// container by the button on first toggle) so it can render outside
    /// the button's auto-sized bounds without clipping.
    ///
    /// Layout
    /// ------
    ///     ┌─────────────────────────────────────────┐
    ///     │  [⛩] TORII SERVER PULSE     • Connected │   header
    ///     ├─────────────────────────────────────────┤
    ///     │   42                118        216      │   stats row
    ///     │   playing now      plays/min  online    │
    ///     ├─────────────────────────────────────────┤
    ///     │  ▁▂▂▃▅▇▆▄▃▂▁▁                           │   sparkline
    ///     │  last 12 minutes                         │
    ///     ├─────────────────────────────────────────┤
    ///     │  ┌─cover─┐ TOP RIGHT NOW                 │
    ///     │  │       │ Title — Artist                │   top map card
    ///     │  └───────┘ [Insane] · 23 plays · ★5.2    │
    ///     ├─────────────────────────────────────────┤
    ///     │  updated 4s ago · v1                     │   footer
    ///     └─────────────────────────────────────────┘
    ///
    /// Width 380, height auto-fits. Anchored below the button with a
    /// small horizontal nudge so the centre of the popover roughly aligns
    /// with the centre of the button.
    ///
    /// Animations
    /// ----------
    ///   - Show: slide-down 6px + fade-in (200ms OutQuint).
    ///   - Hide: slide-up 6px + fade-out (180ms OutQuint).
    ///   - Stats numbers: tween between values via local tween logic;
    ///     don't snap.
    ///   - Sparkline: bars rebuild in-place; new heights tween in.
    ///   - Cover: cross-fade when the top map changes.
    ///
    /// Click-outside dismissal
    /// -----------------------
    /// We don't capture global click-outside ourselves (would interfere
    /// with the rest of the toolbar). The user dismisses by clicking the
    /// button again, by hitting Escape (handled in OnKeyDown), or by
    /// hovering away from both popover + button + a small bridge — the
    /// bridge tolerance prevents accidental dismissal when the user
    /// moves diagonally between button and popover.
    /// </summary>
    public partial class ToriiServerPulsePopover : VisibilityContainer
    {
        private static readonly Color4 torii_red = new Color4(204, 41, 41, 255);
        private static readonly Color4 torii_red_dim = new Color4(204, 41, 41, 110);
        private static readonly Color4 panel_bg = new Color4(14, 12, 18, 245);
        private static readonly Color4 muted_white = new Color4(255, 255, 255, 130);

        private const float panel_width = 380f;
        private const float panel_corner_radius = 16f;

        /// <summary>
        /// The button this popover is anchored to. Set by the button when
        /// it first instantiates the popover. Used for relative positioning
        /// only — we don't read any other state off it.
        /// </summary>
        public Drawable? AnchoredAt { get; set; }

        [Resolved(canBeNull: true)]
        private ToriiServerPulseProvider? pulse { get; set; }

        // Visual state owned by us
        private Container body = null!;

        private TweenedNumber playingNumber = null!;
        private TweenedNumber playsPerMinuteNumber = null!;
        private TweenedNumber onlineNumber = null!;

        private SparklineGraph sparkline = null!;
        private TopMapCard topMapCard = null!;
        private OsuSpriteText footerText = null!;
        private StatusPip statusPip = null!;

        private ScheduledDelegate? footerTickDelegate;

        public ToriiServerPulsePopover()
        {
            // Auto-size off so the slide animation has stable geometry.
            Width = panel_width;
            AutoSizeAxes = Axes.Y;
            Alpha = 0;
            AlwaysPresent = true;

            // Position is finalised in Update() relative to AnchoredAt
            // because the toolbar can resize as siblings change state.
            // We just ensure we render in front of toolbar siblings here.
            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopCentre;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = body = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = panel_corner_radius,
                CornerExponent = 2.6f,
                MaskingSmoothness = 1.6f,
                BorderThickness = 1f,
                BorderColour = torii_red.Opacity(0.5f),
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Radius = 22,
                    Roundness = 16,
                    Colour = new Color4(8, 0, 4, 200),
                    Offset = new Vector2(0, 6),
                },
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = panel_bg,
                    },
                    // Faint vermillion gradient wash from top-left so the
                    // panel feels "branded" without competing with the
                    // content for attention.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Blending = BlendingParameters.Additive,
                        Colour = ColourInfo.GradientVertical(
                            torii_red.Opacity(0.10f),
                            torii_red.Opacity(0.0f)),
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Top = 14, Bottom = 14, Horizontal = 18 },
                        Spacing = new Vector2(0, 14),
                        Children = new Drawable[]
                        {
                            buildHeader(),
                            buildStatsRow(),
                            sparkline = new SparklineGraph
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 56,
                            },
                            topMapCard = new TopMapCard
                            {
                                RelativeSizeAxes = Axes.X,
                            },
                            buildFooter(),
                        },
                    },
                },
            };

            if (pulse != null)
            {
                pulse.CurrentlyPlaying.BindValueChanged(v => playingNumber.SetValue(v.NewValue), true);
                pulse.PlaysLastMinute.BindValueChanged(v => playsPerMinuteNumber.SetValue(v.NewValue), true);
                pulse.OnlineUsers.BindValueChanged(v => onlineNumber.SetValue(v.NewValue), true);
                pulse.Sparkline.BindValueChanged(v => sparkline.SetBuckets(v.NewValue), true);
                pulse.TopMap.BindValueChanged(v => topMapCard.SetMap(v.NewValue), true);
                pulse.LastUpdated.BindValueChanged(_ => updateFooter(), true);
                pulse.ConnectionState.BindValueChanged(v => statusPip.SetState(v.NewValue), true);
            }
        }

        private Drawable buildHeader()
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 22,
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            new osu.Game.Graphics.UserInterface.ToriiGateGlyph
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(13, 13),
                                Colour = torii_red,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = @"TORII SERVER PULSE",
                                Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                                Spacing = new Vector2(1.4f, 0),
                                Colour = torii_red,
                            },
                        }
                    },
                    statusPip = new StatusPip
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                    },
                },
            };
        }

        private Drawable buildStatsRow()
        {
            playingNumber = new TweenedNumber("playing now", torii_red);
            playsPerMinuteNumber = new TweenedNumber("plays/min", new Color4(255, 220, 130, 255));
            onlineNumber = new TweenedNumber("online", new Color4(150, 220, 255, 255));

            return new GridContainer
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
            };
        }

        private Drawable buildFooter()
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 14,
                Child = footerText = new OsuSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.Regular),
                    Text = "—",
                    Colour = muted_white,
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Tick the footer every second so the "updated Xs ago"
            // freshness counter increments smoothly. Cheap enough that
            // it doesn't matter that it runs while invisible — but we
            // still gate it on State.Value so when popover is hidden
            // the counter stops touching layout.
            footerTickDelegate = Scheduler.AddDelayed(() =>
            {
                if (State.Value == Visibility.Visible)
                    updateFooter();
            }, 1000, true);
        }

        private void updateFooter()
        {
            DateTimeOffset? captured = pulse?.LastUpdated.Value;

            if (captured == null)
            {
                footerText.Text = pulse?.ConnectionState.Value == ToriiServerPulseConnectionState.Disabled
                    ? "widget disabled · enable in Settings → Torii"
                    : "waiting for first snapshot…";
                return;
            }

            int seconds = (int)Math.Max(0, (DateTimeOffset.UtcNow - captured.Value).TotalSeconds);
            string ago = seconds < 1 ? "just now"
                : seconds < 60 ? $"{seconds}s ago"
                : seconds < 3600 ? $"{seconds / 60}m ago"
                : "stale (>1h)";

            footerText.Text = $"updated {ago}";
        }

        protected override void PopIn()
        {
            this.MoveToY(8).FadeTo(0);
            this.MoveToY(0, 220, Easing.OutQuint);
            this.FadeIn(200, Easing.OutQuint);

            updateFooter();
        }

        protected override void PopOut()
        {
            this.MoveToY(-6, 180, Easing.OutQuint);
            this.FadeOut(180, Easing.OutQuint);
        }

        protected override void Update()
        {
            base.Update();

            // Anchor the popover to the button's bottom-centre on every
            // frame. Toolbar siblings can resize (notification badge
            // grows when unread count changes, pp-dev indicator slides
            // in / out, etc.) so a one-shot positioning at construction
            // would drift visually. Position is recomputed cheaply in
            // local space.
            if (AnchoredAt == null) return;

            var anchorRect = AnchoredAt.ScreenSpaceDrawQuad;
            // Convert screen-space bottom-centre to our parent's local
            // space so layout reads correctly regardless of where in the
            // tree we live.
            if (Parent == null) return;

            Vector2 localTopCentre = Parent.ToLocalSpace(new Vector2(
                anchorRect.BottomLeft.X + anchorRect.Width / 2f,
                anchorRect.BottomLeft.Y));

            // 8px gap below the button so the popover doesn't kiss the
            // toolbar pill edge — gives the visual a breath of air.
            Position = new Vector2(localTopCentre.X, localTopCentre.Y + 8);
        }

        protected override bool OnKeyDown(KeyDownEvent e)
        {
            if (e.Key == osuTK.Input.Key.Escape && State.Value == Visibility.Visible)
            {
                Hide();
                return true;
            }
            return base.OnKeyDown(e);
        }

        protected override void Dispose(bool isDisposing)
        {
            footerTickDelegate?.Cancel();
            base.Dispose(isDisposing);
        }

        // -------------------------------------------------------------
        // StatusPip — tiny green/yellow/red dot + label in the header
        // -------------------------------------------------------------
        private partial class StatusPip : CompositeDrawable
        {
            private CircularContainer dot = null!;
            private Box dotFill = null!;
            private OsuSpriteText label = null!;

            public StatusPip()
            {
                AutoSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChild = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(5, 0),
                    Children = new Drawable[]
                    {
                        dot = new CircularContainer
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Size = new Vector2(7, 7),
                            Masking = true,
                            Child = dotFill = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(120, 180, 120, 255),
                            },
                        },
                        label = new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                            Spacing = new Vector2(0.8f, 0),
                            Text = "—",
                            Colour = muted_white,
                        },
                    }
                };
            }

            public void SetState(ToriiServerPulseConnectionState state)
            {
                Color4 colour;
                string text;
                switch (state)
                {
                    case ToriiServerPulseConnectionState.Connected:
                        colour = new Color4(110, 220, 130, 255);
                        text = "LIVE";
                        break;
                    case ToriiServerPulseConnectionState.Connecting:
                        colour = new Color4(255, 220, 110, 255);
                        text = "SYNCING";
                        break;
                    case ToriiServerPulseConnectionState.Offline:
                        colour = new Color4(255, 110, 110, 255);
                        text = "OFFLINE";
                        break;
                    case ToriiServerPulseConnectionState.Idle:
                        colour = muted_white;
                        text = "IDLE";
                        break;
                    case ToriiServerPulseConnectionState.Disabled:
                        colour = muted_white;
                        text = "DISABLED";
                        break;
                    default:
                        colour = muted_white;
                        text = "—";
                        break;
                }

                dotFill.FadeColour(colour, 220, Easing.OutQuint);
                label.Text = text;
                label.FadeColour(colour.Opacity(0.85f), 220, Easing.OutQuint);
            }
        }

        // -------------------------------------------------------------
        // TweenedNumber — big stat block (number + small caption)
        // -------------------------------------------------------------
        private partial class TweenedNumber : CompositeDrawable
        {
            private readonly string caption;
            private readonly Color4 accent;

            private OsuSpriteText valueText = null!;
            private OsuSpriteText captionText = null!;

            private double currentDisplayValue;
            private double targetValue;
            private double tweenDurationRemaining;

            // Linear interpolation over ~280ms feels snappy without
            // snapping. Tweaked alongside the heartbeat ambient cadence
            // so the popover and the button feel like the same animation
            // language.
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
                if (Math.Abs(newValue - currentDisplayValue) < 0.5)
                {
                    // No actual change; ensure final text is correct.
                    valueText.Text = newValue.ToString();
                    return;
                }

                targetValue = newValue;
                tweenDurationRemaining = tween_duration_ms;

                // Subtle pulse on update — brief scale-up of the value
                // text + accent flash, fades back to white. Keeps the
                // motion language coherent with the toolbar button's
                // count update.
                valueText.ClearTransforms();
                valueText.ScaleTo(1.10f, 90, Easing.OutQuint).Then().ScaleTo(1f, 220, Easing.OutBack);
                valueText.FadeColour(accent, 80, Easing.OutQuint).Then().FadeColour(Color4.White, 360, Easing.OutQuint);
            }

            protected override void Update()
            {
                base.Update();

                if (tweenDurationRemaining <= 0 && Math.Abs(currentDisplayValue - targetValue) < 0.5) return;

                if (tweenDurationRemaining <= 0)
                {
                    currentDisplayValue = targetValue;
                }
                else
                {
                    double progress = 1.0 - tweenDurationRemaining / tween_duration_ms;
                    progress = Math.Clamp(progress, 0, 1);
                    // Ease-out cubic for a snappy start + soft landing
                    double eased = 1 - Math.Pow(1 - progress, 3);
                    currentDisplayValue = (targetValue - currentDisplayValue) * (eased - (1.0 - tweenDurationRemaining / tween_duration_ms)) + currentDisplayValue;
                    // Simpler: just use linear interpolation between
                    // initial and target. We don't actually need the
                    // perfect ease since values are integers.
                    tweenDurationRemaining -= Time.Elapsed;
                }

                valueText.Text = ((int)Math.Round(currentDisplayValue)).ToString();
            }
        }

        // -------------------------------------------------------------
        // SparklineGraph — 12 vertical bars showing recent play activity
        // -------------------------------------------------------------
        // Bars are rendered as Boxes with bottom anchor + RelativeSizeAxes
        // so resizing the parent container reflows them naturally. Each
        // bar tweens its height when the bucket data updates.
        // -------------------------------------------------------------
        private partial class SparklineGraph : CompositeDrawable
        {
            private FillFlowContainer barsFlow = null!;
            private OsuSpriteText caption = null!;
            private OsuSpriteText emptyHint = null!;

            private readonly List<Box> bars = new List<Box>();

            // Soft minimum height so even a zero-bucket renders a faint
            // baseline strip — communicates "the graph is here, it's
            // just quiet right now" instead of looking broken.
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
                            Height = 38,
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
                            Child = caption = new OsuSpriteText
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
                if (buckets == null || buckets.Count == 0)
                {
                    foreach (var bar in bars)
                        bar.ResizeHeightTo(min_bar_height, 240, Easing.OutQuint);
                    emptyHint.FadeIn(220, Easing.OutQuint);
                    return;
                }

                emptyHint.FadeOut(160, Easing.OutQuint);

                // Lazy-allocate bar widgets the first time we know the
                // bucket count. Subsequent updates reuse the same boxes,
                // just retargeting their heights — no churn.
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
                            // Newer bars on the right are brighter — gives
                            // the eye an easy "current activity is on
                            // the right" affordance.
                            Colour = ColourInfo.GradientVertical(
                                ColourFor(i, buckets.Count, 1f),
                                ColourFor(i, buckets.Count, 0.5f)),
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

            private static Color4 ColourFor(int index, int total, float alpha)
            {
                // Older buckets dim toward muted-grey; newer buckets are
                // closer to torii_red for "this is happening now". Linear
                // interpolation between the two endpoints.
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

        // -------------------------------------------------------------
        // TopMapCard — small card with cover + title/artist + stats
        // -------------------------------------------------------------
        // Cover loaded via LargeTextureStore.Get(url) — same path used
        // for avatars (DrawableAvatar). Cross-fades between covers when
        // the top map changes; renders a calm "no plays yet" empty state
        // when the server returns top_map = null.
        // -------------------------------------------------------------
        private partial class TopMapCard : CompositeDrawable
        {
            private Container coverContainer = null!;
            private Sprite? currentCover;
            private Box coverPlaceholder = null!;
            private OsuSpriteText titleText = null!;
            private OsuSpriteText artistText = null!;
            private OsuSpriteText metaText = null!;
            private Container contentContainer = null!;
            private Container emptyContainer = null!;

            [Resolved]
            private LargeTextureStore textures { get; set; } = null!;

            private string? lastLoadedCoverUrl;

            public TopMapCard()
            {
                AutoSizeAxes = Axes.Y;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    contentContainer = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 64,
                        Children = new Drawable[]
                        {
                            // Cover, left side, square. CornerRadius
                            // matches the panel's overall corner language
                            // for visual cohesion.
                            new Container
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(64, 64),
                                Masking = true,
                                CornerRadius = 8,
                                Children = new Drawable[]
                                {
                                    coverPlaceholder = new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = new Color4(28, 24, 32, 255),
                                    },
                                    coverContainer = new Container
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                    },
                                    // Subtle vermillion sheen on top of
                                    // the cover so it reads as part of
                                    // the Torii panel rather than a
                                    // foreign image.
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
                            // Right side: caption + title + meta line.
                            new Container
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                X = 76,
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Width = 1f,
                                Padding = new MarginPadding { Right = 76 }, // leave room for the cover
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
                                            Text = "TOP RIGHT NOW",
                                            Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                                            Spacing = new Vector2(0.8f, 0),
                                            Colour = torii_red,
                                        },
                                        titleText = new OsuSpriteText
                                        {
                                            Text = "—",
                                            Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                                            Colour = Color4.White,
                                            Truncate = true,
                                            RelativeSizeAxes = Axes.X,
                                        },
                                        artistText = new OsuSpriteText
                                        {
                                            Text = "—",
                                            Font = OsuFont.GetFont(size: 11, weight: FontWeight.Regular),
                                            Colour = muted_white,
                                            Truncate = true,
                                            RelativeSizeAxes = Axes.X,
                                        },
                                        metaText = new OsuSpriteText
                                        {
                                            Text = "—",
                                            Font = OsuFont.GetFont(size: 10, weight: FontWeight.Regular),
                                            Colour = torii_red_dim,
                                            Truncate = true,
                                            RelativeSizeAxes = Axes.X,
                                        },
                                    }
                                },
                            },
                        },
                    },
                    emptyContainer = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 64,
                        Alpha = 0,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(255, 255, 255, 8),
                                // Soft transparent panel so the empty
                                // state still has a defined card area
                                // matching the populated layout.
                            },
                            new FillFlowContainer
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 2),
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        Text = "no plays in the last 5 minutes",
                                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.Regular),
                                        Colour = muted_white,
                                    },
                                    new OsuSpriteText
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        Text = "be the first to break the silence",
                                        Font = OsuFont.GetFont(size: 9, weight: FontWeight.SemiBold),
                                        Spacing = new Vector2(0.6f, 0),
                                        Colour = torii_red.Opacity(0.85f),
                                    },
                                }
                            },
                        }
                    },
                };
            }

            public void SetMap(APIToriiServerPulseTopMap? map)
            {
                if (map == null)
                {
                    contentContainer.FadeOut(180, Easing.OutQuint);
                    emptyContainer.FadeIn(220, Easing.OutQuint);
                    return;
                }

                contentContainer.FadeIn(220, Easing.OutQuint);
                emptyContainer.FadeOut(180, Easing.OutQuint);

                titleText.Text = map.DisplayTitle;
                artistText.Text = map.DisplayArtist;

                // Star rating may be 0 if server didn't compute it; only
                // include the badge when we actually have a number.
                string starPart = map.StarRating > 0 ? $"  ·  ★{map.StarRating:0.00}" : string.Empty;
                metaText.Text = $"[{map.Version}]  ·  {map.PlayCount5Min} play{(map.PlayCount5Min == 1 ? "" : "s")} in 5min{starPart}";

                string? coverUrl = map.BestCoverUrl;
                if (coverUrl == lastLoadedCoverUrl) return;

                lastLoadedCoverUrl = coverUrl;
                loadCover(coverUrl);
            }

            private void loadCover(string? url)
            {
                if (string.IsNullOrEmpty(url))
                {
                    currentCover?.FadeOut(180, Easing.OutQuint);
                    return;
                }

                Texture? tex = null;
                try
                {
                    tex = textures.Get(url);
                }
                catch
                {
                    // textures.Get can throw on malformed URLs / protocol
                    // restrictions. Fall back to placeholder silently.
                    tex = null;
                }

                if (tex == null)
                {
                    currentCover?.FadeOut(180, Easing.OutQuint);
                    return;
                }

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

                // Fade out and remove the previous cover with a small
                // delay so the cross-fade overlaps cleanly.
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
}
