// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osu.Game.Online.Matchmaking;
using osu.Game.Online.Matchmaking.Requests;
using osu.Game.Online.Matchmaking.Responses;
using osu.Game.Online.Multiplayer.MatchTypes.RankedPlay;
using osu.Game.Online.RankedPlay;
using osu.Game.Online.Rooms;
using osu.Game.Overlays.Notifications;
using osuTK;

namespace osu.Game.Online.Multiplayer
{
    /// <summary>
    /// A <see cref="MultiplayerClient"/> with online connectivity.
    /// </summary>
    public partial class OnlineMultiplayerClient : MultiplayerClient
    {
        private readonly string endpoint;

        private IHubClientConnector? connector;

        public override IBindable<bool> IsConnected { get; } = new BindableBool();

        private HubConnection? connection => connector?.CurrentConnection;

        public OnlineMultiplayerClient(EndpointConfiguration endpoints)
        {
            endpoint = endpoints.MultiplayerUrl;
        }

        [BackgroundDependencyLoader]
        private void load(IAPIProvider api)
        {
            // Importantly, we are intentionally not using MessagePack here to correctly support derived class serialization.
            // More information on the limitations / reasoning can be found in osu-server-spectator's initialisation code.
            connector = api.GetHubConnector(nameof(OnlineMultiplayerClient), endpoint);

            if (connector != null)
            {
                connector.ConfigureConnection = connection =>
                {
                    // this is kind of SILLY
                    // https://github.com/dotnet/aspnetcore/issues/15198
                    connection.On<MultiplayerRoomState>(nameof(IMultiplayerClient.RoomStateChanged), ((IMultiplayerClient)this).RoomStateChanged);
                    connection.On<MultiplayerRoomUser>(nameof(IMultiplayerClient.UserJoined), ((IMultiplayerClient)this).UserJoined);
                    connection.On<MultiplayerRoomUser>(nameof(IMultiplayerClient.UserLeft), ((IMultiplayerClient)this).UserLeft);
                    connection.On<MultiplayerRoomUser>(nameof(IMultiplayerClient.UserKicked), ((IMultiplayerClient)this).UserKicked);
                    connection.On<int, long, string>(nameof(IMultiplayerClient.Invited), ((IMultiplayerClient)this).Invited);
                    connection.On<int>(nameof(IMultiplayerClient.HostChanged), ((IMultiplayerClient)this).HostChanged);
                    connection.On<MultiplayerRoomSettings>(nameof(IMultiplayerClient.SettingsChanged), ((IMultiplayerClient)this).SettingsChanged);
                    connection.On<int, MultiplayerUserState>(nameof(IMultiplayerClient.UserStateChanged), ((IMultiplayerClient)this).UserStateChanged);
                    connection.On(nameof(IMultiplayerClient.LoadRequested), ((IMultiplayerClient)this).LoadRequested);
                    connection.On(nameof(IMultiplayerClient.GameplayStarted), ((IMultiplayerClient)this).GameplayStarted);
                    connection.On<GameplayAbortReason>(nameof(IMultiplayerClient.GameplayAborted), ((IMultiplayerClient)this).GameplayAborted);
                    connection.On(nameof(IMultiplayerClient.ResultsReady), ((IMultiplayerClient)this).ResultsReady);
                    connection.On<int, int?, int?>(nameof(IMultiplayerClient.UserStyleChanged), ((IMultiplayerClient)this).UserStyleChanged);
                    connection.On<int, IEnumerable<APIMod>>(nameof(IMultiplayerClient.UserModsChanged), ((IMultiplayerClient)this).UserModsChanged);
                    connection.On<int, BeatmapAvailability>(nameof(IMultiplayerClient.UserBeatmapAvailabilityChanged), ((IMultiplayerClient)this).UserBeatmapAvailabilityChanged);
                    connection.On<MatchRoomState>(nameof(IMultiplayerClient.MatchRoomStateChanged), ((IMultiplayerClient)this).MatchRoomStateChanged);
                    connection.On<int, MatchUserState>(nameof(IMultiplayerClient.MatchUserStateChanged), ((IMultiplayerClient)this).MatchUserStateChanged);
                    connection.On<MatchServerEvent>(nameof(IMultiplayerClient.MatchEvent), ((IMultiplayerClient)this).MatchEvent);
                    connection.On<MultiplayerPlaylistItem>(nameof(IMultiplayerClient.PlaylistItemAdded), ((IMultiplayerClient)this).PlaylistItemAdded);
                    connection.On<long>(nameof(IMultiplayerClient.PlaylistItemRemoved), ((IMultiplayerClient)this).PlaylistItemRemoved);
                    connection.On<MultiplayerPlaylistItem>(nameof(IMultiplayerClient.PlaylistItemChanged), ((IMultiplayerClient)this).PlaylistItemChanged);
                    connection.On<int, bool>(nameof(IMultiplayerClient.UserVotedToSkipIntro), ((IMultiplayerClient)this).UserVotedToSkipIntro);
                    connection.On(nameof(IMultiplayerClient.VoteToSkipIntroPassed), ((IMultiplayerClient)this).VoteToSkipIntroPassed);

                    connection.On(nameof(IMatchmakingClient.MatchmakingQueueJoined), ((IMatchmakingClient)this).MatchmakingQueueJoined);
                    connection.On(nameof(IMatchmakingClient.MatchmakingQueueLeft), ((IMatchmakingClient)this).MatchmakingQueueLeft);
                    connection.On<MatchmakingRoomInvitationParams>(nameof(IMatchmakingClient.MatchmakingRoomInvitedWithParams), ((IMatchmakingClient)this).MatchmakingRoomInvitedWithParams);
                    connection.On<MatchmakingDuelIssuedParams>(nameof(IMatchmakingClient.MatchmakingDuelIssued), ((IMatchmakingClient)this).MatchmakingDuelIssued);
                    connection.On<long, string>(nameof(IMatchmakingClient.MatchmakingRoomReady), ((IMatchmakingClient)this).MatchmakingRoomReady);
                    connection.On<MatchmakingLobbyStatus>(nameof(IMatchmakingClient.MatchmakingLobbyStatusChanged), ((IMatchmakingClient)this).MatchmakingLobbyStatusChanged);
                    connection.On<MatchmakingQueueStatus>(nameof(IMatchmakingClient.MatchmakingQueueStatusChanged), ((IMatchmakingClient)this).MatchmakingQueueStatusChanged);
                    connection.On<int, long>(nameof(IMatchmakingClient.MatchmakingItemSelected), ((IMatchmakingClient)this).MatchmakingItemSelected);
                    connection.On<int, long>(nameof(IMatchmakingClient.MatchmakingItemDeselected), ((IMatchmakingClient)this).MatchmakingItemDeselected);

                    connection.On<int, RankedPlayCardItem>(nameof(IRankedPlayClient.RankedPlayCardAdded), ((IRankedPlayClient)this).RankedPlayCardAdded);
                    connection.On<int, RankedPlayCardItem>(nameof(IRankedPlayClient.RankedPlayCardRemoved), ((IRankedPlayClient)this).RankedPlayCardRemoved);
                    connection.On<RankedPlayCardItem, MultiplayerPlaylistItem>(nameof(IRankedPlayClient.RankedPlayCardRevealed), ((IRankedPlayClient)this).RankedPlayCardRevealed);
                    connection.On<RankedPlayCardItem>(nameof(IRankedPlayClient.RankedPlayCardPlayed), ((IRankedPlayClient)this).RankedPlayCardPlayed);

                    connection.On(nameof(IStatefulUserHubClient.DisconnectRequested), ((IStatefulUserHubClient)this).DisconnectRequested);
                    connection.On(nameof(IStatefulUserHubClient.ServerShuttingDown), ((IStatefulUserHubClient)this).ServerShuttingDown);

                    // torii GHOST CURSOR: relay por NOMBRE de metodo (no toca los tipos MessagePack
                    // del protocolo) — un server/cliente que no lo conoce simplemente lo ignora.
                    connection.On<int, float, float>(@"RankedPlayCursor", (userId, x, y) => TriggerRankedPlayCursorReceived(userId, new Vector2(x, y)));

                    // torii DIBUJITO: mismo esquema que el cursor — relay por nombre, arrays de
                    // primitivos, cero tipos nuevos en el protocolo.
                    connection.On<int, int, float[], float[], int, float, bool>(@"RankedPlayDrawStroke", (userId, strokeId, xs, ys, colour, thickness, done) =>
                    {
                        int count = Math.Min(xs.Length, ys.Length);
                        var points = new Vector2[count];

                        for (int i = 0; i < count; i++)
                            points[i] = new Vector2(xs[i], ys[i]);

                        TriggerRankedPlayDrawStrokeReceived(userId, strokeId, points, colour, thickness, done);
                    });
                    connection.On<int>(@"RankedPlayDrawClear", TriggerRankedPlayDrawClearReceived);
                };

                IsConnected.BindTo(connector.IsConnected);
            }
        }

