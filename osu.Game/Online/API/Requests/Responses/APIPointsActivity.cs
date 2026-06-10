// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>A user and how many points they earned in the activity window.</summary>
    public class APIPointEarner
    {
        [JsonProperty("user_id")]
        public int UserId { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("earned")]
        public int Earned { get; set; }

        [JsonProperty("balance")]
        public int Balance { get; set; }
    }

    /// <summary>A single large award, for the recent-awards anti-abuse list.</summary>
    public class APIPointAward
    {
        [JsonProperty("user_id")]
        public int UserId { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("amount")]
        public int Amount { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }

        [JsonProperty("ref")]
        public string Ref { get; set; }
    }

    /// <summary>Response of <c>GET /torii/points/admin/activity</c>.</summary>
    public class APIPointsActivity
    {
        [JsonProperty("days")]
        public int Days { get; set; }

        [JsonProperty("top_earners")]
        public APIPointEarner[] TopEarners { get; set; } = Array.Empty<APIPointEarner>();

        [JsonProperty("recent_large")]
        public APIPointAward[] RecentLarge { get; set; } = Array.Empty<APIPointAward>();
    }
}
