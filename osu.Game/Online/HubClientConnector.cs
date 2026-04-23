// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using osu.Framework;
using osu.Game.Online.API;
using osu.Game.Rulesets;

namespace osu.Game.Online
{
    public class HubClientConnector : PersistentEndpointClientConnector, IHubClientConnector
    {
        public const string SERVER_SHUTDOWN_MESSAGE = "Server is shutting down.";

        public const string VERSION_HASH_HEADER = @"X-Osu-Version-Hash";
        public const string RULESET_HASH_HEADER = @"X-Osu-Ruleset-Hashes";
        public const string CLIENT_SESSION_ID_HEADER = @"X-Client-Session-ID";

        /// <summary>
        /// Header sent with every hub connection to identify the Torii client to the server.
        /// The server uses this to populate <see cref="osu.Game.Users.UserPresence.ClientName"/>
        /// in presence broadcasts so other clients can show the Torii badge.
        /// </summary>
        public const string CLIENT_NAME_HEADER = @"X-Client-Name";

        private const string CLIENT_NAME_VALUE = @"torii";

        /// <summary>
        /// Invoked whenever a new hub connection is built, to configure it before it's started.
        /// </summary>
        public Action<HubConnection>? ConfigureConnection { get; set; }

        private readonly string endpoint;
        private readonly string versionHash;
        private readonly RulesetHashCache rulesetHashCache;

        /// <summary>
        /// The current connection opened by this connector.
        /// </summary>
        public new HubConnection? CurrentConnection => ((HubClient?)base.CurrentConnection)?.Connection;

        /// <summary>
        /// Constructs a new <see cref="HubClientConnector"/>.
        /// </summary>
        /// <param name="clientName">The name of the client this connector connects for, used for logging.</param>
        /// <param name="endpoint">The endpoint to the hub.</param>
        /// <param name="api"> An API provider used to react to connection state changes.</param>
        /// <param name="versionHash">The hash representing the current game version, used for verification purposes.</param>
        /// <param name="rulesetHashCache">The ruleset hash cache.</param>
        public HubClientConnector(string clientName, string endpoint, IAPIProvider api, string versionHash, RulesetHashCache rulesetHashCache)
            : base(api)
        {
            ClientName = clientName;
            this.endpoint = endpoint;
            this.versionHash = versionHash;
            this.rulesetHashCache = rulesetHashCache;

            // Automatically start these connections.
            Start();
        }

        protected override Task<PersistentEndpointClient> BuildConnectionAsync(CancellationToken cancellationToken)
        {
            var builder = new HubConnectionBuilder()
                .WithUrl(endpoint, options =>
                {
                    // Configuring proxies is not supported on iOS, see https://github.com/xamarin/xamarin-macios/issues/14632.
                    if (RuntimeInfo.OS != RuntimeInfo.Platform.iOS)
                        options.Proxy = HttpClient.DefaultProxy;

                    options.AccessTokenProvider = () => Task.FromResult<string?>(API.AccessToken);
                    options.Headers.Add(VERSION_HASH_HEADER, versionHash);
                    options.Headers.Add(CLIENT_SESSION_ID_HEADER, API.SessionIdentifier.ToString());
                    options.Headers.Add(RULESET_HASH_HEADER, JsonConvert.SerializeObject(rulesetHashCache.RulesetsHashes));
                    options.Headers.Add(CLIENT_NAME_HEADER, CLIENT_NAME_VALUE);
                });

            builder.AddMessagePackProtocol(options =>
            {
                options.SerializerOptions = SignalRUnionWorkaroundResolver.OPTIONS;
            });

            var newConnection = builder.Build();

            ConfigureConnection?.Invoke(newConnection);

            return Task.FromResult((PersistentEndpointClient)new HubClient(newConnection));
        }

        async Task IHubClientConnector.Disconnect()
        {
            await Disconnect().ConfigureAwait(false);
            API.Logout();
        }

        protected override string ClientName { get; }
    }
}