        public override Task<string> RankedPlayGetLiveMatches(int poolId)
        {
            var conn = connection;

            // Sin conexion se devuelve vacio en vez de tirar: el panel muestra
            // "no hay partidas" y listo. Que no se pueda listar no es un error
            // que el jugador tenga que ver.
            if (!IsConnected.Value || conn == null)
                return Task.FromResult(string.Empty);

            return conn.InvokeAsync<string>(@"RankedPlayGetLiveMatches", poolId);
        }

        public override Task SendRankedPlayCursor(Vector2 normalisedPosition)
        {
            // torii GHOST CURSOR: fire-and-forget total — es cosmetica, no estado del match. Sin
            // conexion no hay nada que mandar, y un fallo puntual de red se ignora en el caller.
            if (!IsConnected.Value || connection == null)
                return Task.CompletedTask;

            return connection.SendAsync(@"RankedPlayCursor", normalisedPosition.X, normalisedPosition.Y);
        }

        public override Task SendRankedPlayDrawStroke(int strokeId, Vector2[] points, int colour, float thickness, bool done)
        {
            // torii DIBUJITO: misma politica que el cursor — cosmetica pura, sin conexion no se manda.
            var conn = connection;

            if (!IsConnected.Value || conn == null || points.Length == 0)
                return Task.CompletedTask;

            float[] xs = new float[points.Length];
            float[] ys = new float[points.Length];

            for (int i = 0; i < points.Length; i++)
            {
                xs[i] = points[i].X;
                ys[i] = points[i].Y;
            }

            return conn.SendAsync(@"RankedPlayDrawStroke", strokeId, xs, ys, colour, thickness, done);
        }

