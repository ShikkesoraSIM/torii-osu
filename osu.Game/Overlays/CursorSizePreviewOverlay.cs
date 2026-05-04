// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
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
    /// - A live preview circle styled like the osu! gameplay cursor
    ///   (translucent fill + ring + centre dot) sized exactly to
    ///   <see cref="OsuSetting.GameplayCursorSize"/>. Gives the user
    ///   a visual reference for how big the cursor will be in actual
    ///   play without having to start a map.
    /// - The numeric value (e.g. "1.20×") + a "menu / gameplay" label
    ///   so the user knows what they're affecting. The hotkey writes
    ///   both the menu and gameplay cursor sizes; we surface the
    ///   gameplay value because that's the one users are usually
    ///   trying to dial in.
    ///
    /// Why a separate overlay instead of piggy-backing the volume
    /// meter: the volume meter is heavily customised for audio
    /// (multiple meters, mute toggle, cumulative scroll handling)
    /// and adding another mode would weigh it down. A dedicated
    /// 80-line component is simpler.
    /// </summary>
    public partial class CursorSizePreviewOverlay : VisibilityContainer
    {
        // Auto-hide grace after the last adjustment. Shorter than the
        // volume meter (which sits ~2s) because a cursor adjust is
        // typically a single ramp rather than a back-and-forth tweak.
        private const int hide_after_ms = 1400;

        // Base diameter the preview circle is multiplied against. Set
        // so the circle at 1.0× sits comfortably next to the size
        // text without dwarfing it.
        private const float base_preview_size = 36f;

        private Container pill = null!;
        private Container previewCircleHost = null!;
        private OsuSpriteText valueText = null!;

        private IBindable<float> menuCursorSize = null!;
        private IBindable<float> gameplayCursorSize = null!;

        private ScheduledDelegate? hideTask;

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
        private void load(OsuConfigManager config)
        {
            menuCursorSize = config.GetBindable<float>(OsuSetting.MenuCursorSize);
            gameplayCursorSize = config.GetBindable<float>(OsuSetting.GameplayCursorSize);

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
                            // Stable-size host so the preview can scale
                            // up to 2× without changing the pill's
                            // overall layout — picks the largest
                            // possible preview footprint up front and
                            // the inner circle scales within it.
                            previewCircleHost = new Container
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(base_preview_size * 2),
                                Child = createPreviewCursor(),
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

            // Live updates while held / scrolled. Using gameplayCursorSize
            // because that's the more meaningful "in-game preview" size,
            // and our hotkey writes both bindables in lockstep so menu
            // and gameplay always read the same number.
            gameplayCursorSize.BindValueChanged(v => updateForSize(v.NewValue), true);
        }

        /// <summary>
        /// Called by <see cref="OsuGame"/> after each cursor-size
        /// hotkey press. Pops the overlay in (if hidden) and arms /
        /// re-arms the auto-hide timer.
        /// </summary>
        public void OnAdjusted()
        {
            Show();

            hideTask?.Cancel();
            hideTask = Scheduler.AddDelayed(Hide, hide_after_ms);
        }

        private void updateForSize(float gameplayValue)
        {
            // Format with two decimals, append ×. The menu vs gameplay
            // value can differ if the user is tuning them separately
            // via Settings, but they're written together by the
            // hotkey so during an adjustment they stay in sync.
            valueText.Text = $@"{gameplayValue:0.00}×";

            // Diameter scales linearly with the cursor size. The host
            // is always base_preview_size × 2 (max footprint), so the
            // child stays centred as it grows / shrinks.
            float diameter = base_preview_size * gameplayValue;
            if (previewCircleHost.Child is Drawable child)
            {
                child.Size = new Vector2(diameter);
                child.Anchor = Anchor.Centre;
                child.Origin = Anchor.Centre;
            }
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

        /// <summary>
        /// Builds a stylised cursor-shaped circle used as the preview
        /// drawable. Three layers stacked centre-out: outer ring,
        /// translucent pink fill, opaque centre dot — matches the
        /// silhouette of the default osu! gameplay cursor enough to
        /// give the user a useful visual reference.
        /// </summary>
        private Drawable createPreviewCursor()
        {
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
    }
}
