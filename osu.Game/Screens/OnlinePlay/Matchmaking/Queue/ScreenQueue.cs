// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Audio;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Matchmaking.Requests;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Overlays;
using osu.Game.Overlays.Volume;
using osu.Game.Rulesets;
using osu.Game.Screens.Footer;
using osu.Game.Screens.OnlinePlay.Matchmaking.Match;
using osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.Queue
{
    /// <summary>
    /// The initial screen that users arrive at when preparing for a quick play session.
    /// </summary>
    public partial class ScreenQueue : OsuScreen
    {
        public override bool ShowFooter => true;

        public override bool? ApplyModTrackAdjustments => false;

        private Container mainContent = null!;
        private CloudVisualisation cloud = null!;
        private RankHeroCard rankHero = null!;
        private FillFlowContainer resultPanelContainer = null!;

        // ultimo percentil "mejor que X%" calculado del RatingDistribution; lo cachea el status y
        // lo reusa el fetch de g0v0 para no perder el dato entre updates.
        private double? lastPercentile;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private MultiplayerClient client { get; set; } = null!;

        [Resolved]
        private QueueController queue { get; set; } = null!;

        [Resolved]
        private UserLookupCache userLookupCache { get; set; } = null!;

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private MusicController music { get; set; } = null!;

        [Resolved]
        private DashboardOverlay? dashboardOverlay { get; set; }

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private readonly IBindable<MatchmakingScreenState> currentState = new Bindable<MatchmakingScreenState>();

        private readonly Bindable<MatchmakingPool[]?> availablePools = new Bindable<MatchmakingPool[]?>();
        private readonly Bindable<MatchmakingPool?> selectedPool = new Bindable<MatchmakingPool?>();

        private readonly MatchmakingPoolType poolType;

        private CancellationTokenSource userLookupCancellation = new CancellationTokenSource();

        private Sample? enqueueSample;
        private Sample? matchFoundSample;

        private SampleChannel? waitingLoopChannel;
        private ScheduledDelegate? startLoopPlaybackDelegate;
        private DrawableSample waitingLoop = null!;
        private ScheduledDelegate? pushScreenDelegate;

        private int? userRating;

        // torii: si el jugador sigue en placement (pocas partidas ranked). mientras es true el
        // badge muestra "Provisional" en vez del tier real, asi un seed fresco del star-pick no
        // se lee como "Master" sin haber jugado. lo trae g0v0 (que sabe el plays count); el
        // contrato MessagePack del spectator es NuGet-pinned y no puede cargar el flag.
        private bool userProvisional;
        private GetMatchmakingRankRequest? rankRequest;

        // torii: gate del star-rating pick. false hasta que el jugador eligio (o ya tenia) su
        // dificultad comoda de la season; mientras, el estado Idle muestra el picker en vez del boton.
        private bool comfortPickReady;

        private GridContainer mainGrid = null!;

        private IBindable<bool> isConnected = null!;

        public ScreenQueue(MatchmakingPoolType poolType)
        {
            this.poolType = poolType;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            enqueueSample = audio.Samples.Get(@"Multiplayer/Matchmaking/enqueue");
            matchFoundSample = audio.Samples.Get(@"Multiplayer/Matchmaking/match-found");

            LinkFlowContainer experimentalText;

            // torii revamp: layout de bandas (banner / hero / cuerpo) en vez del viejo grid 2x2.
            // los paneles del cuerpo se construyen como locals ACA (incondicional) para que los
            // campos (cloud, mainContent, ratingGraph, resultPanelContainer) queden asignados
            // siempre, y despues se acomodan distinto en desktop (2 columnas) vs mobile (apilado).

            // panel principal: la accion (pool selector + boton begin) sobre un backdrop del cloud de jugadores en cola.
            Drawable mainStagePanel = glass(new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    cloud = new CloudVisualisation
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.Both,
                        Size = new Vector2(0.9f),
                    },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding(12),
                        RowDimensions =
                        [
                            new Dimension(GridSizeMode.AutoSize)
                        ],
                        Content = new[]
                        {
                            new Drawable[] { new QueueSectionHeader(poolType == MatchmakingPoolType.RankedPlay ? "Ranked queue" : "Quick play queue") },
                            new Drawable[]
                            {
                                mainContent = new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Padding = new MarginPadding(20),
                                    Alpha = 0,
                                }
                            }
                        }
                    }
                }
            });

            // panel de rank: el crest grande del tier + barra de progreso + "mejor que X%".
            // reemplaza la vieja curva de distribucion (densa y poco util en la cola).
            Drawable ratingsPanel = glass(new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(12),
                RowDimensions =
                [
                    new Dimension(GridSizeMode.AutoSize)
                ],
                Content = new[]
                {
                    new Drawable[] { new QueueSectionHeader("Your rank") },
                    new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Child = rankHero = new RankHeroCard()
                        }
                    }
                }
            });

            // panel de partidas recientes.
            Drawable recentPanel = glass(new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(12) { Bottom = 0 },
                RowDimensions =
                [
                    new Dimension(GridSizeMode.AutoSize)
                ],
                Content = new[]
                {
                    new Drawable[] { new QueueSectionHeader("Recent matches") },
                    new Drawable[]
                    {
                        new OsuScrollContainer(Direction.Vertical)
                        {
                            RelativeSizeAxes = Axes.Both,
                            ScrollbarOverlapsContent = false,
                            Child = resultPanelContainer = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Spacing = new Vector2(10),
                            }
                        }
                    }
                }
            });

            // cuerpo responsive: desktop = stage grande a la izquierda + sidebar (ratings arriba, recientes abajo);
            // mobile = todo apilado en vertical para no quedar apretado.
            Drawable body = RuntimeInfo.IsMobile
                ? new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    RowDimensions =
                    [
                        new Dimension(),
                        new Dimension(GridSizeMode.Relative, 0.34f),
                        new Dimension(GridSizeMode.Relative, 0.34f)
                    ],
                    Content = new[]
                    {
                        new Drawable[] { mainStagePanel },
                        new Drawable[] { ratingsPanel },
                        new Drawable[] { recentPanel },
                    }
                }
                : new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ColumnDimensions =
                    [
                        new Dimension(GridSizeMode.Relative, 0.62f),
                        new Dimension()
                    ],
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            mainStagePanel,
                            new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                RowDimensions =
                                [
                                    new Dimension(GridSizeMode.Relative, 0.5f),
                                    new Dimension()
                                ],
                                Content = new[]
                                {
                                    new Drawable[] { ratingsPanel },
                                    new Drawable[] { recentPanel },
                                }
                            }
                        }
                    }
                };

            InternalChild = new InverseScalingDrawSizePreservingFillContainer
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    waitingLoop = new DrawableSample(audio.Samples.Get(@"Multiplayer/Matchmaking/waiting-loop")),
                    new GlobalScrollAdjustsVolume(),
                    mainGrid = new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding
                        {
                            Horizontal = 20,
                            Top = 20,
                            Bottom = ScreenFooter.HEIGHT + 20
                        },
                        RowDimensions =
                        [
                            new Dimension(GridSizeMode.AutoSize),
                            new Dimension(GridSizeMode.Absolute, RuntimeInfo.IsMobile ? 128 : 112),
                            new Dimension()
                        ],
                        Content = new[]
                        {
                            // ── banda 1: aviso de desarrollo (franja amarilla full-width) ──
                            new Drawable[]
                            {
                                new Container
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Padding = new MarginPadding(5),
                                    Child = new Container
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Masking = true,
                                        CornerRadius = 8,
                                        Children = new Drawable[]
                                        {
                                            new Box
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                Colour = colours.Yellow
                                            },
                                            experimentalText = new ExperimentalLinkFlowContainer
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                                Padding = new MarginPadding { Horizontal = 14, Vertical = 8 },
                                            }
                                        }
                                    }
                                }
                            },
                            // ── banda 2: hero con identidad (avatar + nombre) + rank badge ──
                            new Drawable[]
                            {
                                glass(new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Padding = new MarginPadding { Horizontal = 22, Vertical = 12 },
                                    Children = new Drawable[]
                                    {
                                        new FillFlowContainer
                                        {
                                            // cluster izquierdo. flow horizontal: todos los hijos con el mismo
                                            // Anchor.Y (CentreLeft) o crashea cada frame. ver [[reference_briefingglass_fillflow_crash]].
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                            AutoSizeAxes = Axes.Both,
                                            Direction = FillDirection.Horizontal,
                                            Spacing = new Vector2(16, 0),
                                            Children = new Drawable[]
                                            {
                                                new MatchmakingAvatar(api.LocalUser.Value, true)
                                                {
                                                    Anchor = Anchor.CentreLeft,
                                                    Origin = Anchor.CentreLeft,
                                                    Size = new Vector2(64),
                                                },
                                                new FillFlowContainer
                                                {
                                                    Anchor = Anchor.CentreLeft,
                                                    Origin = Anchor.CentreLeft,
                                                    AutoSizeAxes = Axes.Both,
                                                    Direction = FillDirection.Vertical,
                                                    Spacing = new Vector2(0, 3),
                                                    Children = new Drawable[]
                                                    {
                                                        new OsuSpriteText
                                                        {
                                                            Text = api.LocalUser.Value.Username,
                                                            Font = OsuFont.GetFont(size: 26, weight: FontWeight.SemiBold, typeface: Typeface.TorusAlternate),
                                                        },
                                                        new OsuSpriteText
                                                        {
                                                            Text = poolType == MatchmakingPoolType.RankedPlay ? "Ranked Play" : "Quick Play",
                                                            Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                                                            Colour = new Color4(0.66f, 0.66f, 0.72f, 1f),
                                                        },
                                                    }
                                                }
                                            }
                                        },
                                    }
                                })
                            },
                            // ── banda 3: cuerpo ──
                            new Drawable[] { body }
                        }
                    }
                }
            };

            experimentalText.AddIcon(FontAwesome.Solid.Lightbulb);
            experimentalText.AddText(@" ");
            experimentalText.AddText("This system is under continuous and rapid development.\n", sp => sp.Font = sp.Font.With(weight: FontWeight.SemiBold));
            experimentalText.AddText("Follow the ");
            experimentalText.AddLink("changelog", @"https://osu.ppy.sh/community/forums/topics/2202736", sp => sp.Font = sp.Font.With(weight: FontWeight.SemiBold));
            experimentalText.AddText(" and provide any ");
            experimentalText.AddLink("feedback", @"https://osu.ppy.sh/community/forums/topics/2198397", sp => sp.Font = sp.Font.With(weight: FontWeight.SemiBold));
            experimentalText.AddText(" on the osu! forums!");
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            int delay = 0;

            foreach (var a in mainGrid.Content)
            {
                foreach (var d in a)
                {
                    d.FadeOut()
                     .Delay(delay)
                     .FadeInFromZero(500, Easing.OutQuint);

                    delay += 100;
                }
            }

            client.MatchmakingLobbyStatusChanged += onMatchmakingLobbyStatusChanged;

            currentState.BindTo(queue.CurrentState);
            currentState.BindValueChanged(s => SetState(s.NewValue));

            selectedPool.BindTo(queue.SelectedPool);
            selectedPool.BindValueChanged(e =>
            {
                refreshLobbyData();
                fetchMatchmakingRank();
            });

            isConnected = client.IsConnected.GetBoundCopy();
            isConnected.BindValueChanged(connected => Schedule(() =>
            {
                if (connected.NewValue)
                {
                    populateAvailablePools().FireAndForget();
                    refreshLobbyData();
                }
                else
                {
                    availablePools.Value = null;
                    clearLobbyData();
                }
            }), true);
        }

        private async Task populateAvailablePools()
        {
            MatchmakingPool[] pools;

            try
            {
                pools = await client.GetMatchmakingPoolsOfType(poolType).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // torii: al entrar a la pantalla la conexion del hub puede no estar activa todavia
                // (esta (re)conectando), y GetMatchmakingPoolsOfType tira "connection is not active".
                // el bind de IsConnected vuelve a disparar esto cuando el hub conecta de verdad, asi
                // que tragamos el error transitorio en vez de spamear notificaciones de error.
                return;
            }

            Schedule(() =>
            {
                availablePools.Value = pools;

                // Default to the currently queueing pool, or fallback to the user's ruleset for the initial pool selection.
                selectedPool.Value ??= pools.FirstOrDefault(p => p.RulesetId == ruleset.Value.OnlineID) ?? pools.FirstOrDefault();
            });
        }

        private void onMatchmakingLobbyStatusChanged(MatchmakingLobbyStatus status) => Scheduler.Add(() =>
        {
            userLookupCancellation.Cancel();
            var cancellation = userLookupCancellation = new CancellationTokenSource();

            userLookupCache.GetUsersAsync(status.UsersInQueue, cancellation.Token)
                           .ContinueWith(result => Schedule(() =>
                           {
                               APIUser?[] users = result.GetResultSafely();
                               if (!cancellation.IsCancellationRequested)
                                   cloud.Users = users.OfType<APIUser>().ToArray();
                           }), cancellation.Token);

            // Global (incremental) updates will not contain the user rating, so keep the one we already received from initial status data.
            if (status.UserRating != null)
                userRating = status.UserRating;

            // percentil "mejor que X%" derivado del RatingDistribution (la unica data del viejo
            // grafico que vale la pena conservar; ahora va como una linea chica en el hero card).
            double? percentile = null;

            if (userRating != null && status.RatingDistribution.Length > 0)
            {
                int below = status.RatingDistribution.Where(d => d.Rating < userRating).Sum(d => d.Count);
                int total = status.RatingDistribution.Sum(d => d.Count);
                if (total > 0)
                    percentile = (double)below / total;
            }

            lastPercentile = percentile;
            rankHero.SetData(userRating, userProvisional, percentile);

            loadRecentMatches(status.RecentMatches.OfType<RankedPlayRoomState>().ToArray()).FireAndForget();
        });

        // torii: trae el rango de ranked play (rating + placement) de g0v0 para el badge. El
        // contrato del status (MessagePack, ppy.osu.Game NuGet-pinned en el spectator) no puede
        // cargar el flag provisional, asi que lo pedimos aparte. Cambia lento (solo cuando termina
        // el placement), asi que alcanza con refetchearlo al cambiar de pool/ruleset.
        private void fetchMatchmakingRank()
        {
            rankRequest?.Cancel();

            if (selectedPool.Value == null)
                return;

            var req = rankRequest = new GetMatchmakingRankRequest(selectedPool.Value.RulesetId);
            req.Success += rank => Schedule(() =>
            {
                if (req != rankRequest)
                    return;

                userProvisional = rank.Provisional;
                // el mu de g0v0 y el del spectator salen de la misma tabla; usamos el del status
                // como fuente viva del numero, pero si todavia no llego uno, sembramos con este.
                userRating ??= rank.Rating;
                rankHero.SetData(userRating, userProvisional, lastPercentile);
            });
            api.Queue(req);
        }

        private int historyInsertOrder;

        private async Task loadRecentMatches(RankedPlayRoomState[] matches)
        {
            // matches initial API response.
            const int max_panels = 50;

            await userLookupCache.GetUsersAsync(matches.SelectMany(m => m.Users.Keys).ToArray()).ConfigureAwait(false);

            Scheduler.Add(() =>
            {
                foreach (var match in matches)
                {
                    resultPanelContainer.Insert(historyInsertOrder--, new RankedPlayMatchPanel(match)
                    {
                        RelativeSizeAxes = Axes.X,
                        // full width: en el revamp los recientes van en un sidebar/columna angosta,
                        // asi que apilan de a uno en vez de dos por fila.
                        Width = 1f
                    });
                }

                if (resultPanelContainer.Any(c => c.Position != Vector2.Zero))
                {
                    resultPanelContainer.LayoutDuration = 400;
                    resultPanelContainer.LayoutEasing = Easing.OutQuint;
                }

                while (resultPanelContainer.Count > max_panels)
                    resultPanelContainer.Children.First().RemoveAndDisposeImmediately();
            });
        }

        private void refreshLobbyData()
        {
            clearLobbyData();

            if (selectedPool.Value == null)
            {
                // onError vacio: mismo motivo que populateAvailablePools, el hub puede estar
                // reconectando al entrar y no queremos spamear notificaciones de error.
                client.MatchmakingLeaveLobby().FireAndForget(onError: _ => { });
                return;
            }

            client.MatchmakingJoinLobbyWithParams(new MatchmakingJoinLobbyRequest
            {
                PoolId = selectedPool.Value.Id
            }).FireAndForget(onError: _ => { });
        }

        private void clearLobbyData()
        {
            resultPanelContainer.Clear();
            resultPanelContainer.LayoutDuration = 0;
            userRating = null;
            userProvisional = false;
            lastPercentile = null;
            rankHero.Clear();

            cloud.Users = Array.Empty<APIUser>();
        }

        public override void OnEntering(ScreenTransitionEvent e)
        {
            base.OnEntering(e);

            queue.SearchInForeground();

            using (BeginDelayedSequence(800))
                Schedule(() => SetState(currentState.Value));
        }

        public override void OnResuming(ScreenTransitionEvent e)
        {
            base.OnResuming(e);

            // Rejoin the lobby.
            selectedPool.TriggerChange();
        }

        public override void OnSuspending(ScreenTransitionEvent e)
        {
            base.OnSuspending(e);

            stopWaitingLoopPlayback();
            client.MatchmakingLeaveLobby().FireAndForget();
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            if (base.OnExiting(e))
                return true;

            stopWaitingLoopPlayback();

            switch (currentState.Value)
            {
                default:
                    client.MatchmakingLeaveLobby().FireAndForget();
                    queue.SearchInBackground();
                    return false;

                case MatchmakingScreenState.PendingAccept:
                case MatchmakingScreenState.AcceptedWaitingForRoom:
                    queue.LeaveQueue();
                    return true;

                case MatchmakingScreenState.InRoom:
                    // Block exit until it's initiated from inside the matchmaking screen.
                    return true;
            }
        }

        public void SetState(MatchmakingScreenState newState)
        {
            mainContent.FadeInFromZero(500, Easing.OutQuint);
            mainContent.Clear();

            startLoopPlaybackDelegate?.Cancel();
            stopWaitingLoopPlayback();

            pushScreenDelegate?.Cancel();
            pushScreenDelegate = null;

            switch (newState)
            {
                case MatchmakingScreenState.Idle:
                    // torii: gate del star-rating pick. Hasta que no eligas tu dificultad comoda de la
                    // season, mostramos el picker en vez del boton de queue. OnReady se dispara al elegir
                    // (o si ya elegiste) y re-entra a Idle para mostrar el flujo normal.
                    if (!comfortPickReady)
                    {
                        mainContent.Child = new ComfortPickPanel(ruleset.Value.OnlineID)
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Width = 0.9f,
                            OnReady = () =>
                            {
                                comfortPickReady = true;
                                // deferir a un frame limpio: SetState hace mainContent.Clear(), que
                                // dispone este ComfortPickPanel. Si lo disponemos mientras corre su
                                // propio callback (el OnReady sale de ahi), revienta con
                                // ObjectDisposedException en el update siguiente.
                                if (currentState.Value == MatchmakingScreenState.Idle)
                                    Schedule(() => SetState(MatchmakingScreenState.Idle));
                            },
                        };
                        break;
                    }

                    LinkFlowContainer duelHint;

                    mainContent.Child = new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(10),
                        Children = new Drawable[]
                        {
                            new PoolSelector
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                AvailablePools = { BindTarget = availablePools },
                                SelectedPool = { BindTarget = selectedPool }
                            },
                            new BeginQueueingButton
                            {
                                DarkerColour = colours.Blue2,
                                LighterColour = colours.Blue1,
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Width = 200,
                                Enabled = { BindTarget = isConnected },
                                SelectedPool = { BindTarget = selectedPool },
                                Action = () =>
                                {
                                    Debug.Assert(selectedPool.Value != null);
                                    queue.JoinQueue(selectedPool.Value);
                                },
                                Text = "Begin queueing",
                            },
                            duelHint = new LinkFlowContainer
                            {
                                TextAnchor = Anchor.TopCentre,
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                            }
                        }
                    };

                    duelHint.AddText("Open the ");
                    duelHint.AddLink("dashboard", () => dashboardOverlay?.Show());
                    duelHint.AddText(" to duel another player!");

                    break;

                case MatchmakingScreenState.Queueing:
                    mainContent.Child = new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(15),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Text = "Waiting for a game...",
                                Font = OsuFont.GetFont(size: 32, weight: FontWeight.Light, typeface: Typeface.TorusAlternate),
                            },
                            new LoadingSpinner
                            {
                                State = { Value = Visibility.Visible },
                            },
                            new ShearedButton
                            {
                                DarkerColour = colours.Red3,
                                LighterColour = colours.Red4,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Width = 200,
                                Text = "Stop queueing",
                                Action = () => queue.LeaveQueue()
                            }
                        }
                    };

                    enqueueSample?.Play();
                    startLoopPlaybackDelegate = Scheduler.AddDelayed(startWaitingLoopPlayback, 2000);
                    break;

                case MatchmakingScreenState.PendingAccept:
                    client.MatchmakingAcceptInvitation().FireAndForget();
                    SetState(MatchmakingScreenState.AcceptedWaitingForRoom);

                    matchFoundSample?.Play();
                    music.DuckMomentarily(1250);
                    break;

                case MatchmakingScreenState.AcceptedWaitingForRoom:
                    mainContent.Child = new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(20),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Text = "Waiting for opponents...",
                                Font = OsuFont.GetFont(size: 32, weight: FontWeight.Light, typeface: Typeface.TorusAlternate),
                            },
                            new LoadingSpinner
                            {
                                State = { Value = Visibility.Visible },
                            },
                        }
                    };

                    startWaitingLoopPlayback();
                    break;

                case MatchmakingScreenState.InRoom:
                    // room received, show users and transition to next screen.
                    mainContent.Child = new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(20),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Text = "Good luck!",
                                Font = OsuFont.GetFont(size: 32, weight: FontWeight.Light, typeface: Typeface.TorusAlternate),
                            },
                        }
                    };

                    using (BeginDelayedSequence(2000))
                    {
                        pushScreenDelegate = Schedule(() =>
                        {
                            // torii: durante los 2s de "Good luck!" la room se puede caer (el rival se
                            // va, el match aborta, se corta la conexion). si client.Room quedo null y
                            // empujamos igual, el ctor de la screen NREa en room.Settings y la excepcion
                            // no manejada hard-lockea el juego (ni alt-F4 cierra). guardamos y volvemos
                            // a la cola en vez de romper.
                            var room = client.Room;

                            if (room == null)
                            {
                                SetState(MatchmakingScreenState.Idle);
                                return;
                            }

                            switch (poolType)
                            {
                                case MatchmakingPoolType.QuickPlay:
                                    this.Push(new ScreenMatchmaking(room));
                                    break;

                                case MatchmakingPoolType.RankedPlay:
                                    this.Push(new RankedPlayScreen(room));
                                    break;
                            }
                        });
                    }

                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(newState), newState, null);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            stopWaitingLoopPlayback();

            rankRequest?.Cancel();

            if (client.IsNotNull())
                client.MatchmakingLobbyStatusChanged -= onMatchmakingLobbyStatusChanged;
        }

        public enum MatchmakingScreenState
        {
            Idle,
            Queueing,
            PendingAccept,
            AcceptedWaitingForRoom,
            InRoom
        }

        private void startWaitingLoopPlayback()
        {
            stopWaitingLoopPlayback();

            waitingLoopChannel = waitingLoop.GetChannel();
            if (waitingLoopChannel == null)
                return;

            waitingLoopChannel.Looping = true;
            waitingLoopChannel?.Play();

            waitingLoop.VolumeTo(1)
                       .Delay(2000)
                       .VolumeTo(0, 12000);
        }

        private void stopWaitingLoopPlayback()
        {
            waitingLoopChannel?.Stop();
            waitingLoopChannel?.Dispose();
        }

        /// <summary>
        /// torii: envuelve un contenido en un panel dark-glass redondeado (fondo <see cref="PanelBackground"/>
        /// + masking). se usa para cada panel del revamp del queue.
        /// </summary>
        private static Container glass(Drawable content) => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Padding = new MarginPadding(5),
            Child = new Container
            {
                RelativeSizeAxes = Axes.Both,
                CornerRadius = 12f,
                Masking = true,
                Children = new Drawable[] { new PanelBackground(), content },
            },
        };

        public partial class PanelBackground : CompositeDrawable
        {
            [Resolved]
            private OverlayColourProvider colourProvider { get; set; } = null!;

            [BackgroundDependencyLoader]
            private void load()
            {
                // torii: panel dark-glass. base oscura semi-transparente + tinte del tema +
                // un highlight sutil arriba (brillo de vidrio) + borde tenue. el parent ya
                // maskea con corner radius 12, replicamos aca para que el borde acompanie.
                RelativeSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 12f;
                BorderThickness = 1.5f;
                BorderColour = new Color4(1f, 1f, 1f, 0.08f);

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(0.05f, 0.06f, 0.08f, 0.55f),
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colourProvider.Background3,
                        Blending = BlendingParameters.Additive,
                        Alpha = 0.22f,
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Height = 0.5f,
                        Colour = ColourInfo.GradientVertical(new Color4(1f, 1f, 1f, 0.05f), new Color4(1f, 1f, 1f, 0f)),
                        Blending = BlendingParameters.Additive,
                    },
                };
            }
        }

        public partial class QueueSectionHeader : SectionHeader
        {
            public QueueSectionHeader(string header)
                : base(header)
            {
                // Reduce base class padding.
                Margin = new MarginPadding { Top = 5, Bottom = 10, Horizontal = 5 };
            }
        }

        private partial class BeginQueueingButton : SelectionButton
        {
            public readonly IBindable<MatchmakingPool?> SelectedPool = new Bindable<MatchmakingPool?>();

            protected override void LoadComplete()
            {
                base.LoadComplete();

                SelectedPool.BindValueChanged(p => Enabled.Value = p.NewValue != null, true);
            }
        }

        private partial class SelectionButton : ShearedButton, IKeyBindingHandler<GlobalAction>
        {
            public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
            {
                if (e.Action == GlobalAction.Select && !e.Repeat)
                {
                    TriggerClickWithSound();
                    return true;
                }

                return false;
            }

            public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
            {
            }
        }

        private partial class ExperimentalLinkFlowContainer : LinkFlowContainer
        {
            public ExperimentalLinkFlowContainer()
                : base(sp => sp.Colour = Color4.Black)
            {
            }

            protected override DrawableLinkCompiler CreateLinkCompiler(ITextPart textPart)
                => new LinkCompiler(textPart);

            private partial class LinkCompiler : DrawableLinkCompiler
            {
                public LinkCompiler(ITextPart part)
                    : base(part)
                {
                }

                public LinkCompiler(IEnumerable<Drawable> parts)
                    : base(parts)
                {
                }

                [BackgroundDependencyLoader]
                private void load(OsuColour colours)
                {
                    IdleColour = colours.YellowDarker;
                    HoverColour = Color4.Black;
                }
            }
        }
    }
}
