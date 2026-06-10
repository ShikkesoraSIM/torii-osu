// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>One pending Torii gift for the local user.</summary>
    public class APIGift
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("points")]
        public int Points { get; set; }

        [JsonProperty("granted_cosmetics")]
        public string[] GrantedCosmetics { get; set; } = Array.Empty<string>();

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("sender")]
        public string Sender { get; set; }
    }

    /// <summary>Response of <c>GET /torii/gifts/pending</c>.</summary>
    public class APIPendingGifts
    {
        [JsonProperty("gifts")]
        public APIGift[] Gifts { get; set; } = Array.Empty<APIGift>();
    }

    /// <summary>Response of <c>POST /torii/gifts/claim</c>.</summary>
    public class APIGiftClaim
    {
        [JsonProperty("points")]
        public int Points { get; set; }

        [JsonProperty("granted_cosmetics")]
        public string[] GrantedCosmetics { get; set; } = Array.Empty<string>();

        [JsonProperty("balance")]
        public int Balance { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; }

        [JsonProperty("sender")]
        public string Sender { get; set; }
    }
}
