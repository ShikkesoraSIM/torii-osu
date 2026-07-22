// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Performance;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ToriiBriefing
{
    /// <summary>
    /// torii: popup de arranque (estilo BriefingGlass) que avisa que hay que recalcular la dificultad de
    /// los mapas y deja elegir cuanta CPU usar. se registra en la capa top-most global (como el briefing)
    /// asi NO queda dimmeado por la pantalla. se auto-dispara cuando el processor anuncia mapas pendientes.
    /// si se cierra (escape) sin elegir, cae al modo gentil.
    /// </summary>
    public partial class DifficultyRecalcOverlay : OsuFocusedOverlayContainer
    {
        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        private bool chosen;
        private bool wasShown;
        private bool showScheduled;
        private Container panelContainer = null!;

        public override bool BlockScreenWideMouse => true;

        protected override string PopInSampleName => @"UI/overlay-big-pop-in";
        protected override string PopOutSampleName => @"UI/overlay-big-pop-out";

        public DifficultyRecalcOverlay()
        {
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;
            // arranca oculto pero tiene que seguir actualizando (su Scheduler) para que el trigger
            // diferido (esperar el announce + el delay del Show) corra aunque todavia no se mostro.
            AlwaysPresent = true;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // solo el scrim al inicio; el panel se arma cuando sabemos cuantos mapas hay.
            InternalChild = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0.6f), Color4.Black.Opacity(0.72f)),
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ToriiDifficultyRecalcCoordinator.PendingReady.ContinueWith(_ => Schedule(onPendingResolved));
        }

        private void onPendingResolved()
        {
            int count = ToriiDifficultyRecalcCoordinator.PendingCount;

            // corrida silenciosa (re-own tras un wipe del cliente oficial / resume / backfill de
            // imports): la eleccion de CPU ya se hizo alguna vez, la respetamos sin popup.
            if (!ToriiDifficultyRecalcCoordinator.PendingInteractive)
            {
                ToriiDifficultyRecalcCoordinator.Choose(count < 200
                    ? ToriiDifficultyRecalcMode.LazerDefault
                    : config.Get<ToriiDifficultyRecalcMode>(OsuSetting.ToriiDifficultyRecalcMode));
                return;
            }

            // pocos mapas: el recalculo es trivial, no molestamos con el popup. modo gentil y listo.
            if (count < 200)
            {
                ToriiDifficultyRecalcCoordinator.Choose(ToriiDifficultyRecalcMode.LazerDefault);
                return;
            }

            buildPanel(count);

            // los overlays estan deshabilitados durante el intro/transiciones; Show() ahi se suprime y el
            // overlay queda invisible. esperamos a que la activacion sea All (menu listo) y recien ahi
            // mostramos, con un respiro para que asiente la animacion de entrada.
            OverlayActivationMode.BindValueChanged(mode =>
            {
                if (showScheduled || mode.NewValue != OverlayActivation.All)
                    return;

                showScheduled = true;
                Scheduler.AddDelayed(Show, 800);
            }, true);
        }

        private void buildPanel(int count)
        {
            AddInternal(panelContainer = new Container
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Width = 560,
                AutoSizeAxes = Axes.Y,
                Child = new BriefingGlass
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    CornerSize = BriefingTheme.CornerLg,
                    ShadowColor = Color4.Black,
                    ShadowOpacity = 0.30f,
                    ShadowRadius = 44f,
                    ShadowRoundness = 16f,
                    ShadowOffset = new Vector2(0, 18f),
                    SpecularStrength = 0.18f,
                    SpecularHeight = 70f,
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 12),
                        Padding = new MarginPadding { Horizontal = 38, Vertical = 34 },
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Icon = FontAwesome.Solid.Calculator,
                                Size = new Vector2(32),
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Colour = colours.Yellow,
                                Margin = new MarginPadding { Bottom = 4 },
                            },
                            new OsuSpriteText
                            {
                                Text = "Recalculating beatmap difficulty",
                                Font = OsuFont.GetFont(size: 26, weight: FontWeight.SemiBold),
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                            },
                            new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(size: 15))
                            {
                                Text = $"Torii needs to recalculate the star rating of {count:N0} beatmaps with the new pp system. "
                                       + "This runs once in the background and pauses while you play. Pick how much CPU to use:",
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                TextAnchor = Anchor.TopCentre,
                                Colour = colours.GrayC,
                                Margin = new MarginPadding { Bottom = 8 },
                            },
                            modeButton(count, "i paid for the whole cpu", "uses almost every core", colours.Pink, ToriiDifficultyRecalcMode.MaxCores),
                            modeButton(count, "Half the cores", "faster, some impact", colours.Blue, ToriiDifficultyRecalcMode.HalfCores),
                            modeButton(count, "Gentle (lazer default)", "barely noticeable", colours.Gray5, ToriiDifficultyRecalcMode.LazerDefault),
                        },
                    },
                },
            });
        }

        private Drawable modeButton(int count, string title, string note, Color4 accent, ToriiDifficultyRecalcMode mode)
        {
            int seconds = ToriiDifficultyRecalc.EstimateSeconds(count, ToriiDifficultyRecalc.ParallelismFor(mode));
            string eta = ToriiDifficultyRecalc.FormatEta(seconds);

            return new RecalcModeButton(title, $"~{eta}  ·  {note}", accent)
            {
                RelativeSizeAxes = Axes.X,
                Action = () => choose(mode),
            };
        }

        private void choose(ToriiDifficultyRecalcMode mode)
        {
            if (chosen)
                return;

            chosen = true;
            config.SetValue(OsuSetting.ToriiDifficultyRecalcMode, mode);
            ToriiDifficultyRecalcCoordinator.Choose(mode);
            Hide();
        }

        protected override void PopIn()
        {
            wasShown = true;
            this.FadeIn(300, Easing.OutQuint);
            panelContainer?.ScaleTo(0.9f).ScaleTo(1f, 500, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            // PopOut tambien corre al inicializar el overlay en estado Hidden (antes de mostrarse): ahi NO
            // queremos elegir nada. solo si ya se mostro de verdad y se cierra sin elegir -> default gentil.
            if (wasShown && !chosen)
            {
                choose(ToriiDifficultyRecalcMode.LazerDefault);
                this.Delay(220).Expire();
            }

            this.FadeOut(200, Easing.OutQuint);
            panelContainer?.ScaleTo(0.92f, 200, Easing.OutQuint);
        }

        /// <summary>boton de un modo: titulo + subtitulo (ETA), con una barra de acento a la izquierda.</summary>
        private partial class RecalcModeButton : OsuClickableContainer
        {
            private readonly string title;
            private readonly string subtitle;
            private readonly Color4 accent;

            private Box background = null!;

            public RecalcModeButton(string title, string subtitle, Color4 accent)
            {
                this.title = title;
                this.subtitle = subtitle;
                this.accent = accent;
                Height = 52;
                CornerRadius = 8;
                Masking = true;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White.Opacity(0.06f),
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = 4,
                        Colour = accent,
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Y,
                        RelativeSizeAxes = Axes.X,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Left = 18, Right = 14 },
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = title,
                                Font = OsuFont.GetFont(size: 16, weight: FontWeight.SemiBold),
                            },
                            new OsuSpriteText
                            {
                                Text = subtitle,
                                Font = OsuFont.GetFont(size: 12),
                                Alpha = 0.7f,
                            },
                        },
                    },
                };
            }

            protected override bool OnHover(osu.Framework.Input.Events.HoverEvent e)
            {
                background.FadeColour(Color4.White.Opacity(0.14f), 150, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(osu.Framework.Input.Events.HoverLostEvent e)
            {
                background.FadeColour(Color4.White.Opacity(0.06f), 150, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
