// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Online
{
    /// <summary>
    /// Endpoint configuration locked to the Torii server (g0v0). The official
    /// osu.ppy.sh endpoints are intentionally never used — this client only ever
    /// talks to Torii, both for the API and the SignalR hubs.
    /// </summary>
    public class ToriiEndpointConfiguration : EndpointConfiguration
    {
        public ToriiEndpointConfiguration()
        {
            const string api_url = @"https://lazer-api.shikkesora.com";

            APIUrl = api_url;
            WebsiteUrl = @"https://lazer.shikkesora.com";

            // g0v0 mirrors the osu-web OAuth surface, so the stock public client works.
            APIClientSecret = @"FGc9GAtyHzeQDshWP5Ah7dega8hJACAJpQtw6OXk";
            APIClientID = "5";

            SpectatorUrl = $@"{api_url}/signalr/spectator";
            MultiplayerUrl = $@"{api_url}/signalr/multiplayer";
            MetadataUrl = $@"{api_url}/signalr/metadata";
            BeatmapSubmissionServiceUrl = $@"{api_url}/beatmap-submission";
        }
    }
}
