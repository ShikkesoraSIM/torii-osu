// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using JetBrains.Annotations;
using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>
    /// Response payload of <c>GET /api/v2/me/aura-catalog</c> and the
    /// PATCH equip endpoint. Carries everything the settings UI needs to
    /// render the picker plus the sentinel constants so we don't hardcode
    /// "default" / "none" strings in the client.
    /// </summary>
    public class APIAuraCatalog
    {
        /// <summary>Sentinel value to write back when the user picks "Default".</summary>
        [JsonProperty("sentinel_default")]
        public string SentinelDefault { get; set; } = "default";

        /// <summary>Sentinel value to write back when the user picks "None".</summary>
        [JsonProperty("sentinel_none")]
        public string SentinelNone { get; set; } = "none";

        /// <summary>The auras this user is entitled to equip, ordered for display.</summary>
        [JsonProperty("available")]
        public APIAuraCatalogEntry[] Available { get; set; } = System.Array.Empty<APIAuraCatalogEntry>();

        /// <summary>Raw stored value of the user's pick, including sentinels.
        /// Null when never picked. Use this to pre-select the picker UI.</summary>
        [JsonProperty("current_setting")]
        [CanBeNull]
        public string CurrentSetting { get; set; }

        /// <summary>Resolved aura id that everyone else sees on this user's
        /// name right now. Mirrors <c>APIUser.EquippedAura</c>.</summary>
        [JsonProperty("effective_aura_id")]
        [CanBeNull]
        public string EffectiveAuraId { get; set; }
    }

    public class APIAuraCatalogEntry
    {
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        [JsonProperty("display_name")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonProperty("description")]
        public string Description { get; set; } = string.Empty;

        [JsonProperty("owning_groups")]
        public string[] OwningGroups { get; set; } = System.Array.Empty<string>();
    }
}
