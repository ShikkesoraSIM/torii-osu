// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
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
using osu.Game.Input.Bindings;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ToriiBriefing
{
    /// <summary>
    /// popup glass (estilo briefing) que sugiere prender el auto-hide de la toolbar. lo dispara el
    /// stable song select la primera vez, o a los ~30 toggles de Ctrl+T. no es un toast de esquina:
    /// es un panel centrado con el glass, de la misma familia que el skip-break briefing.
    /// </summary>
    public partial class ToolbarAutoHideHintOverlay : VisibilityContainer, IKeyBindingHandler<GlobalAction>
    {
        /// <summary>fired cuando el usuario elige prender el auto-hide.</summary>
        public Action OnEnable;

        private Container panel;

        public ToolbarAutoHideHintOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        // capturamos todo el input posicional asi nada se filtra a lo de atras del scrim; los hijos
        // interactivos (botones) estan adelante y reciben sus eventos primero.
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
                    Width = 520,
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
                // kicker: el badge torii-gate + label, la firma del briefing.
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
                    Text = "Hide the toolbar",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
                },
                new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: BriefingTheme.TypeBody))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                    Text = "You can auto-hide the toolbar and bring it back any time by moving the cursor to the very top of the screen. It feels great with the stable song select. Want to turn it on?",
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(BriefingTheme.SpacingSm, 0),
                    Children = new Drawable[]
                    {
                        new PillButton(BriefingTheme.AccentPink, true)
                        {
                            Width = 220,
                            Height = 44,
                            LabelText = "Enable auto-hide",
                            Action = enable,
                        },
                        new PillButton(Color4.White, false)
                        {
                            Width = 130,
                            Height = 44,
                            LabelText = "Not now",
                            Action = () => Hide(),
                        },
                    },
                },
            });
        }

        private void enable()
        {
            Hide();
            OnEnable?.Invoke();
        }

        // con el popup arriba, tratamos Back (Escape) como "ok, despues" asi cierra en vez de caer
        // al screen de atras.
        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Repeat)
                return false;

            if (e.Action == GlobalAction.Back)
            {
                Hide();
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

        /// <summary>pill al estilo del CTA del briefing. filled = primario, sino subtle.</summary>
        private partial class PillButton : OsuClickableContainer
        {
            public LocalisableString LabelText { private get; init; }

            private readonly Color4 accent;
            private readonly bool filled;
            private Box background;

            public PillButton(Color4 accent, bool filled)
            {
                this.accent = accent;
                this.filled = filled;
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
                        Alpha = filled ? 0.9f : 0.12f,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = LabelText,
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                        Colour = filled ? Color4.Black : Color4.White,
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.FadeTo(filled ? 1f : 0.22f, BriefingTheme.HoverDuration, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                background.FadeTo(filled ? 0.9f : 0.12f, BriefingTheme.HoverDuration, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
