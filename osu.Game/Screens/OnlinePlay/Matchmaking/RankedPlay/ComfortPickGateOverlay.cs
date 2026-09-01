// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay
{
    /// <summary>
    /// torii: la ventana que salta cuando alguien intenta entrar a la cola de ranked
    /// play sin haber elegido su star rating.
    /// </summary>
    /// <remarks>
    /// <para>
    /// El picker ya existia, pero vivia unicamente adentro de la pantalla de ranked
    /// play. El atajo de la pildora del toolbar lo salteaba, asi que se podia entrar a
    /// la cola sin haber elegido nunca la dificultad: el emparejamiento quedaba a cargo
    /// solo del elo, y asi termina alguien de 5.4 jugando contra alguien de 7.5.
    /// </para>
    /// <para>
    /// PERFORMANCE: esto NO usa <c>AlwaysPresent</c>, y es a proposito. Un
    /// <see cref="OverlayContainer"/> escondido queda en alpha 0, o sea no presente, y
    /// el framework poda el subarbol entero: ni se dibuja ni corre su Update. Cerrado
    /// cuesta exactamente cero, que es lo que tiene que costar algo que se abre unos
    /// segundos y una sola vez por season.
    /// </para>
    /// <para>
    /// Se puede permitir ese lujo justamente porque no tiene nada corriendo de fondo: no
    /// hay polling ni temporizadores, y el contenido se ARMA al abrir y se tira al
    /// cerrar. Un overlay que si necesita seguir vivo mientras esta escondido (el de
    /// render de replays, por ejemplo, que sigue un trabajo en el servidor) tiene que
    /// usar el otro patron: <c>AlwaysPresent</c> en el overlay y lo visual colgando de un
    /// contenedor aparte.
    /// </para>
    /// </remarks>
    public partial class ComfortPickGateOverlay : OverlayContainer
    {
        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private Container panel = null!;
        private Container slot = null!;

        private Action? onPicked;

        /// <summary>Rulesets donde ya confirmamos que el jugador eligio, para no re-preguntar.</summary>
        private readonly HashSet<int> yaEligio = new HashSet<int>();

        public ComfortPickGateOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        // El scrim solo muerde clicks cuando esta abierto. Escondido el overlay no es
        // presente, asi que en la practica no le llega input igual; queda explicito para
        // que siga siendo cierto si alguien le agrega AlwaysPresent mas adelante.
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => State.Value == Visibility.Visible;
        protected override bool OnClick(ClickEvent e) => State.Value == Visibility.Visible;
        protected override bool OnMouseDown(MouseDownEvent e) => State.Value == Visibility.Visible;

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = 0.62f,
                },
                panel = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = 520,
                    AutoSizeAxes = Axes.Y,
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, BriefingTheme.SpacingMd),
                        // Regla de los flows verticales: TODOS los hijos anclados arriba
                        // en Y. Mezclar Centre con TopCentre tira una excepcion en el
                        // primer layout, lejos de la pantalla que lo agrego.
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(BriefingTheme.SpacingSm, 0),
                                Children = new Drawable[]
                                {
                                    new SpriteIcon
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Icon = FontAwesome.Solid.Star,
                                        Size = new Vector2(BriefingTheme.TypeHeadline),
                                        Colour = BriefingTheme.AccentAmber,
                                    },
                                    new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Text = "You need to set your Star Rating first",
                                        Font = OsuFont.GetFont(size: BriefingTheme.TypeHeadline, weight: FontWeight.SemiBold),
                                        Colour = Color4.White,
                                    },
                                },
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "it decides the difficulty you get matched at, and you only pick it once",
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeBody),
                                Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                            },
                            // El picker se instancia al abrir y se tira al cerrar. Asi el
                            // piso anti-sandbag siempre sale de datos frescos, y mientras
                            // esta cerrado no hay ni un drawable colgando.
                            slot = new Container
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                            },
                        },
                    },
                },
            };
        }

        /// <summary>
        /// Deja pasar si el jugador ya eligio su star rating; si no, abre el gate y corre
        /// <paramref name="onPicked"/> recien cuando termine de elegir.
        /// </summary>
        /// <remarks>
        /// Es el metodo que hay que llamar antes de encolar. Pregunta una sola vez por
        /// ruleset y se guarda la respuesta: el pick es una vez por season, asi que
        /// volver a preguntarlo en cada click seria pedir un dato que ya sabemos.
        ///
        /// Se pregunta antes de mostrar nada a proposito. Abrir el gate siempre y dejar
        /// que <see cref="ComfortPickPanel"/> se cierre solo cuando ya elegiste tambien
        /// funcionaria, pero le meteria un parpadeo de scrim a la enorme mayoria que ya
        /// eligio, cada vez que aprieta queue.
        /// </remarks>
        public void EnsurePicked(int rulesetId, Action onPicked)
        {
            if (yaEligio.Contains(rulesetId))
            {
                onPicked();
                return;
            }

            var req = new GetComfortPickFloorRequest(rulesetId);

            req.Success += response => Schedule(() =>
            {
                if (response.AlreadyPicked)
                {
                    yaEligio.Add(rulesetId);
                    onPicked();
                    return;
                }

                open(rulesetId, onPicked);
            });

            // Si no se pudo averiguar, se abre el gate igual: el picker vuelve a
            // consultar por su cuenta y ahi se resuelve. Dejar pasar ante la duda seria
            // el agujero que estamos tapando.
            req.Failure += _ => Schedule(() => open(rulesetId, onPicked));

            api.Queue(req);
        }

        private void open(int rulesetId, Action onPicked)
        {
            this.onPicked = () =>
            {
                yaEligio.Add(rulesetId);
                onPicked();
            };

            slot.Child = new ComfortPickPanel(rulesetId)
            {
                // ComfortPickPanel resuelve solo el caso de "ya elegiste esta season":
                // dispara OnReady sin llegar a dibujar el picker. Por eso este gate no
                // necesita preguntar por su cuenta antes de abrirse.
                OnReady = picked,
            };

            Show();
        }

        private void picked()
        {
            // Con Schedule y no derecho: OnReady sale de adentro del propio
            // ComfortPickPanel, y cerrar aca lo dispondria en la mitad de su callback.
            // Este mismo panel ya nos mordio con eso en la pantalla de ranked play.
            Schedule(() =>
            {
                Action? seguir = onPicked;
                onPicked = null;

                Hide();
                seguir?.Invoke();
            });
        }

        protected override void PopIn()
        {
            this.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            panel.MoveToY(20).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.FadeOut(BriefingTheme.HoverDuration, Easing.OutQuint);

            // OJO: aca NO va un Scheduler.AddDelayed para tirar el picker al terminar el
            // fade. El Scheduler de un drawable se procesa en su Update, y un overlay en
            // alpha 0 deja de ser presente, asi que el framework poda el subarbol y ese
            // Update nunca llega: la tarea quedaria encolada para siempre.
            //
            // El picker se reemplaza en Open(), que es donde importa que sea nuevo. Lo
            // que queda colgando mientras esto esta cerrado no se dibuja ni se updatea
            // (por lo mismo que rompe al Scheduler), asi que no cuesta ni un frame.
        }
    }
}
