// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Input.Bindings;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings.Sections.Audio;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Configuration
{
    /// <summary>
    /// One-time, Torii-glass-styled briefing shown when the experimental (WASAPI)
    /// low-latency audio engine becomes the default (ports ppy/osu#37856). Folds the
    /// migration in: flips existing Windows installs onto the engine, preserves the
    /// tuning of anyone already using it, and nudges everyone to recalibrate. Fresh
    /// installs and non-Windows are left alone (and never see this).
    /// </summary>
    public partial class NewAudioMigrationOverlay : VisibilityContainer, IKeyBindingHandler<GlobalAction>
    {
        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        // Held so the migration briefing waits for the main menu (OverlayActivationMode
        // flips to All there) instead of popping over the "welcome to osu!" intro.
        // canBeNull for test scenes that don't spin up a full game.
        [Resolved(canBeNull: true)]
        private OsuGame? game { get; set; }

        // The embedded legacy-audio toggle is a FormCheckBox, which resolves an
        // OverlayColourProvider (normally supplied by the settings panel). We're a
        // standalone overlay, so provide one ourselves - pink to match Torii.
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Pink);

        private Container panel = null!;
        private FillFlowContainer content = null!;

        public NewAudioMigrationOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        // Only swallow input while actually visible (this lives in the always-present topmost layer).
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => State.Value == Visibility.Visible;

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = 0.6f,
                },
                panel = new BriefingGlass
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = 560,
                    AutoSizeAxes = Axes.Y,
                    CornerSize = BriefingTheme.CornerLg,
                    SpecularStrength = 0.18f,
                    SpecularHeight = 70,
                    ShadowOpacity = 0.4f,
                    ShadowRadius = 30,
                    RelativeContentSize = Axes.X,
                    Child = content = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, BriefingTheme.SpacingMd),
                        Padding = new MarginPadding(BriefingTheme.SpacingXl),
                    },
                },
            };
        }

        private bool shown;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // The engine flip + offset migration runs now (invisible). The briefing
            // popup, though, is held until the user reaches the main menu - where
            // OverlayActivationMode flips to All - so it doesn't appear over the
            // "welcome to osu!" intro animation.
            if (!runMigration())
                return;

            if (game == null)
            {
                showBriefing();
                return;
            }

            game.OverlayActivationMode.BindValueChanged(mode =>
            {
                if (mode.NewValue == OverlayActivation.All)
                    showBriefing();
            }, true);
        }

        private void showBriefing()
        {
            // BindValueChanged can fire more than once (menu -> gameplay -> menu);
            // only build + pop the briefing the first time.
            if (shown)
                return;

            shown = true;
            buildContent(pendingWasAlreadyUsing);
            Show();
        }

        private bool pendingWasAlreadyUsing;

        /// <summary>
        /// Runs the one-time engine flip + offset migration. Returns true when a
        /// briefing popup should be shown (the caller holds it until the menu).
        /// </summary>
        private bool runMigration()
        {
            if (config.Get<bool>(OsuSetting.NewAudioMigrationApplied))
                return false;

            // Mark as run exactly once, regardless of what happens below.
            config.SetValue(OsuSetting.NewAudioMigrationApplied, true);

            // The WASAPI platform-offset split only applies on Windows.
            if (RuntimeInfo.OS != RuntimeInfo.Platform.Windows)
                return false;

            // Fresh installs have no stored version; they already get the new default
            // and shouldn't see this or have their offset touched.
            if (config.Get<string>(OsuSetting.Version).Length < 6)
                return false;

            pendingWasAlreadyUsing = audio.UseExperimentalWasapi.Value;

            // Preserve the tuning of prior experimental users: the new platform offset would
            // otherwise shift their feel by WINDOWS_EXPERIMENTAL_AUDIO_OFFSET.
            if (pendingWasAlreadyUsing)
                config.SetValue(OsuSetting.AudioOffset, config.Get<double>(OsuSetting.AudioOffset) - FramedBeatmapClock.WINDOWS_EXPERIMENTAL_AUDIO_OFFSET);

            audio.UseExperimentalWasapi.Value = true;

            return true;
        }

        private void buildContent(bool wasAlreadyUsing)
        {
            content.AddRange(new Drawable[]
            {
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(BriefingTheme.SpacingSm, 0),
                    Children = new Drawable[]
                    {
                        new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = FontAwesome.Solid.ToriiGate,
                            Size = new Vector2(BriefingTheme.TypeBody),
                            Colour = BriefingTheme.AccentPink,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = "TORII",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                            Colour = BriefingTheme.AccentPink,
                        },
                    },
                },
                new OsuSpriteText
                {
                    Text = wasAlreadyUsing ? "New audio engine is now default!" : "New audio engine has been enabled",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
                },
                new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: BriefingTheme.TypeBody))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                    Text = wasAlreadyUsing
                        ? "We added a new low-latency audio engine for Windows. Due to overwhelmingly positive feedback, it is now the default.\n\nYou were already using it, so your audio offset has been adjusted automatically to keep your timing the same. Switch back any time from settings (\"Use legacy audio mode\")."
                        : "We added a new low-latency audio engine for Windows. Due to overwhelmingly positive feedback, it is now the default.\n\nBecause it has lower latency, your audio offset will likely need adjusting. You can switch back to the legacy engine below, or any time in settings.",
                },
                new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.Bold))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Colour = Color4Extensions.FromHex(@"ffcc33"),
                    Text = "Either way: play ~20-30 maps around 180-200 BPM at your usual skill level, then re-calibrate your global offset (Settings -> Audio) so it is as accurate as possible.",
                },
                new BriefingGlass
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    CornerSize = BriefingTheme.CornerMd,
                    SurfaceLift = 1.25f,
                    Child = new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding(BriefingTheme.SpacingMd),
                        Child = new AudioDevicesSettings.LegacyAudioCheckbox(),
                    },
                },
                new DismissButton(BriefingTheme.AccentPink)
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Width = 320,
                    Height = 44,
                    LabelText = "Got it!",
                    Action = Hide,
                },
            });
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (!panel.ReceivePositionalInputAt(e.ScreenSpaceMousePosition))
                Hide();

            return true;
        }

        protected override void PopIn()
        {
            this.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            panel.ScaleTo(0.94f).ScaleTo(1f, BriefingTheme.EntranceDuration, Easing.OutBack)
                 .MoveToY(20).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.FadeOut(BriefingTheme.DismissDuration, Easing.OutQuint);
            panel.ScaleTo(0.97f, BriefingTheme.DismissDuration, Easing.OutQuint);
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Repeat)
                return false;

            if (e.Action == GlobalAction.Back && State.Value == Visibility.Visible)
            {
                Hide();
                return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        /// <summary>Pill button styled to match the briefing's primary CTA.</summary>
        private partial class DismissButton : OsuClickableContainer
        {
            public LocalisableString LabelText { private get; init; }

            private readonly Color4 accent;
            private Box background = null!;

            public DismissButton(Color4 accent)
            {
                this.accent = accent;
                Masking = true;
                CornerRadius = BriefingTheme.CornerSm;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = accent,
                        Alpha = 0.9f,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = LabelText,
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                        Colour = Color4.Black,
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeTo(1f, BriefingTheme.HoverDuration, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeTo(0.9f, BriefingTheme.HoverDuration, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
