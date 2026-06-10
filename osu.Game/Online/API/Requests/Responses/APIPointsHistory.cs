// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>One row of the points ledger (earn or spend).</summary>
    public class APIPointTransaction
    {
        [JsonProperty("amount")]
        public int Amount { get; set; }

        /// <summary>Why this moved (e.g. <c>top_play</c>, <c>store_purchase</c>, <c>gift</c>).</summary>
        [JsonProperty("reason")]
        public string Reason { get; set; }

        /// <summary>Free-form context for the reason (a score id, a cosmetic id, <c>streak:N</c>, ...).</summary>
        [JsonProperty("ref")]
        public string Ref { get; set; }

        [JsonProperty("balance_after")]
        public int BalanceAfter { get; set; }

        /// <summary>Naive-UTC timestamp string from the server. Parse with <see cref="CreatedAtUtc"/>.</summary>
        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }

        /// <summary>The created_at string parsed as UTC, or null if it can't be parsed. The server
        /// stores naive UTC, so we force universal interpretation rather than letting the local tz
        /// skew it.</summary>
        public DateTime? CreatedAtUtc
        {
            get
            {
                if (string.IsNullOrEmpty(CreatedAt))
                    return null;

                if (DateTime.TryParse(CreatedAt, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                        out var dt))
                    return dt;

                return null;
            }
        }
    }

    /// <summary>Response of <c>GET /torii/points/me/history</c> — a page of the ledger, newest first.</summary>
    public class APIPointsHistory
    {
        [JsonProperty("balance")]
        public int Balance { get; set; }

        [JsonProperty("transactions")]
        public APIPointTransaction[] Transactions { get; set; } = Array.Empty<APIPointTransaction>();
    }
}
