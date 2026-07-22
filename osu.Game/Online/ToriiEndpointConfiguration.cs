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
            // override local para dev: TORII_API_URL / TORII_WEB_URL apuntan el cliente al
            // stack local (torii.local) sin tocar el default de prod. sin las envs seteadas
            // se comporta identico a siempre.
            string api_url = System.Environment.GetEnvironmentVariable("TORII_API_URL")?.TrimEnd('/') ?? @"https://lazer-api.shikkesora.com";
            string web_url = System.Environment.GetEnvironmentVariable("TORII_WEB_URL")?.TrimEnd('/') ?? @"https://lazer.shikkesora.com";

            APIUrl = api_url;
            WebsiteUrl = web_url;

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
