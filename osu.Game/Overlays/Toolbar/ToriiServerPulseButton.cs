// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.Server;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>
    /// Compact toolbar entry-point for the live server-pulse widget. Sits
    /// in the right cluster of <see cref="ToriiAlphaToolbar"/> next to the
    /// notification bell + user chip + clock, mirrors their visual height
    /// + corner radius + glass-y background so it reads as a sibling
    /// rather than a foreign element.
    ///
    /// Visual layout (collapsed)
    /// -------------------------
    /// One pill, ~70-90px wide depending on the count:
    ///   [ ❤ pulsing dot ]  [ 12 ]
    ///         vermillion   semibold white digits
    ///
    /// The dot has a steady idle heartbeat (slow scale 1.0 → 1.12 → 1.0,
    /// ~60 BPM) so the eye perceives the widget as ALIVE even on a quiet
    /// server. When <see cref="ToriiServerPulseProvider.PlayDetected"/>
    /// fires, it does an extra one-shot flash with a brighter halo —
    /// you literally see the play happen.
    ///
    /// Behaviour
    /// ---------
    ///   - Hover: gentle scale-up + glow brighten, like the other toolbar
    ///     chips. Asks the provider to keep its idle cadence (60s) since
    ///     hovering doesn't promise the user will click.
    ///   - Click: opens the popover. Provider switches to the 10s
    ///     cadence and is asked to refresh immediately so the popover
    ///     opens with fresh numbers.
    ///   - Tooltip on hover: short label so the function reads even
    ///     before the user opens the popover.
    ///   - Connection state Offline / Disabled: button stays visible
    ///     but desaturates + dot stops pulsing.
    ///
    /// What this file does NOT contain
    /// -------------------------------
    /// The popover itself lives in
    /// <see cref="ToriiServerPulsePopover"/>. We just own a reference and
    /// drive its show/hide. Keeps each file at a manageable size and
    /// makes test scenes possible (you can instantiate the button alone).
    /// </summary>
    public partial class ToriiServerPulseButton : OsuClickableContainer, IHasTooltip
    {
        // Vermillion — same shade ToriiClientBadge + the cursor preview
        // pill use, kept consistent across the brand. Alpha values vary
        // by purpose (background vs accent vs glow) but the base RGB is
        // pinned to one constant.
        private static readonly Color4 torii_red = new Color4(204, 41, 41, 255);

        // Toolbar chip dimensions match AlphaActionButton / AlphaUserChip
        // so we slot in cleanly. Hard-coded rather than parameterised
        // because changing them would visually misalign the rest of the
        // toolbar.
        private const float pill_height = 32f;
        private const float pill_corner_radius = 12f;

        public LocalisableString TooltipText => "Torii server pulse — click for live activity";

        [Resolved(canBeNull: true)]
        private ToriiServerPulseProvider? pulse { get; set; }

        [Resolved(canBeNull: true)]
        private OsuConfigManager? config { get; set; }

        private readonly Bindable<bool> enabled = new BindableBool(true);

        private Container pillContainer = null!;
        private Box backgroundBox = null!;
        private Box hoverGlow = null!;
        private HeartbeatDot heartbeat = null!;
        private OsuSpriteText countText = null!;
        private Container desaturationVeil = null!;

        private ToriiServerPulsePopover? popover;

        private ScheduledDelegate? heartbeatLoopDelegate;

        public ToriiServerPulseButton()
        {
            AutoSizeAxes = Axes.X;
            Height = pill_height;
            Action = togglePopover;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = pillContainer = new Container
            {
                AutoSizeAxes = Axes.X,
                RelativeSizeAxes = Axes.Y,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                CornerRadius = pill_corner_radius,
                CornerExponent = 2.4f,
                MaskingSmoothness = 1.4f,
                BorderThickness = 1f,
                BorderColour = torii_red.Opacity(0.45f),
                EdgeEffect = new EdgeEffectParameters
                {
                    // Subtle tinted shadow under the pill so it lifts off
                    // the toolbar background without screaming. Roundness
                    // kept high to feather edges (no hard outline).
                    Type = EdgeEffectType.Shadow,
                    Radius = 8,
                    Roundness = 6,
                    Colour = torii_red.Opacity(0.18f),
                    Offset = new Vector2(0, 1),
                },
                Children = new Drawable[]
                {
                    backgroundBox = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        // Dark base with a vermillion bias — glass-y but
                        // not pink-screaming. Tints brighter on hover.
                        Colour = new Color4(28, 16, 18, 230),
                    },
                    // Additive vermillion wash on top of the dark base
                    // for the brand-tinted look without losing legibility.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = torii_red.Opacity(0.10f),
                        Blending = BlendingParameters.Additive,
                    },
                    hoverGlow = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = torii_red.Opacity(0.18f),
                        Blending = BlendingParameters.Additive,
                        Alpha = 0,
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Padding = new MarginPadding { Horizontal = 12 },
                        Children = new Drawable[]
                        {
                            heartbeat = new HeartbeatDot
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(11, 11),
                            },
                            countText = new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                                Text = @"--",
                                Colour = Color4.White,
                            },
                        }
                    },
                    desaturationVeil = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(20, 20, 20, 180),
                        },
                    },
                },
            };

            if (pulse != null)
            {
                pulse.CurrentlyPlaying.BindValueChanged(updateCount, true);
                pulse.ConnectionState.BindValueChanged(updateConnectionState, true);
                pulse.PlayDetected += onPlayDetected;
            }

            if (config != null)
            {
                config.BindWith(OsuSetting.ToriiServerPulseEnabled, enabled);
                enabled.BindValueChanged(e => updateEnabledVisibility(e.NewValue), true);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Pre-load the popover off the UI thread. Constructing it inline
            // on first click was freezing the game for ~50-150ms while the
            // BackgroundDependencyLoader walked through ~2.8K LOC of nested
            // drawables across 4 carousel pages — perceived as a stutter,
            // exacerbated when the network was slow because the same click
            // also kicks off a refresh request. Loading async lets the
            // first click stay instant.
            //
            // Edge cases handled by togglePopover:
            //   - Click before async-load finishes: ignored (rare; the
            //     toolbar lives long enough that this only happens if the
            //     user mashes the button within ~50ms of game launch).
            //   - Async-load failure: caught + logged + retried on the next
            //     click via the existing try/catch fallback that nulls
            //     `popover` so a fresh instance is constructed.
            popover = new ToriiServerPulsePopover
            {
                BypassAutoSizeAxes = Axes.Both,
            };

            LoadComponentAsync(popover, p =>
            {
                AddInternal(p);
                p.AnchoredAt = this;
                Logger.Log("[ToriiServerPulse] popover preloaded async", LoggingTarget.Runtime, LogLevel.Verbose);
            });
        }

        private void updateEnabledVisibility(bool isEnabled)
        {
            // Smooth collapse animation so the toolbar reflows cleanly
            // when the user toggles the setting at runtime — same shape
            // as the pp-dev indicator.
            if (isEnabled)
            {
                this.FadeIn(180, Easing.OutQuint);
            }
            else
            {
                this.FadeOut(140, Easing.OutQuint);
                popover?.Hide();
            }
        }

        private void updateCount(ValueChangedEvent<int> e)
        {
            // Provider drives this from its polling task, which can run off
            // the update thread. Marshal back before touching transforms.
            Schedule(() =>
            {
                countText.Text = e.NewValue.ToString();
                countText.ScaleTo(1.18f, 80, Easing.OutQuint)
                         .Then()
                         .ScaleTo(1f, 220, Easing.OutBack);
            });
        }

        private void updateConnectionState(ValueChangedEvent<ToriiServerPulseConnectionState> e)
        {
            // Fired from APIAccess.handleFailure on the API thread when the
            // user disconnects, so transforms have to be scheduled.
            Schedule(() =>
            {
                switch (e.NewValue)
                {
                    case ToriiServerPulseConnectionState.Connected:
                    case ToriiServerPulseConnectionState.Connecting:
                        desaturationVeil.FadeOut(220, Easing.OutQuint);
                        heartbeat.SetActive(true);
                        break;

                    case ToriiServerPulseConnectionState.Offline:
                    case ToriiServerPulseConnectionState.Idle:
                    case ToriiServerPulseConnectionState.Disabled:
                        desaturationVeil.FadeTo(0.55f, 220, Easing.OutQuint);
                        heartbeat.SetActive(false);
                        break;
                }
            });
        }

        private void onPlayDetected(int delta)
        {
            // Schedule onto the update thread — provider may invoke from
            // its API callback which can land on a different scheduler.
            Schedule(() =>
            {
                heartbeat.Flash(delta);

                // Brighten the pill background briefly so even a peripheral
                // glance catches the activity.
                hoverGlow.ClearTransforms();
                hoverGlow.FadeTo(0.45f, 80, Easing.OutQuint)
                         .Then()
                         .FadeTo(0f, 380, Easing.OutQuint);
            });
        }

        protected override bool OnHover(HoverEvent e)
        {
            hoverGlow.FadeTo(0.30f, 200, Easing.OutQuint);
            this.ScaleTo(1.04f, 200, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hoverGlow.FadeTo(0f, 280, Easing.OutQuint);
            this.ScaleTo(1f, 280, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        private void togglePopover()
        {
            // Wrap in try/catch + verbose logging so a regression in popover
            // wiring takes down the CLICK rather than the entire game. The
            // pulse widget is best-effort decoration; a buggy build hitting
            // an unhandled exception here would otherwise crash the
            // application, which is a much worse user experience than
            // "click did nothing".
            try
            {
                Logger.Log("[ToriiServerPulse] togglePopover start", LoggingTarget.Runtime, LogLevel.Verbose);

                // Popover is pre-loaded async in LoadComplete. The state
                // progression is:
                //   NotLoaded → Loading → Ready (BDL done)
                //                       → Loaded (after AddInternal + LoadComplete)
                // The async callback in LoadComplete calls AddInternal, so
                // by the time this click fires the popover is in `Loaded`,
                // not `Ready`. Use the IsLoaded helper (== LoadState.Loaded)
                // — the previous "!= Ready" check returned true for both
                // "still loading" AND "fully loaded", which silently
                // dropped every click. That was the regression that broke
                // the toolbar pill in v2026.508.10.
                if (popover == null || !popover.IsLoaded)
                {
                    Logger.Log("[ToriiServerPulse] popover not ready yet, ignoring click", LoggingTarget.Runtime, LogLevel.Verbose);
                    return;
                }

                if (popover.State.Value == Visibility.Visible)
                {
                    Logger.Log("[ToriiServerPulse] hiding popover", LoggingTarget.Runtime, LogLevel.Verbose);
                    popover.Hide();
                    pulse?.SetPopoverOpen(false);
                }
                else
                {
                    Logger.Log("[ToriiServerPulse] showing popover", LoggingTarget.Runtime, LogLevel.Verbose);
                    popover.Show();
                    pulse?.SetPopoverOpen(true);
                    pulse?.RefreshNow();
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[ToriiServerPulse] togglePopover threw: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}", LoggingTarget.Runtime, LogLevel.Error);

                // If the popover got into a bad state (e.g. async-load
                // crashed mid-construction), tear it down so the next user
                // gesture isn't permanently locked out. We rebuild via the
                // same async path so we still don't freeze the UI thread.
                if (popover != null)
                {
                    try
                    {
                        RemoveInternal(popover, true);
                    }
                    catch (Exception cleanupEx)
                    {
                        Logger.Log($"[ToriiServerPulse] popover cleanup also threw: {cleanupEx.Message}", LoggingTarget.Runtime, LogLevel.Error);
                    }

                    popover = null;

                    // Kick off a fresh async-load so a future click works.
                    popover = new ToriiServerPulsePopover { BypassAutoSizeAxes = Axes.Both };
                    LoadComponentAsync(popover, p =>
                    {
                        AddInternal(p);
                        p.AnchoredAt = this;
                    });
                }
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            if (pulse != null)
                pulse.PlayDetected -= onPlayDetected;
            heartbeatLoopDelegate?.Cancel();

            base.Dispose(isDisposing);
        }

        // -------------------------------------------------------------
        // HeartbeatDot
        // -------------------------------------------------------------
        // Pulsing vermillion dot that lives at the start of the pill.
        // Two animation modes:
        //   - Idle ambient pulse: continuous loop, slow & gentle. Conveys
        //     "this is alive" without demanding attention.
        //   - One-shot Flash: triggered by PlayDetected. A brighter,
        //     bigger spike that fades quickly. Conveys "a new play just
        //     landed". Doesn't disrupt the idle pulse — both run via
        //     transforms and merge cleanly via the framework's transform
        //     stack.
        //
        // Implemented as a CircularContainer with a Box fill + an outer
        // halo that scales further than the core for visual depth.
        // -------------------------------------------------------------
        private partial class HeartbeatDot : CompositeDrawable
        {
            private CircularContainer core = null!;
            private CircularContainer halo = null!;
            private Box coreBox = null!;
            private Box haloBox = null!;

            private bool isActive = true;

            public HeartbeatDot()
            {
                AlwaysPresent = true;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    halo = new CircularContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        Child = haloBox = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = torii_red.Opacity(0.35f),
                        },
                        Scale = new Vector2(1.6f),
                        Alpha = 0.4f,
                    },
                    core = new CircularContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        Child = coreBox = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = torii_red,
                        },
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                startAmbientPulse();
            }

            public void SetActive(bool active)
            {
                if (isActive == active) return;
                isActive = active;

                if (active)
                {
                    coreBox.FadeColour(torii_red, 220, Easing.OutQuint);
                    haloBox.FadeColour(torii_red.Opacity(0.35f), 220, Easing.OutQuint);
                    halo.FadeTo(0.4f, 220, Easing.OutQuint);
                    startAmbientPulse();
                }
                else
                {
                    // Desaturate to a neutral grey + stop the pulse. The
                    // dot stays visible (as a "no signal" indicator) but
                    // doesn't pretend to be alive.
                    coreBox.FadeColour(new Color4(120, 120, 120, 255), 220, Easing.OutQuint);
                    haloBox.FadeColour(new Color4(120, 120, 120, 80), 220, Easing.OutQuint);
                    halo.FadeTo(0.15f, 220, Easing.OutQuint);
                    core.ClearTransforms(targetMember: nameof(Scale));
                    core.ScaleTo(1f, 220, Easing.OutQuint);
                    halo.ClearTransforms(targetMember: nameof(Scale));
                    halo.ScaleTo(1.6f, 220, Easing.OutQuint);
                }
            }

            private void startAmbientPulse()
            {
                core.ClearTransforms(targetMember: nameof(Scale));
                halo.ClearTransforms(targetMember: nameof(Scale));

                // 60 BPM-ish heartbeat — 1000ms cycle, scaled phases for
                // the classic "lub-dub" feel: quick contraction, longer
                // expansion, rest.
                core.ScaleTo(1.10f, 180, Easing.OutQuint)
                    .Then()
                    .ScaleTo(1f, 320, Easing.OutQuint)
                    .Delay(540)
                    .Loop();

                halo.ScaleTo(2.0f, 180, Easing.OutQuint)
                    .FadeTo(0.55f, 180, Easing.OutQuint)
                    .Then()
                    .ScaleTo(1.6f, 480, Easing.OutQuint)
                    .FadeTo(0.4f, 480, Easing.OutQuint)
                    .Delay(380)
                    .Loop();
            }

            /// <summary>
            /// Fire a one-shot bright flash on top of the ambient pulse.
            /// Scales by sqrt(delta) so a burst of plays produces a
            /// noticeably bigger flash without the small "+1" cases
            /// being underwhelming.
            /// </summary>
            public void Flash(int delta)
            {
                if (!isActive) return;

                float intensity = MathF.Min(1.6f, 1.0f + 0.18f * MathF.Sqrt(MathF.Max(1, delta)));

                halo.ClearTransforms(targetMember: nameof(Scale));
                halo.ClearTransforms(targetMember: nameof(Alpha));
                halo.ScaleTo(2.6f * intensity, 90, Easing.OutQuint)
                    .FadeTo(0.85f, 90, Easing.OutQuint)
                    .Then()
                    .ScaleTo(1.6f, 520, Easing.OutQuint)
                    .FadeTo(0.4f, 520, Easing.OutQuint);
            }
        }
    }
}
