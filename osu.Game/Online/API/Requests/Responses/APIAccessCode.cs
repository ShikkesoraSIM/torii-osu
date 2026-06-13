// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>A minted Torii access code (the response of the admin code-create
    /// endpoint). The <see cref="Code"/> is the string a player redeems.</summary>
    public class APIAccessCode
    {
        [JsonProperty("code")]
        public string Code { get; set; } = string.Empty;

        [JsonProperty("amount")]
        public int Amount { get; set; }

        [JsonProperty("max_uses")]
        public int MaxUses { get; set; }

        [JsonProperty("note")]
        public string Note { get; set; }
    }
}
