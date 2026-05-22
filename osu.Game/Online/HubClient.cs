// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;

namespace osu.Game.Online
{
    public class HubClient : PersistentEndpointClient
    {
        public readonly HubConnection Connection;

        public HubClient(HubConnection connection)
        {
            Connection = connection;
            Connection.Closed += InvokeClosed;
        }

        public override Task ConnectAsync(CancellationToken cancellationToken) => Connection.StartAsync(cancellationToken);

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync().ConfigureAwait(false);

            // Torii: stop the SignalR connection with a bounded cancellation
            // token before disposing. HubConnection.DisposeAsync internally
            // calls StopAsync with no caller-provided token, so a dead
            // server (or any close-handshake stall) makes the dispose chain
            // wait on the SignalR runtime's internal timeouts (which are
            // tied to KeepAliveInterval * 2 = ~30s by default). Forcing
            // a stop with our own 2s token bounds it tightly, so the
            // subsequent DisposeAsync just cleans up the already-stopped
            // connection state instead of trying to gracefully close.
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await Connection.StopAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort. If StopAsync throws or hits our cancel,
                // DisposeAsync still needs to run to release transport
                // resources (the underlying socket etc.).
            }

            await Connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}
