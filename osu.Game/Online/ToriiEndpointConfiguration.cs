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
            // prod: todo cuelga del mismo host y los hubs van bajo /signalr.
            : this(@"https://lazer-api.shikkesora.com", @"https://lazer.shikkesora.com", @"https://lazer-api.shikkesora.com/signalr")
        {
        }

        // torii: ctor parametrizado para apuntar a un stack local en debug (api en un
        // puerto, los hubs del spectator en otro) sin tocar el comportamiento de prod.
        public ToriiEndpointConfiguration(string apiUrl, string websiteUrl, string signalrBase)
        {
            APIUrl = apiUrl;
            WebsiteUrl = websiteUrl;

            // g0v0 mirrors the osu-web OAuth surface, so the stock public client works.
            APIClientSecret = @"FGc9GAtyHzeQDshWP5Ah7dega8hJACAJpQtw6OXk";
            APIClientID = "5";

            SpectatorUrl = $@"{signalrBase}/spectator";
            MultiplayerUrl = $@"{signalrBase}/multiplayer";
            MetadataUrl = $@"{signalrBase}/metadata";
            BeatmapSubmissionServiceUrl = $@"{apiUrl}/beatmap-submission";
        }
    }
}
