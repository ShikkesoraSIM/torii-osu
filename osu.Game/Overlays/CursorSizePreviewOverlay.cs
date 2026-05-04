// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays
{
    /// <summary>
    /// Transient HUD overlay shown when the user adjusts cursor size
    /// via the IncreaseCursorSize / DecreaseCursorSize global hotkeys.
    /// Modelled after the volume meter overlay: pops in on every
    /// adjustment, auto-hides after a brief idle, never demands
    /// dismissal.
    ///
    /// What it shows
    /// -------------
    /// - The user's ACTUAL skin cursor sprites (<c>cursor.png</c> +
    ///   optional <c>cursormiddle.png</c> if present, the same way
    ///   the gameplay cursor is composed). This is loaded from the
    ///   active <see cref="ISkinSource"/> so a legacy skin shows its
    ///   own art, an Argon / Triangles skin (which doesn't ship
    ///   those legacy textures) falls back to a stylised circle that
    ///   approximates the default look.
    /// - Numeric size value next to the preview (e.g. "1.20×") with
    ///   the "CURSOR SIZE" header.
    ///
    /// Sound feedback
    /// --------------
    /// Plays the same <c>UI/osd-change</c> sample that the standard
    /// <c>TrackedSettingToast</c> uses when a setting changes —
    /// keeps the audio feel consistent with the rest of the OSD
    /// system. Debounced through the shared
    /// <see cref="Static.LastHoverSoundPlaybackTime"/> static so
    /// holding the wheel doesn't machine-gun the speakers.
    /// </summary>
    public partial class CursorSizePreviewOverlay : VisibilityContainer
    {
        // Auto-hide grace after the last adjustment. Shorter than the
        // volume meter (~2s) because a cursor adjust is typically a
        // single ramp rather than a back-and-forth tweak.
        private const int hide_after_ms = 1400;

        // Outer host has to fit SkinnableGameplayCursor at its
        // maximum effective scale (BASE_SIZE 50 × max GameplayCursorSize
        // 2.0 = 100), plus a touch of padding so cursor textures
        // larger than the bounding box (most legacy skins ship
        // 128×128 cursor.png) still have somewhere to render.
        private const float host_size = 130f;

        // Vermillion — same shade ToriiClientBadge uses for the user-panel
        // gate glyph. Tying both surfaces to one constant keeps the Torii
        // brand colour consistent across the client (badge in social panel,
        // badge in cursor preview, anywhere else we might add a Torii
        // call-out) — change once and everything follows.
        private static readonly Color4 torii_red = new Color4(204, 41, 41, 255);

        private Container pill = null!;
        private Container previewHost = null!;
        private OsuSpriteText valueText = null!;

        private IBindable<float> gameplayCursorSize = null!;

        private ScheduledDelegate? hideTask;

        // Audio: same sample the OSD toasts use, same debounce static
        // so we don't double-play when both fire on the same frame.
        private Sample? sampleChange;
        private Bindable<double?> lastSamplePlaybackTime = null!;

        public CursorSizePreviewOverlay()
        {
            Anchor = Anchor.BottomCentre;
            Origin = Anchor.BottomCentre;
            AutoSizeAxes = Axes.Both;
            // Above the screen-bottom edge but below the gameplay-area
            // mid-screen ring so it doesn't cover the play field.
            Margin = new MarginPadding { Bottom = 80 };
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config, AudioManager audio, SessionStatics statics)
        {
            gameplayCursorSize = config.GetBindable<float>(OsuSetting.GameplayCursorSize);

            sampleChange = audio.Samples.Get(@"UI/osd-change");
            lastSamplePlaybackTime = statics.GetBindable<double?>(Static.LastHoverSoundPlaybackTime);

            Child = pill = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 18,
                CornerExponent = 2.2f,
                MaskingSmoothness = 1.6f,
                BorderThickness = 1f,
                BorderColour = new Color4(150, 168, 230, 100),
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Radius = 16,
                    Roundness = 12,
                    Colour = new Color4(0, 4, 24, 170),
                    Offset = new Vector2(0, 4),
                },
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(12, 14, 32, 232),
                    },
                    // Outer vertical flow: small "TORII EXCLUSIVE" header
                    // strip on top, then the original [cursor preview] +
                    // [size value] horizontal layout. The header reads as
                    // a quiet "powered by" caption — vermillion gate +
                    // small label, low-contrast against the dark pill so
                    // it doesn't compete with the actual cursor preview
                    // for attention but immediately communicates "this
                    // size hotkey is a Torii feature, not vanilla lazer".
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 6),
                        Padding = new MarginPadding { Horizontal = 18, Vertical = 12 },
                        Children = new Drawable[]
                        {
                            // "TORII EXCLUSIVE" header — gate glyph + label
                            // pill, sized small + sat slightly to the side
                            // so the eye doesn't read it as the primary
                            // content. Same vermillion as ToriiClientBadge
                            // for brand cohesion across the client.
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Anchor = Anchor.TopLeft,
                                Origin = Anchor.TopLeft,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(5, 0),
                                Children = new Drawable[]
                                {
                                    new ToriiGateGlyph
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Size = new Vector2(10, 10),
                                        Colour = torii_red,
                                    },
                                    new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Text = @"TORII EXCLUSIVE",
                                        Font = OsuFont.GetFont(size: 9, weight: FontWeight.Bold),
                                        Spacing = new Vector2(1.2f, 0),
                                        Colour = torii_red,
                                        // Tiny optical-centring nudge — the
                                        // glyph's visual centre sits slightly
                                        // above the text baseline because
                                        // capitals don't have descenders.
                                        Margin = new MarginPadding { Bottom = 1 },
                                    },
                                },
                            },
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(14, 0),
                                Children = new Drawable[]
                                {
                                    // Stable host sized to fit the preview at
                                    // its largest scale (2.0×) plus a touch of
                                    // padding. The inner preview keeps its
                                    // base Size constant and is animated via
                                    // .Scale instead — Scale-based animation
                                    // is more robust against layout-cycle
                                    // surprises that we hit when resizing the
                                    // child directly (the cursor sprite was
                                    // ignoring the new Size and rendering at
                                    // its texture's native footprint, making
                                    // the preview look identical regardless
                                    // of the actual cursor-size value).
                                    previewHost = new Container
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Size = new Vector2(host_size),
                                    },
                                    new FillFlowContainer
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        AutoSizeAxes = Axes.Both,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, 2),
                                        Children = new Drawable[]
                                        {
                                            new OsuSpriteText
                                            {
                                                Text = @"CURSOR SIZE",
                                                Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                                                Spacing = new Vector2(1.4f, 0),
                                                Colour = Color4.White.Opacity(0.6f),
                                            },
                                            valueText = new OsuSpriteText
                                            {
                                                Font = OsuFont.Numeric.With(size: 22, fixedWidth: true),
                                                Colour = Color4.White,
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };

            // SkinnableGameplayCursor manages its own scaling against
            // GameplayCursorSize internally — we just drop it in and
            // it auto-mirrors the in-game cursor visual + scale 1:1.
            // No more tweaking of size constants on our side.
            previewHost.Child = new SkinnableGameplayCursor
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
            };

            // We still bind to the size for the numeric label + sound
            // pitch — those don't come from the cursor drawable itself.
            gameplayCursorSize.BindValueChanged(v => valueText.Text = $@"{v.NewValue:0.00}×", true);
        }

        /// <summary>
        /// Called by <see cref="OsuGame"/> after each cursor-size
        /// hotkey press. Pops the overlay in (if hidden), arms / re-
        /// arms the auto-hide timer, and plays the OSD-change sample
        /// (debounced so a wheel ramp doesn't machine-gun audio).
        /// </summary>
        public void OnAdjusted()
        {
            Show();

            hideTask?.Cancel();
            hideTask = Scheduler.AddDelayed(Hide, hide_after_ms);

            playAdjustSample();
        }

        private void playAdjustSample()
        {
            if (sampleChange == null) return;

            // Same debounce mechanism TrackedSettingToast uses — share
            // the static so hover sounds and our adjust sound don't
            // both fire on the exact same frame and clip each other.
            bool enoughTimePassed = !lastSamplePlaybackTime.Value.HasValue
                                    || Time.Current - lastSamplePlaybackTime.Value >= OsuGameBase.SAMPLE_DEBOUNCE_TIME;

            if (!enoughTimePassed) return;

            // Pitch the sample up slightly as the value grows — gives
            // the user audible feedback that they're moving in a
            // direction without needing to look at the screen.
            sampleChange.Frequency.Value = 0.92 + (gameplayCursorSize.Value / 2.0) * 0.16;
            sampleChange.Play();

            lastSamplePlaybackTime.Value = Time.Current;
        }

        protected override void PopIn()
        {
            this.FadeIn(160, Easing.OutQuint);
            pill.MoveToY(0, 220, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.FadeOut(220, Easing.OutQuint);
            pill.MoveToY(20, 200, Easing.OutQuint);
        }
    }
}
