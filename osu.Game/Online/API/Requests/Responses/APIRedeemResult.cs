// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>Result of redeeming an access code: points awarded, the new
    /// balance, and any cosmetic ids the code unlocked.</summary>
    public class APIRedeemResult
    {
        [JsonProperty("awarded")]
        public int Awarded { get; set; }

        [JsonProperty("balance")]
        public int Balance { get; set; }

        [JsonProperty("granted_cosmetics")]
        public string[] GrantedCosmetics { get; set; } = Array.Empty<string>();
    }
}
