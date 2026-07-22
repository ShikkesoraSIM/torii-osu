// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>
    /// torii: respuesta de <c>GET /api/v2/torii/comfort-pick/rank</c>. El rango de ranked play
    /// del jugador para el badge de la cola: mu actual, partidas jugadas y si sigue en placement
    /// (provisional). Mientras <see cref="Provisional"/> es true el badge muestra "Provisional"
    /// en vez del tier, asi un seed fresco del star-pick no se lee como "Master" sin games.
    /// </summary>
    public class APIMatchmakingRank
    {
        /// <summary>El mu (rating) actual del jugador, o null si no tiene stats en el pool.</summary>
        [JsonProperty("rating")]
        public int? Rating { get; set; }

        /// <summary>Partidas ranked terminadas en el pool.</summary>
        [JsonProperty("plays")]
        public int Plays { get; set; }

        /// <summary>Si el jugador sigue en placement (tier no final todavia).</summary>
        [JsonProperty("provisional")]
        public bool Provisional { get; set; }

        /// <summary>Partidas necesarias para salir de placement.</summary>
        [JsonProperty("placement_plays")]
        public int PlacementPlays { get; set; }
    }
}
