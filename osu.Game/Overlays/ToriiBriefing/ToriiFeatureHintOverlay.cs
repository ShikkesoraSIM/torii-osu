// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
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
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ToriiBriefing
{
    /// <summary>
    /// popup glass (estilo briefing) reutilizable para "descubrir features": kicker torii-gate, titulo,
    /// un cuerpo que se setea por contexto al mostrarlo, y el toggle real de la opcion embebido (igual
    /// que se ve en settings) asi el usuario la prende ahi mismo. tiene sonido + animacion de pop-in
    /// como los otros overlays. vive en OsuGame (no en una screen) asi que no lo afecta el dispose de
    /// la song select; el delay de aparicion lo maneja el caller.
    /// </summary>
    public partial class ToriiFeatureHintOverlay : VisibilityContainer, IKeyBindingHandler<GlobalAction>
    {
        public LocalisableString Title { get; init; }
        public LocalisableString ToggleCaption { get; init; }
        public LocalisableString ToggleHint { get; init; }
        public Color4 Accent { get; init; } = BriefingTheme.AccentPink;

        /// <summary>el bindable de config (ya bindeado) de la opcion que ofrece el popup. lo guardamos
        /// como campo (init) asi no lo recolecta el GC y la binding del checkbox sobrevive.</summary>
        public Bindable<bool> Toggle { get; init; }

        // el FormCheckBox embebido necesita un OverlayColourProvider en scope (sino crashea al cargar,
        // como el Player que cachea el suyo); lo proveemos nosotros porque vivimos en OsuGame.
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Pink);

        private Container panel;
        private OsuTextFlowContainer bodyFlow;
        private Sample samplePopIn;
        private Sample samplePopOut;

        public ToriiFeatureHintOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        // capturamos todo el input posicional asi nada se filtra atras del scrim; los hijos interactivos
        // (toggle, boton) estan adelante y reciben sus eventos primero.
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;
        protected override bool OnClick(ClickEvent e) => true;
        protected override bool OnMouseDown(MouseDownEvent e) => true;

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            samplePopIn = audio?.Samples.Get(@"UI/overlay-big-pop-in");
            samplePopOut = audio?.Samples.Get(@"UI/overlay-big-pop-out");

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
                    Width = 540,
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

            // todos los hijos del flow vertical quedan anclados arriba (Top-Y); no mezclar con Centre-Y
            // para evitar el crash de FillFlow con anchors mezclados en el eje cruzado.
            var items = new List<Drawable>(new Drawable[]
            {
                // kicker: badge torii-gate + label, la firma del briefing.
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
                            // la firma "TORII" va siempre en rosa, igual que el resto de los briefings,
                            // sin importar el accent del popup.
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
                    Text = Title,
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
                },
                bodyFlow = new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: BriefingTheme.TypeBody))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                },
            });

            // el toggle es opcional: un popup puramente informativo (ej. "cambio el theme default") no
            // ofrece nada para prender, asi que sin Toggle mostramos solo el cuerpo + el boton de cierre.
            if (Toggle != null)
            {
                items.Add(new BriefingGlass
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
                        Padding = new MarginPadding(BriefingTheme.SpacingMd),
                        Child = new FormCheckBox
                        {
                            Caption = ToggleCaption,
                            HintText = ToggleHint,
                            Current = { BindTarget = Toggle },
                        },
                    },
                });
            }

            items.Add(new DismissButton(Accent)
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Width = 320,
                Height = 44,
                LabelText = "Got it",
                Action = Hide,
            });

            content.AddRange(items);
        }

        /// <summary>setea el cuerpo segun el contexto y muestra el popup (con sonido + animacion).</summary>
        public void Present(LocalisableString body)
        {
            if (bodyFlow != null)
                bodyFlow.Text = body;

            Show();
        }

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
            samplePopIn?.Play();

            this.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            panel.ScaleTo(0.94f).ScaleTo(1f, BriefingTheme.EntranceDuration, Easing.OutBack)
                 .MoveToY(20).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            samplePopOut?.Play();

            this.FadeOut(BriefingTheme.DismissDuration, Easing.OutQuint);
            panel.ScaleTo(0.97f, BriefingTheme.DismissDuration, Easing.OutQuint);
        }

        /// <summary>pill al estilo del CTA del briefing.</summary>
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
