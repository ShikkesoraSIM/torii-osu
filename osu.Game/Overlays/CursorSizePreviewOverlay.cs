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
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Skinning;
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

        // Base diameter the preview is multiplied against. Picked so
        // the preview at 1.0× sits comfortably next to the size text
        // without dwarfing it. Skin cursor sprites are scaled to fit
        // this footprint.
        private const float base_preview_size = 36f;

        private Container pill = null!;
        private Container previewHost = null!;
        private OsuSpriteText valueText = null!;

        private IBindable<float> gameplayCursorSize = null!;

        private ScheduledDelegate? hideTask;

        // Audio: same sample the OSD toasts use, same debounce static
        // so we don't double-play when both fire on the same frame.
        private Sample? sampleChange;
        private Bindable<double?> lastSamplePlaybackTime = null!;

        // Skin: resolved nullable so we don't crash in test contexts
        // where ISkinSource isn't cached. Fallback preview (the
        // generic circle) handles missing-source gracefully.
        [Resolved(canBeNull: true)]
        private ISkinSource? skinSource { get; set; }

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
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(14, 0),
                        Padding = new MarginPadding { Horizontal = 18, Vertical = 12 },
                        Children = new Drawable[]
                        {
                            // Stable host that's always sized to the
                            // 2×-max preview footprint, so the inner
                            // (cursor sprite or fallback circle) can
                            // grow / shrink without changing the
                            // pill's overall layout.
                            previewHost = new Container
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(base_preview_size * 2),
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
            };

            buildPreview();

            gameplayCursorSize.BindValueChanged(v => updateForSize(v.NewValue), true);
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

        private void updateForSize(float gameplayValue)
        {
            valueText.Text = $@"{gameplayValue:0.00}×";

            if (previewHost.Child is Drawable child)
            {
                child.Size = new Vector2(base_preview_size * gameplayValue);
                child.Anchor = Anchor.Centre;
                child.Origin = Anchor.Centre;
            }
        }

        /// <summary>
        /// Build the preview drawable — try to load the user's actual
        /// skin cursor sprites first, fall back to a generic circle if
        /// the active skin doesn't ship them (Argon / Triangles).
        ///
        /// Note: this only runs once at load. If the user changes
        /// skin while the overlay is hidden, the preview won't
        /// update until they restart the client. That's acceptable
        /// for a transient feedback overlay — the alternative
        /// (rebinding to skin-source-changed events and rebuilding)
        /// adds complexity for an edge case nobody will hit during
        /// the 1.4s the overlay is visible.
        /// </summary>
        private void buildPreview()
        {
            previewHost.Child = createPreviewDrawable();
        }

        private Drawable createPreviewDrawable()
        {
            Texture? cursor = skinSource?.GetTexture(@"cursor");

            if (cursor != null)
            {
                // Stack cursor + cursormiddle the same way LegacyCursor
                // does — gives the user the EXACT visual they'll see
                // in gameplay (or as close as we can get without
                // running through the full ruleset cursor pipeline).
                Texture? middle = skinSource?.GetTexture(@"cursormiddle");

                var stack = new Container
                {
                    Size = new Vector2(base_preview_size),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Child = new Sprite
                    {
                        Texture = cursor,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        FillMode = FillMode.Fit,
                    },
                };

                if (middle != null)
                {
                    stack.Add(new Sprite
                    {
                        Texture = middle,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        FillMode = FillMode.Fit,
                    });
                }

                return stack;
            }

            // Fallback: stylised circle that approximates a default
            // osu! cursor (translucent pink fill + white ring + dot).
            // Used when the active skin has no cursor.png — Argon /
            // Triangles / vanilla.
            return new CircularContainer
            {
                Size = new Vector2(base_preview_size),
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Masking = true,
                MaskingSmoothness = 2f,
                BorderThickness = 2f,
                BorderColour = Color4.White.Opacity(0.95f),
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Glow,
                    Radius = 6,
                    Colour = new Color4(255, 130, 195, 130),
                },
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(255, 138, 211, 110),
                    },
                    new CircularContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(0.32f),
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.White,
                        },
                    },
                },
            };
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
