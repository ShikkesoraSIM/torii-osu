// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Logging;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Matchmaking.Requests;
using osu.Game.Online.Multiplayer;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>
    /// La pildora de ranked play: cuanta gente hay en la cola y si hay partida en curso.
    /// </summary>
    /// <remarks>
    /// Existe para que ranked play se vea VIVO desde el menu principal. El problema que
    /// resuelve es de arranque en frio: nadie entra a la cola porque nunca hay nadie, y
    /// nunca hay nadie porque nadie entra. Si desde el toolbar se ve que hay dos esperando,
    /// entrar deja de ser una apuesta.
    ///
    /// Tres estados, de menos a mas ruidoso:
    ///   vacia   -> solo las espadas, angostita, sin numero (no molesta)
    ///   con cola-> se agranda y muestra el numero
    ///   partida -> ademas aparece el marcador verde de partida en curso
    ///
    /// El dato sale del mismo <see cref="MatchmakingLobbyStatus"/> que ya usa la pantalla
    /// de cola. Para recibirlo hay que estar en el grupo del lobby, asi que la pildora se
    /// une sola al conectarse: es solo una suscripcion, no te mete en la cola ni te muestra
    /// como disponible.
    /// </remarks>
    public partial class ToolbarRankedPlayButton : OsuClickableContainer, IHasTooltip
    {
        /// <summary>
        /// El pool del que se miran las estadisticas. Hoy solo hay uno activo (osu! standard).
        /// </summary>
        private const int pool_id = 2;

        private const float pill_height = 32f;
        private const float pill_corner_radius = 12f;

        /// <summary>Aire entre el icono y lo que venga a su derecha.</summary>
        private const float gap = 7f;

        private static readonly Color4 ranked_orange = new Color4(255, 146, 43, 255);
        private static readonly Color4 live_green = new Color4(86, 227, 128, 255);

        [Resolved]
        private MultiplayerClient? multiplayerClient { get; set; }

        [Resolved]
        private OsuGame? game { get; set; }

        private Container pillContainer = null!;
        private Box hoverGlow = null!;
        private RankedPlayCrossedSwords swords = null!;
        private Container countContainer = null!;
        private OsuSpriteText countText = null!;
        private Container liveContainer = null!;
        private RankedPlayLiveDot liveDot = null!;
        private RankedPlayQueueToast toast = null!;
        private RankedPlayPopover? popover;

        private readonly BindableInt queueCount = new BindableInt();
        private readonly BindableInt liveMatches = new BindableInt();

        private int[] lastQueueUserIds = [];

        public ToolbarRankedPlayButton()
        {
            AutoSizeAxes = Axes.X;
            Height = pill_height;
            Anchor = Anchor.CentreLeft;
            Origin = Anchor.CentreLeft;

            Action = togglePopover;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // Child y no AddInternal: OsuClickableContainer solo acepta clics que caen
            // dentro de su Content (lo chequea explicitamente en
            // ReceivePositionalInputAt), y AddInternal deja el dibujo AFUERA de Content.
            // Con Content vacio la pildora se veia pero no se podia clickear.
            Child = pillContainer = new Container
                {
                    AutoSizeAxes = Axes.X,
                    RelativeSizeAxes = Axes.Y,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Masking = true,
                    CornerRadius = pill_corner_radius,
                    CornerExponent = 2.4f,
                    MaskingSmoothness = 1.4f,
                    BorderThickness = 1f,
                    BorderColour = ranked_orange.Opacity(0.45f),
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Shadow,
                        Radius = 8,
                        Roundness = 6,
                        Colour = ranked_orange.Opacity(0.18f),
                        Offset = new Vector2(0, 1),
                    },
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            // Base oscura con sesgo calido, hermana de la del pulse.
                            Colour = new Color4(30, 21, 13, 230),
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = ranked_orange.Opacity(0.10f),
                            Blending = BlendingParameters.Additive,
                        },
                        hoverGlow = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = ranked_orange.Opacity(0.18f),
                            Blending = BlendingParameters.Additive,
                            Alpha = 0,
                        },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            // Spacing en 0: con espacio de flow, los dos contenedores de
                            // ancho 0 (numero y punto) seguian dejando su separacion a la
                            // derecha del icono y la pildora vacia quedaba descentrada. El
                            // aire va DENTRO del ancho que se anima, asi desaparece con ellos.
                            Spacing = new Vector2(0, 0),
                            Padding = new MarginPadding { Horizontal = 10 },
                            Children = new Drawable[]
                            {
                                swords = new RankedPlayCrossedSwords
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Size = new Vector2(22, 22),
                                },
                                // Los dos de abajo viven siempre pero arrancan en ancho 0.
                                // Animar el ancho (y no aparecer/desaparecer) es lo que hace
                                // que la pildora "crezca" en vez de saltar.
                                countContainer = new Container
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    AutoSizeAxes = Axes.Y,
                                    Width = 0,
                                    Masking = true,
                                    Child = countText = new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                                        Text = @"0",
                                        Margin = new MarginPadding { Left = gap },
                                        Colour = Color4.White,
                                    },
                                },
                                liveContainer = new Container
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    // Altura FIJA y no auto-size: un contenedor que se mide
                                    // por sus hijos, con el hijo anclado al centro de ese
                                    // mismo contenedor, es circular y termina midiendo
                                    // cualquier cosa. Con RelativeSizeAxes.Y (el primer
                                    // intento) directamente daba 0 y la pastilla, que si
                                    // recorta, se comia el punto: el tooltip decia
                                    // "1 match ongoing" y no se veia nada.
                                    Height = 9,
                                    Width = 0,
                                    // Siempre presente aunque mida 0 y este transparente:
                                    // un drawable que no esta presente no actualiza NADA
                                    // suyo, ni transiciones ni scheduler. Ese era el
                                    // motivo de que el punto no apareciera nunca: la
                                    // toolbar se auto-esconde, el subarbol dejaba de
                                    // actualizarse y el fade de entrada quedaba clavado
                                    // en el valor inicial.
                                    AlwaysPresent = true,
                                    Alpha = 0,
                                    // SIN Masking: el punto tiene un halo que late hasta 2.2x
                                    // su tamaño, y recortandolo al ancho del contenedor
                                    // quedaba cuadrado de los costados. Lo que reserva el
                                    // lugar en la fila es el ancho; que el halo se dibuje
                                    // afuera no molesta a nadie.
                                    Child = liveDot = new RankedPlayLiveDot
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Size = new Vector2(7, 7),
                                        Margin = new MarginPadding { Left = gap },
                                    },
                                },
                            },
                        },
                    },
            };

            // El cartelito va por AddInternal (fuera de Content) a proposito: adentro
            // agrandaria el area clickeable de la pildora hasta cubrirlo.
            AddInternal(toast = new RankedPlayQueueToast
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.TopCentre,
                Y = 6,
                // La pildora mide por auto-size, asi que TODO lo que cuelgue de ella
                // le agranda la caja aunque sea invisible (AlwaysPresent no lo saca de
                // la medicion, solo lo mantiene vivo). Sin esto el cartelito, que es
                // mas ancho que la pildora, le abria un hueco enorme en el toolbar.
                BypassAutoSizeAxes = Axes.Both,
            });
        }

        /// <summary>Si el panel esta abierto. Lo mira el auto-hide del toolbar.</summary>
        public bool PopoverVisible => popover?.State.Value == Visibility.Visible;

        private void togglePopover()
        {
            // El panel se precarga async, pero NO se actualiza de fondo: construirlo
            // es barato y una sola vez, mientras que mantenerlo al dia seria hacer
            // trabajar al juego permanentemente por algo que se mira unos segundos.
            // Los datos los pide el panel al abrirse (ver RankedPlayPopover).
            if (popover == null || !popover.IsLoaded)
                return;

            if (popover.State.Value == Visibility.Visible)
            {
                popover.Hide();
                return;
            }

            popover.QueueUserIds = lastQueueUserIds;
            popover.Show();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Precargado fuera del hilo de UI: armarlo en el primer click congelaria
            // el juego un instante justo cuando el jugador espera respuesta.
            popover = new RankedPlayPopover
            {
                PoolId = pool_id,
                OnQueueRequested = () =>
                {
                    popover?.Hide();
                    game?.PresentRankedPlay();
                },
            };

            LoadComponentAsync(popover, p =>
            {
                // Mismo motivo que el cartelito, y peor: el panel mide 320px de ancho
                // y colgaba de una pildora que mide por auto-size. Ese era el hueco
                // gigante entre el pulse y el reloj.
                p.BypassAutoSizeAxes = Axes.Both;
                AddInternal(p);
                p.AnchoredAt = this;
            });

            queueCount.BindValueChanged(onQueueCountChanged, true);
            liveMatches.BindValueChanged(onLiveMatchesChanged, true);

            if (multiplayerClient != null)
            {
                multiplayerClient.MatchmakingLobbyStatusChanged += onLobbyStatus;
                multiplayerClient.RankedPlayLiveMatchCountReceived += onLiveMatchCount;

                // Atado a IsConnected y no llamado una sola vez: en LoadComplete el hub
                // casi nunca esta conectado todavia (el toolbar se arma antes que la
                // conexion), asi que el join se perdia y la pildora se quedaba en cero
                // para siempre. Ademas asi se vuelve a unir sola despues de una
                // reconexion, donde el server ya nos saco del grupo.
                multiplayerClient.IsConnected.BindValueChanged(c =>
                {
                    if (c.NewValue)
                        joinLobby();
                    else
                    {
                        // Sin conexion no se sabe nada, y dejar el ultimo numero puesto
                        // seria mentir. Se vuelve al estado vacio.
                        lastQueueUserIds = [];
                        queueCount.Value = 0;
                        liveMatches.Value = 0;
                    }
                }, true);
            }
        }

        /// <summary>
        /// Se une al grupo del lobby para recibir el estado. Es solo una suscripcion.
        /// </summary>
        private void joinLobby()
        {
            if (multiplayerClient == null)
                return;

            // Si falla no pasa nada: la pildora se queda en cero, que es como se veia
            // antes de existir. No vale la pena molestar al jugador por esto.
            multiplayerClient.MatchmakingJoinLobbyWithParams(new MatchmakingJoinLobbyRequest { PoolId = pool_id })
                             .ContinueWith(t =>
                             {
                                 Logger.Log(t.IsFaulted
                                     ? $@"[RankedPlay] lobby join failed: {t.Exception?.GetBaseException().Message}"
                                     : @"[RankedPlay] joined matchmaking lobby", LoggingTarget.Runtime, LogLevel.Verbose);
                             }, TaskScheduler.Default);
        }

        private void onLobbyStatus(MatchmakingLobbyStatus status)
        {
            int[] ids = status.UsersInQueue ?? [];

            // Quien entro DESDE LA ULTIMA VEZ. Se compara por id y no por cantidad
            // porque si uno entra y otro sale al mismo tiempo el numero no cambia y
            // el que entro igual merece su cartelito.
            int[] joined = ids.Except(lastQueueUserIds).ToArray();
            lastQueueUserIds = ids;

            queueCount.Value = ids.Length;

            if (joined.Length > 0)
            {
                swords.Clash(Math.Min(1f, 0.4f + joined.Length * 0.3f));
                toast.AnnounceJoined(joined);
            }
        }

        private void onLiveMatchCount(int poolId, int count)
        {
            if (poolId != pool_id)
                return;

            liveMatches.Value = count;
        }

        private void onQueueCountChanged(ValueChangedEvent<int> e)
        {
            countText.Text = e.NewValue.ToString();

            // Vacia = angostita y sin numero. El ancho se mide sobre el texto para que
            // "12" ocupe mas que "2" sin hardcodear nada.
            bool show = e.NewValue > 0;
            countContainer.ResizeWidthTo(show ? countText.DrawWidth + gap : 0, 260, Easing.OutQuint);
            countContainer.FadeTo(show ? 1 : 0, 200, Easing.OutQuint);

            swords.SetTint(show ? ranked_orange : ranked_orange.Opacity(0.55f));
        }

        /// <summary>
        /// Se calcula al leerlo y no se guarda en un campo: el tooltip lee esto UNA vez
        /// cuando aparece, asi que un valor cacheado se queda con el numero de hace un
        /// rato. Paso: la pildora decia 0 y el tooltip seguia diciendo "1 player in
        /// queue".
        /// </summary>
        public LocalisableString TooltipText => describeState();

        /// <summary>
        /// Lo que la pildora NO dibuja. El numero de la cola se ve y el punto verde
        /// avisa que hay partida; aca se aclara que es cada cosa y cuantas partidas hay.
        /// </summary>
        private LocalisableString describeState()
        {
            int q = queueCount.Value;
            int m = liveMatches.Value;

            string queuePart = q == 0 ? @"Nobody in queue"
                : q == 1 ? @"1 player in queue"
                : $@"{q} players in queue";

            if (m == 0)
                return queuePart;

            return $"{queuePart}  ·  {(m == 1 ? "1 match ongoing" : $"{m} matches ongoing")}";
        }

        private void onLiveMatchesChanged(ValueChangedEvent<int> e)
        {
            // Cuantas partidas hay exactamente no se dibuja: el punto solo dice "hay
            // algo pasando". El numero vive en el tooltip, que es donde va la
            // curiosidad. Ver RankedPlayLiveDot.
            bool show = e.NewValue > 0;

            // Asignado y no animado a proposito. Con ResizeWidthTo/FadeTo el punto no
            // aparecia NUNCA: la toolbar se auto-esconde, y mientras esta escondida el
            // subarbol no se actualiza, asi que las transiciones no avanzan y se
            // quedaban en el valor inicial (ancho 0, alfa 0) para siempre. Medido: el
            // handler corria con show=true y 1,2s despues el contenedor seguia en 0.
            // Una transicion sirve para adornar algo que igual pasa, no para decidir si
            // pasa.
            liveContainer.Width = show ? 9 + gap : 0;
            liveContainer.Alpha = show ? 1 : 0;
        }

        /// <summary>
        /// Para las test scenes: empujar estado sin un server atras.
        /// </summary>
        public void SetStateForTesting(int inQueue, int live, string[]? joinedNames = null)
        {
            queueCount.Value = inQueue;
            liveMatches.Value = live;

            if (joinedNames?.Length > 0)
            {
                swords.Clash(MathF.Min(1f, 0.4f + joinedNames.Length * 0.3f));
                toast.AnnounceJoined(joinedNames);
            }
        }

        /// <summary>
        /// Para las test scenes: abrir el panel con partidas inventadas, sin server.
        /// </summary>
        public void OpenPopoverForTesting(RankedPlayLiveMatch[]? fakeMatches = null)
        {
            if (popover == null || !popover.IsLoaded)
                return;

            popover.LiveMatchesOverride = fakeMatches;
            popover.QueueUserIds = lastQueueUserIds;
            popover.Show();
        }

        protected override bool OnHover(osu.Framework.Input.Events.HoverEvent e)
        {
            // Mismos valores y tiempos que ToolbarPointsButton para que las tres
            // pildoras se sientan una sola cosa. Se escala THIS y no el contenedor de
            // adentro, igual que ellas.
            hoverGlow.FadeTo(0.30f, 200, Easing.OutQuint);
            this.ScaleTo(1.04f, 200, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(osu.Framework.Input.Events.HoverLostEvent e)
        {
            hoverGlow.FadeTo(0f, 280, Easing.OutQuint);
            this.ScaleTo(1f, 280, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (multiplayerClient != null)
            {
                multiplayerClient.MatchmakingLobbyStatusChanged -= onLobbyStatus;
                multiplayerClient.RankedPlayLiveMatchCountReceived -= onLiveMatchCount;
            }

            base.Dispose(isDisposing);
        }
    }
}
