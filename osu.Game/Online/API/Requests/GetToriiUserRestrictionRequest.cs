// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// GET /api/v2/torii/restriction - the one authenticated endpoint a restricted
    /// account can still reach. Every normal endpoint 403s a restricted user (so the
    /// client otherwise treats login as a blank failure); this one resolves the token
    /// WITHOUT the restriction 403 and reports why the account is locked, so the client
    /// can show a proper restriction briefing instead of "couldn't log in".
    /// </summary>
    public class GetToriiUserRestrictionRequest : APIRequest<APIToriiUserRestriction>
    {
        protected override string Target => @"torii/restriction";
    }

    public class APIToriiUserRestriction
    {
        [JsonProperty("is_restricted")]
        public bool IsRestricted { get; set; }

        [JsonProperty("permanent")]
        public bool Permanent { get; set; }

        [JsonProperty("reason")]
        public string? Reason { get; set; }

        [JsonProperty("ends_at")]
        public DateTimeOffset? EndsAt { get; set; }
    }
}
