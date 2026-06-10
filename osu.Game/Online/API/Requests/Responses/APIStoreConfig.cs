// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>
    /// Response of <c>GET/PUT /api/v2/torii/store/config</c> — the cosmetic
    /// store pool config. <see cref="Disabled"/> is the set of catalog ids
    /// (trail / name-colour / aura ids) an admin has pulled from the store pool,
    /// so they aren't offered for sale. Empty = everything sellable.
    /// </summary>
    public class APIStoreConfig
    {
        [JsonProperty("disabled")]
        public string[] Disabled { get; set; } = Array.Empty<string>();
    }
}
