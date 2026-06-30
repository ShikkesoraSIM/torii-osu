// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Online
{
    public class DevelopmentEndpointConfiguration : EndpointConfiguration
    {
        public DevelopmentEndpointConfiguration()
        {
            // Vanilla stream: even debug builds talk to Torii only, never dev.ppy.sh.
            APIUrl = @"https://lazer-api.shikkesora.com";
            WebsiteUrl = @"https://lazer.shikkesora.com";
            APIClientSecret = @"FGc9GAtyHzeQDshWP5Ah7dega8hJACAJpQtw6OXk";
            APIClientID = "5";
            SpectatorUrl = $@"{APIUrl}/signalr/spectator";
            MultiplayerUrl = $@"{APIUrl}/signalr/multiplayer";
            MetadataUrl = $@"{APIUrl}/signalr/metadata";
            BeatmapSubmissionServiceUrl = $@"{APIUrl}/beatmap-submission";
        }
    }
}
