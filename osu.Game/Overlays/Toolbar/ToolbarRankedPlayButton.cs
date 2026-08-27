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
using osu.Framework.Graphics.Sprites;
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
    public partial class ToolbarRankedPlayButton : OsuClickableContainer
    {
        /// <summary>
        /// El pool del que se miran las estadisticas. Hoy solo hay uno activo (osu! standard).
        /// </summary>
        private const int pool_id = 2;

        private const float pill_height = 32f;
        private const float pill_corner_radius = 12f;

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
        private OsuSpriteText liveText = null!;
        private RankedPlayQueueToast toast = null!;

        private readonly BindableInt queueCount = new BindableInt();
        private readonly BindableInt liveMatches = new BindableInt();

        private int[] lastQueueUserIds = [];

        public ToolbarRankedPlayButton()
        {
            AutoSizeAxes = Axes.X;
            Height = pill_height;
            Anchor = Anchor.CentreLeft;
            Origin = Anchor.CentreLeft;

            // Apretarla es lo mismo que ir al menu y entrar a ranked play: si ya estas
            // mirando cuanta gente hay, hacerte buscar el boton seria una burla.
            Action = () => game?.PresentRankedPlay();
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            AddRangeInternal(new Drawable[]
            {
                pillContainer = new Container
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
                            Spacing = new Vector2(7, 0),
                            Padding = new MarginPadding { Horizontal = 11 },
                            Children = new Drawable[]
                            {
                                swords = new RankedPlayCrossedSwords
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Size = new Vector2(19, 19),
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
                                        Colour = Color4.White,
                                    },
                                },
                                liveContainer = new Container
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    AutoSizeAxes = Axes.Y,
                                    Width = 0,
                                    Masking = true,
                                    Child = liveText = new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                                        Text = @"1 vs",
                                        Colour = live_green,
                                    },
                                },
                            },
                        },
                    },
                },
                // El cartelito cuelga POR FUERA de la pildora, debajo. Va en el mismo
                // drawable para que se mueva con el toolbar cuando se esconde.
                toast = new RankedPlayQueueToast
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.TopCentre,
                    Y = 6,
                },
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            queueCount.BindValueChanged(onQueueCountChanged, true);
            liveMatches.BindValueChanged(onLiveMatchesChanged, true);

            if (multiplayerClient != null)
                multiplayerClient.MatchmakingLobbyStatusChanged += onLobbyStatus;

            joinLobby();
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
                             .ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnFaulted);
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

        private void onQueueCountChanged(ValueChangedEvent<int> e)
        {
            countText.Text = e.NewValue.ToString();

            // Vacia = angostita y sin numero. El ancho se mide sobre el texto para que
            // "12" ocupe mas que "2" sin hardcodear nada.
            bool show = e.NewValue > 0;
            countContainer.ResizeWidthTo(show ? countText.DrawWidth : 0, 260, Easing.OutQuint);
            countContainer.FadeTo(show ? 1 : 0, 200, Easing.OutQuint);

            swords.SetTint(show ? ranked_orange : ranked_orange.Opacity(0.55f));
        }

        private void onLiveMatchesChanged(ValueChangedEvent<int> e)
        {
            liveText.Text = e.NewValue == 1 ? @"1 vs" : $@"{e.NewValue} vs";

            bool show = e.NewValue > 0;
            liveContainer.ResizeWidthTo(show ? liveText.DrawWidth : 0, 260, Easing.OutQuint);
            liveContainer.FadeTo(show ? 1 : 0, 200, Easing.OutQuint);
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

        protected override bool OnHover(osu.Framework.Input.Events.HoverEvent e)
        {
            hoverGlow.FadeIn(120, Easing.OutQuint);
            pillContainer.ScaleTo(1.03f, 180, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(osu.Framework.Input.Events.HoverLostEvent e)
        {
            hoverGlow.FadeOut(200, Easing.OutQuint);
            pillContainer.ScaleTo(1f, 220, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (multiplayerClient != null)
                multiplayerClient.MatchmakingLobbyStatusChanged -= onLobbyStatus;

            base.Dispose(isDisposing);
        }
    }
}
