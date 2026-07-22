// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>
    /// torii: respuesta de <c>GET /api/v2/torii/comfort-pick/floor</c>. El piso anti-sandbag
    /// para el star-rating pick de ranked play (derivado de las top plays del jugador), mas
    /// el estado del pick de la season actual.
    /// </summary>
    public class APIComfortPickFloor
    {
        [JsonProperty("season_id")]
        public string SeasonId { get; set; } = string.Empty;

        /// <summary>Piso: el pick no puede ser menor a esto.</summary>
        [JsonProperty("floor")]
        public float Floor { get; set; }

        /// <summary>SR (con mods) de la top play del jugador, o null si no tiene plays.</summary>
        [JsonProperty("top_play_sr")]
        public float? TopPlaySr { get; set; }

        /// <summary>Techo del pick.</summary>
        [JsonProperty("pick_max")]
        public float PickMax { get; set; }

        /// <summary>Si el jugador ya eligio esta season (gate: no puede volver a elegir).</summary>
        [JsonProperty("already_picked")]
        public bool AlreadyPicked { get; set; }

        /// <summary>El SR elegido esta season, si ya eligio.</summary>
        [JsonProperty("current_pick")]
        public float? CurrentPick { get; set; }
    }
}