        public override Task SendRankedPlayDrawClear()
        {
            var conn = connection;

            if (!IsConnected.Value || conn == null)
                return Task.CompletedTask;

            return conn.SendAsync(@"RankedPlayDrawClear");
        }

        protected override async Task<MultiplayerRoom> CreateRoomInternal(MultiplayerRoom room)
        {
            if (!IsConnected.Value)
                throw new OperationCanceledException();

            Debug.Assert(connection != null);

            try
            {
                return await connection.InvokeAsync<MultiplayerRoom>(nameof(IMultiplayerServer.CreateRoom), room).ConfigureAwait(false);
            }
            catch (HubException exception)
            {
                if (exception.GetHubExceptionMessage() == HubClientConnector.SERVER_SHUTDOWN_MESSAGE)
                {
                    Debug.Assert(connector != null);

                    await connector.Reconnect().ConfigureAwait(false);
                    return await CreateRoomInternal(room).ConfigureAwait(false);
                }

                throw;
            }
        }

        protected override async Task<MultiplayerRoom> JoinRoomInternal(long roomId, string? password = null)
        {
            if (!IsConnected.Value)
                throw new OperationCanceledException();

            Debug.Assert(connection != null);

            try
            {
                return await connection.InvokeAsync<MultiplayerRoom>(nameof(IMultiplayerServer.JoinRoomWithPassword), roomId, password ?? string.Empty).ConfigureAwait(false);
            }
            catch (HubException exception)
            {
                if (exception.GetHubExceptionMessage() == HubClientConnector.SERVER_SHUTDOWN_MESSAGE)
                {
                    Debug.Assert(connector != null);

                    await connector.Reconnect().ConfigureAwait(false);
                    return await JoinRoomInternal(roomId, password).ConfigureAwait(false);
                }

                throw;
            }
        }

