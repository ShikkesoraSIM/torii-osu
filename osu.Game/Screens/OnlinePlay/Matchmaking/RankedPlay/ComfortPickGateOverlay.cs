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
using osu.Game.Online.API.Requests.Responses;
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
    /// PERFORMANCE: <c>AlwaysPresent</c> va en el OVERLAY, y todo lo visual cuelga de
    /// <see cref="visualContent"/>, que NO lo tiene. Asi el overlay sigue vivo mientras
    /// esta cerrado (su Update corre, o sea su Scheduler tambien) pero el subarbol que
    /// se dibuja queda en alpha 0, no presente, y el framework lo poda entero. Cerrado
    /// no se dibuja un solo pixel.
    /// </para>
    /// <para>
    /// La primera version NO tenia AlwaysPresent, razonando que sin nada corriendo de
    /// fondo un overlay escondido puede podarse completo. Estaba mal, y de la peor
    /// manera: <see cref="EnsurePicked"/> consulta al servidor ESTANDO CERRADO y
    /// procesa la respuesta con <c>Schedule</c>. Sin AlwaysPresent ese Schedule no corre
    /// nunca, porque el Scheduler se procesa en el Update de un drawable presente. El
    /// sintoma no era un error: era el boton de queue sin hacer absolutamente nada, sin
    /// excepcion en ningun lado y sin que el pedido llegara al servidor.
    /// </para>
    /// <para>
    /// Regla: si un overlay hace CUALQUIER cosa mientras esta escondido (una consulta,
    /// un temporizador, seguir un trabajo del servidor), necesita AlwaysPresent + el
    /// contenido aparte. Es el mismo patron que usa el overlay de render de replays.
    /// </para>
    /// </remarks>
    public partial class ComfortPickGateOverlay : OverlayContainer
    {
        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private Container visualContent = null!;
        private Container panel = null!;
        private Container slot = null!;

        private Action? onPicked;

        /// <summary>Rulesets donde ya confirmamos que el jugador eligio, para no re-preguntar.</summary>
        private readonly HashSet<int> yaEligio = new HashSet<int>();

        public ComfortPickGateOverlay()
        {
            RelativeSizeAxes = Axes.Both;

            // Ver la nota de arriba: esto tiene que seguir updateandose cerrado porque
            // EnsurePicked consulta al servidor sin abrir nada. Lo que no se dibuja es
            // visualContent, que si se poda.
            AlwaysPresent = true;
        }

        // El scrim solo muerde clicks cuando esta abierto. Con AlwaysPresent esto pasa de
        // ser una formalidad a ser IMPRESCINDIBLE: el overlay ocupa la pantalla entera y
        // sigue presente aunque no se vea, asi que sin este gate se comeria todos los
        // clicks del juego mientras esta cerrado.
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => State.Value == Visibility.Visible;
        protected override bool OnClick(ClickEvent e) => State.Value == Visibility.Visible;
        protected override bool OnMouseDown(MouseDownEvent e) => State.Value == Visibility.Visible;

        [BackgroundDependencyLoader]
        private void load()
        {
            // Todo lo visual adentro de este contenedor, que arranca en alpha 0 y por lo
            // tanto no esta presente: el overlay sigue vivo, el dibujo no.
            InternalChild = visualContent = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Alpha = 0,
            };

            visualContent.Children = new Drawable[]
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

                // La respuesta se le PASA al picker en vez de que la pida de nuevo: es
                // exactamente el mismo dato, y pedirlo dos veces le suma otro turno de
                // cola a algo que el jugador esta esperando mirando la pantalla.
                open(rulesetId, onPicked, response);
            });

            // Si no se pudo averiguar, se abre el gate igual: el picker vuelve a
            // consultar por su cuenta y ahi se resuelve. Dejar pasar ante la duda seria
            // el agujero que estamos tapando.
            req.Failure += _ => Schedule(() => open(rulesetId, onPicked, null));

            // PerformAsync y no Queue: la cola de APIAccess corre de a UNA request, asi
            // que encolar esto lo pone atras de todo lo que haya pendiente. Se vio en un
            // login real: server-pulse se colgo 9 segundos y esta consulta -que tarda
            // menos de uno- quedo esperando su turno con el jugador mirando un spinner.
            //
            // Va por afuera porque la disparo el jugador y le bloquea la pantalla. Lo que
            // corre de fondo (presencia, chat, actualizaciones) sigue en la cola, que es
            // para lo que esta.
            api.PerformAsync(req);
        }

        private void open(int rulesetId, Action onPicked, APIComfortPickFloor? pisoYaSabido)
        {
            this.onPicked = () =>
            {
                yaEligio.Add(rulesetId);
                onPicked();
            };

            slot.Child = new ComfortPickPanel(rulesetId, pisoYaSabido)
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
            // El contenido se prende de una: es el que decide si se dibuja o no, asi que
            // si entrara con fade el primer cuadro del panel llegaria tarde. El fade lo
            // hace el overlay.
            visualContent.Alpha = 1;

            this.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            panel.MoveToY(20).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.FadeOut(BriefingTheme.HoverDuration, Easing.OutQuint);

            // Al terminar el fade se apaga el contenido, que es lo que lo saca del arbol
            // de dibujo. Con AlwaysPresent en el overlay este Schedule SI corre.
            Scheduler.AddDelayed(() =>
            {
                if (State.Value == Visibility.Hidden)
                {
                    visualContent.Alpha = 0;
                    slot.Clear();
                }
            }, BriefingTheme.HoverDuration);
        }
    }
}
