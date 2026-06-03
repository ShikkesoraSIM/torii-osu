// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input.Bindings;
using osu.Game.Localisation;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Play
{
    /// <summary>
    /// One-time popup shown the very first time a player presses the mid-map
    /// break skip button. Explains the feature and the double-press
    /// confirmation, and embeds the "single confirmation" toggle inline so
    /// the player can opt into single-press skipping without leaving the map.
    ///
    /// Visually mirrors the Torii Briefing overlay (glass panel + 8pt grid +
    /// squircle corners) via <see cref="BriefingGlass"/> / <see cref="BriefingTheme"/>
    /// so it feels like part of the same family. Shown while the gameplay
    /// clock is soft-paused by <see cref="Player"/>; dismissing it fires
    /// <see cref="OnDismiss"/> which resumes play.
    /// </summary>
    public partial class SkipBreakBriefingOverlay : VisibilityContainer, IKeyBindingHandler<GlobalAction>
    {
        /// <summary>Fired when the player dismisses the briefing (resume + persist "seen").</summary>
        public Action OnDismiss;

        private readonly Bindable<bool> singleConfirmation;

        private Container panel;

        public SkipBreakBriefingOverlay(Bindable<bool> singleConfirmation)
        {
            this.singleConfirmation = singleConfirmation;
            RelativeSizeAxes = Axes.Both;
        }

        // Capture all positional input so nothing leaks to gameplay behind the
        // scrim. The interactive children (toggle, button) sit in front and
        // receive their events first.
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;
        protected override bool OnClick(ClickEvent e) => true;
        protected override bool OnMouseDown(MouseDownEvent e) => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            FillFlowContainer content;

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

            content.AddRange(new Drawable[]
            {
                // Kicker — small torii-gate badge + label, the briefing's signature.
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
                    Text = ToriiSettingsStrings.SkipBreakBriefingTitle,
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
                },
                new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: BriefingTheme.TypeBody))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                    Text = ToriiSettingsStrings.SkipBreakBriefingBody,
                },
                // Inline toggle card so the user can flip the single-press
                // option right here, no settings trip needed.
                new BriefingGlass
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    CornerSize = BriefingTheme.CornerMd,
                    SurfaceLift = 1.25f,
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, BriefingTheme.SpacingSm),
                        Padding = new MarginPadding(BriefingTheme.SpacingMd),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = ToriiSettingsStrings.SkipBreakBriefingInlineTip,
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                                Colour = BriefingTheme.AccentCyan,
                            },
                            new FormCheckBox
                            {
                                Caption = ToriiSettingsStrings.SkipBreaksSingleConfirmation,
                                HintText = ToriiSettingsStrings.SkipBreaksSingleConfirmationHint,
                                Current = { BindTarget = singleConfirmation },
                            },
                        },
                    },
                },
                new DismissButton(BriefingTheme.AccentPink)
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Width = 320,
                    Height = 44,
                    LabelText = ToriiSettingsStrings.SkipBreakBriefingDismiss,
                    Action = dismiss,
                },
            });
        }

        private void dismiss()
        {
            Hide();
            OnDismiss?.Invoke();
        }

        // While the briefing is up, treat Back (Escape) as "got it" so it
        // closes + resumes rather than falling through to Player and opening
        // the pause menu behind us. Only fires when visible/present.
        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Repeat)
                return false;

            if (e.Action == GlobalAction.Back)
            {
                dismiss();
                return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
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

        /// <summary>Pill button styled to match the briefing's primary CTA.</summary>
        private partial class DismissButton : OsuClickableContainer
        {
            public LocalisableString LabelText { private get; init; }

            private readonly Color4 accent;
            private Box background;

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