        protected override Task LeaveRoomInternal()
        {
            if (!IsConnected.Value)
                return Task.FromCanceled(new CancellationToken(true));

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.LeaveRoom));
        }

        public override async Task InvitePlayer(int userId)
        {
            if (!IsConnected.Value)
                return;

            Debug.Assert(connection != null);

            try
            {
                await connection.InvokeAsync(nameof(IMultiplayerServer.InvitePlayer), userId).ConfigureAwait(false);
            }
            catch (HubException exception)
            {
                switch (exception.GetHubExceptionMessage())
                {
                    case UserBlockedException.MESSAGE:
                        PostNotification?.Invoke(new SimpleErrorNotification { Text = OnlinePlayStrings.InviteFailedUserBlocked });
                        break;

                    case UserBlocksPMsException.MESSAGE:
                        PostNotification?.Invoke(new SimpleErrorNotification { Text = OnlinePlayStrings.InviteFailedUserOptOut });
                        break;
                }
            }
        }

        public override Task TransferHost(int userId)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.TransferHost), userId);
        }

        public override Task KickUser(int userId)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.KickUser), userId);
        }

        public override Task ChangeSettings(MultiplayerRoomSettings settings)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.ChangeSettings), settings);
        }

        public override Task ChangeState(MultiplayerUserState newState)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.ChangeState), newState);
        }

        public override Task ChangeBeatmapAvailability(BeatmapAvailability newBeatmapAvailability)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.ChangeBeatmapAvailability), newBeatmapAvailability);
        }

        public override Task ChangeUserStyle(int? beatmapId, int? rulesetId)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.ChangeUserStyle), beatmapId, rulesetId);
        }

        public override Task ChangeUserMods(IEnumerable<APIMod> newMods)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.ChangeUserMods), newMods);
        }

        public override Task SendMatchRequest(MatchUserRequest request)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.SendMatchRequest), request);
        }

        public override Task StartMatch()
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.StartMatch));
        }

        public override Task AbortGameplay()
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.AbortGameplay));
        }

        public override Task AbortMatch()
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.AbortMatch));
        }

        public override Task AddPlaylistItem(MultiplayerPlaylistItem item)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.AddPlaylistItem), item);
        }

        public override Task EditPlaylistItem(MultiplayerPlaylistItem item)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.EditPlaylistItem), item);
        }

        public override Task RemovePlaylistItem(long playlistItemId)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.RemovePlaylistItem), playlistItemId);
        }

        public override Task VoteToSkipIntro()
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IMultiplayerServer.VoteToSkipIntro));
        }

        public override Task DiscardCards(RankedPlayCardItem[] cards)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IRankedPlayServer.DiscardCards), cards);
        }

        public override Task PlayCard(RankedPlayCardItem card)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);

            return connection.InvokeAsync(nameof(IRankedPlayServer.PlayCard), card);
        }

        public override Task<MatchmakingPool[]> GetMatchmakingPoolsOfType(MatchmakingPoolType type)
        {
            // torii: capturar la conexion en un local y tratar null como desconectado. Durante un
            // reconnect hay una ventana en la que IsConnected==true pero CurrentConnection es null;
            // el Debug.Assert de antes crasheaba el build debug (la cola pollea estos metodos) y en
            // release era un NRE latente.
            var conn = connection;

            if (!IsConnected.Value || conn == null)
                return Task.FromResult(Array.Empty<MatchmakingPool>());

            return conn.InvokeAsync<MatchmakingPool[]>(nameof(IMatchmakingServer.GetMatchmakingPoolsOfType), type);
        }

        public override Task<MatchmakingJoinLobbyResponse> MatchmakingJoinLobbyWithParams(MatchmakingJoinLobbyRequest request)
        {
            var conn = connection;

            if (!IsConnected.Value || conn == null)
                return Task.FromResult(new MatchmakingJoinLobbyResponse());

            return conn.InvokeAsync<MatchmakingJoinLobbyResponse>(nameof(IMatchmakingServer.MatchmakingJoinLobbyWithParams), request);
        }

        public override Task MatchmakingLeaveLobby()
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);
            return connection.InvokeAsync(nameof(IMatchmakingServer.MatchmakingLeaveLobby));
        }

        public override Task MatchmakingJoinQueue(int poolId)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);
            return connection.InvokeAsync(nameof(IMatchmakingServer.MatchmakingJoinQueue), poolId);
        }

        public override Task MatchmakingLeaveQueue()
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);
            return connection.InvokeAsync(nameof(IMatchmakingServer.MatchmakingLeaveQueue));
        }

        public override Task MatchmakingAcceptInvitation()
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);
            return connection.InvokeAsync(nameof(IMatchmakingServer.MatchmakingAcceptInvitation));
        }

        public override Task<MatchmakingIssueDuelResponse> MatchmakingIssueDuel(MatchmakingIssueDuelRequest request)
        {
            if (!IsConnected.Value)
                return Task.FromResult(new MatchmakingIssueDuelResponse());

            Debug.Assert(connection != null);
            return connection.InvokeAsync<MatchmakingIssueDuelResponse>(nameof(IMatchmakingServer.MatchmakingIssueDuel), request);
        }

        public override Task<MatchmakingAcceptDuelResponse> MatchmakingAcceptDuel(MatchmakingAcceptDuelRequest request)
        {
            if (!IsConnected.Value)
                return Task.FromResult(new MatchmakingAcceptDuelResponse());

            Debug.Assert(connection != null);
            return connection.InvokeAsync<MatchmakingAcceptDuelResponse>(nameof(IMatchmakingServer.MatchmakingAcceptDuel), request);
        }

        public override Task MatchmakingDeclineInvitation()
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);
            return connection.InvokeAsync(nameof(IMatchmakingServer.MatchmakingDeclineInvitation));
        }

        public override Task MatchmakingToggleSelection(long playlistItemId)
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);
            return connection.InvokeAsync(nameof(IMatchmakingServer.MatchmakingToggleSelection), playlistItemId);
        }

        public override Task MatchmakingSkipToNextStage()
        {
            if (!IsConnected.Value)
                return Task.CompletedTask;

            Debug.Assert(connection != null);
            return connection.InvokeAsync(nameof(IMatchmakingServer.MatchmakingSkipToNextStage));
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            connector?.Dispose();
        }

        public override async Task Reconnect()
        {
            if (connector != null)
                await connector.Reconnect().ConfigureAwait(false);
        }

        protected override async Task DisconnectInternal()
        {
            if (connector != null)
                await connector.Disconnect().ConfigureAwait(false);
        }
    }
}
