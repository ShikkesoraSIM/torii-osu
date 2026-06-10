// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>Response of <c>GET /torii/points/me</c> — the authoritative balance.</summary>
    public class APIMyPoints
    {
        [JsonProperty("balance")]
        public int Balance { get; set; }
    }

    /// <summary>Response of <c>GET /torii/store/owned</c> — catalog ids owned server-side.</summary>
    public class APIOwnedCosmetics
    {
        [JsonProperty("owned")]
        public string[] Owned { get; set; } = Array.Empty<string>();
    }

    /// <summary>Response of <c>POST /torii/store/purchase</c>.</summary>
    public class APIPurchaseResult
    {
        [JsonProperty("owned")]
        public bool Owned { get; set; }

        [JsonProperty("balance")]
        public int Balance { get; set; }

        [JsonProperty("already_owned")]
        public bool AlreadyOwned { get; set; }
    }
}
