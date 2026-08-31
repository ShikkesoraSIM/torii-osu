// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;
using osu.Game.Online.API;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>One earned (positive) points event from the server ledger.</summary>
    public class APIPointEvent
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("amount")]
        public int Amount { get; set; }

        /// <summary>Why the points were earned (e.g. <c>top_play</c>, <c>daily_play</c>, <c>gift</c>).</summary>
        [JsonProperty("reason")]
        public string Reason { get; set; }

        /// <summary>Free-form context for the reason (e.g. a score id, or <c>streak:N</c> for daily plays).</summary>
        [JsonProperty("ref")]
        public string Ref { get; set; }

        [JsonProperty("balance_after")]
        public int BalanceAfter { get; set; }

        /// <summary>El texto crudo del server, sin parsear.</summary>
        /// <remarks>
        /// Se guarda como string y se parsea en <see cref="CreatedAt"/> para poder
        /// decidir nosotros que pasa cuando NO viene la zona. Si se deserializara
        /// directo a DateTimeOffset, un timestamp pelado se tomaria como hora local
        /// y quedaria corrido por el huso de cada persona (ver ToriiTime).
        /// </remarks>
        [JsonProperty("created_at")]
        public string CreatedAtRaw { get; set; }

        /// <summary>Cuando se gano. Sirve para no festejar lo de ayer.</summary>
        public DateTimeOffset CreatedAt => ToriiTime.ParseAssumingUtc(CreatedAtRaw);
    }

    /// <summary>Response of <c>GET /torii/points/feed</c> — recent earnings after a cursor id.</summary>
    public class APIPointsFeed
    {
        [JsonProperty("balance")]
        public int Balance { get; set; }

        /// <summary>Highest event id known to the server; clients persist this as their cursor.</summary>
        [JsonProperty("last_id")]
        public int LastId { get; set; }

        [JsonProperty("events")]
        public APIPointEvent[] Events { get; set; } = Array.Empty<APIPointEvent>();
    }
}
