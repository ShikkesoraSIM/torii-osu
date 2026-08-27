// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Logging;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Multiplayer;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>
    /// El panel de ranked play que cuelga de la pildora del toolbar.
    /// </summary>
    /// <remarks>
    /// REGLA DE ORO DE ESTE PANEL: mientras esta cerrado no hace absolutamente nada.
    /// No escucha eventos, no pide datos, no resuelve nombres, no baja avatares. Todo
    /// eso arranca al abrirlo y se corta al cerrarlo.
    ///
    /// La tentacion es la contraria — mantenerlo actualizado de fondo para que abra
    /// instantaneo — y es justo lo que no hay que hacer: significa que el juego de
    /// TODOS trabaja permanentemente solo porque hay gente jugando ranked, cuando el
    /// panel se abre unos segundos al dia. Se prefiere una pantallita de carga de
    /// medio segundo antes que un impuesto constante sobre el update thread.
    ///
    /// Por lo mismo las partidas en curso se PIDEN (RankedPlayGetLiveMatches) en vez
    /// de llegar por broadcast.
    /// </remarks>
    public partial class RankedPlayPopover : VisibilityContainer
    {
        private const float panel_width = 320;
        private static readonly Color4 ranked_orange = new Color4(255, 146, 43, 255);
        private static readonly Color4 live_green = new Color4(86, 227, 128, 255);

        /// <summary>El pool del que se listan cola y partidas.</summary>
        public int PoolId { get; set; } = 2;

        /// <summary>
        /// Ids de la gente en cola. Lo pasa la pildora al abrir: ya los tiene, no
        /// tiene sentido que el panel los pida de nuevo.
        /// </summary>
        public int[] QueueUserIds { get; set; } = [];

        /// <summary>Que hacer cuando aprietan "Queue". Lo cablea la pildora.</summary>
        public Action? OnQueueRequested { get; set; }

        /// <summary>El boton del toolbar, para colgarse debajo.</summary>
        public Drawable? AnchoredAt { get; set; }

        [Resolved]
        private MultiplayerClient? multiplayerClient { get; set; }

        [Resolved]
        private UserLookupCache? userLookup { get; set; }

        private Container body = null!;
        private FillFlowContainer content = null!;
        private LoadingSpinner spinner = null!;
        private FillFlowContainer sections = null!;

        private CancellationTokenSource? loadCancellation;

        public RankedPlayPopover()
        {
            Width = panel_width;
            AutoSizeAxes = Axes.Y;
            Alpha = 0;
            AlwaysPresent = true;

            Anchor = Anchor.TopLeft;
            Origin = Anchor.TopCentre;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = body = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = 14,
                CornerExponent = 2.4f,
                BorderThickness = 1f,
                BorderColour = ranked_orange.Opacity(0.4f),
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Radius = 16,
                    Roundness = 8,
                    Colour = Color4.Black.Opacity(0.5f),
                    Offset = new Vector2(0, 3),
                },
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(26, 19, 13, 245),
                    },
                    content = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding(14),
                        Spacing = new Vector2(0, 12),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold),
                                Text = @"Ranked play",
                                Colour = Color4.White,
                            },
                            new RankedPlayQueueActionButton
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 36,
                                Action = () => OnQueueRequested?.Invoke(),
                            },
                            // El bloque que cambia. Arranca vacio y se llena al abrir.
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Children = new Drawable[]
                                {
                                    sections = new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, 12),
                                    },
                                    spinner = new LoadingSpinner(true)
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        Margin = new MarginPadding { Vertical = 20 },
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        protected override void Update()
        {
            base.Update();

            // Colgado debajo del boton, centrado en el. Se recalcula cada frame porque
            // el toolbar se mueve (se esconde y vuelve) y el ancho de la pildora cambia
            // cuando entra gente a la cola.
            if (AnchoredAt != null)
            {
                var pos = Parent!.ToLocalSpace(AnchoredAt.ScreenSpaceDrawQuad.BottomLeft);
                Position = new Vector2(pos.X + AnchoredAt.DrawWidth / 2, pos.Y + 6);
            }
        }

        protected override void PopIn()
        {
            this.FadeIn(150, Easing.OutQuint);
            body.MoveToY(-8).MoveToY(0, 300, Easing.OutQuint);
            body.ScaleTo(0.96f).ScaleTo(1f, 300, Easing.OutQuint);

            beginLoad();
        }

        protected override void PopOut()
        {
            this.FadeOut(150, Easing.OutQuint);
            body.MoveToY(-6, 200, Easing.OutQuint);

            // Cortar lo que este en vuelo y soltar el contenido. Un panel cerrado no
            // tiene por que seguir teniendo avatares en memoria ni requests abiertos.
            loadCancellation?.Cancel();
            loadCancellation = null;
            sections.Clear();
        }

        private void beginLoad()
        {
            loadCancellation?.Cancel();
            loadCancellation = new CancellationTokenSource();

            sections.Clear();
            spinner.Show();

            loadEverything(loadCancellation.Token);
        }

        private async void loadEverything(CancellationToken token)
        {
            List<Drawable> built = new List<Drawable>();

            try
            {
                // Las dos cosas en paralelo: son independientes y esperar una atras de
                // la otra duplicaria la pantalla de carga sin ganar nada.
                var namesTask = resolveQueueNames(token);
                var matchesTask = fetchLiveMatches(token);

                await Task.WhenAll(namesTask, matchesTask).ConfigureAwait(false);

                if (token.IsCancellationRequested)
                    return;

                string[] names = await namesTask.ConfigureAwait(false);
                RankedPlayLiveMatch[] matches = await matchesTask.ConfigureAwait(false);

                built.Add(new QueueSection(names));

                if (matches.Length > 0)
                    built.Add(new LiveMatchesSection(matches));
            }
            catch (Exception e)
            {
                Logger.Log($@"[RankedPlay] popover load failed: {e.Message}", LoggingTarget.Runtime, LogLevel.Verbose);
                built.Clear();
                built.Add(new EmptyNote(@"Could not load right now."));
            }

            Schedule(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                spinner.Hide();
                sections.ChildrenEnumerable = built;
            });
        }

        private async Task<string[]> resolveQueueNames(CancellationToken token)
        {
            if (QueueUserIds.Length == 0 || userLookup == null)
                return [];

            // Como mucho ocho: la lista es para dar una idea de quien esta esperando,
            // no un censo. Traer cincuenta avatares para un panel de 320px de ancho
            // seria pagar por algo que no se ve.
            int[] ids = QueueUserIds.Take(8).ToArray();

            var users = await userLookup.GetUsersAsync(ids, token).ConfigureAwait(false);

            return users?.Where(u => u != null && !string.IsNullOrEmpty(u.Username))
                        .Select(u => u!.Username)
                        .ToArray() ?? [];
        }

        /// <summary>
        /// Partidas de mentira para las test scenes. Sin esto, ver la parte mas
        /// complicada del panel (dos jugadores, sus barras de vida, el mapa) exigiria
        /// que haya una partida real pasando en prod justo cuando la estas mirando.
        /// </summary>
        public RankedPlayLiveMatch[]? LiveMatchesOverride { get; set; }

        private async Task<RankedPlayLiveMatch[]> fetchLiveMatches(CancellationToken token)
        {
            if (LiveMatchesOverride != null)
                return LiveMatchesOverride;

            if (multiplayerClient == null)
                return [];

            string json = await multiplayerClient.RankedPlayGetLiveMatches(PoolId).ConfigureAwait(false);

            if (token.IsCancellationRequested || string.IsNullOrEmpty(json))
                return [];

            // Si el server es viejo y no conoce el metodo, o manda algo raro, se
            // devuelve vacio: el panel muestra la cola igual. Un server sin esta
            // funcion no tiene por que romper el resto del panel.
            try
            {
                return JsonConvert.DeserializeObject<RankedPlayLiveMatch[]>(json) ?? [];
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }
}
