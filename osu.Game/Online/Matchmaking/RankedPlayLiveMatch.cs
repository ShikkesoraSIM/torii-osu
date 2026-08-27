// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;

namespace osu.Game.Online.Matchmaking
{
    /// <summary>
    /// Una partida de ranked play EN CURSO, para mostrarla desde afuera.
    /// </summary>
    /// <remarks>
    /// El estado de lobby que ya existia solo traia partidas TERMINADAS
    /// (<c>RecentMatches</c>), que sirve para "que paso" pero no para "que esta
    /// pasando". Esto es lo segundo.
    ///
    /// Viaja como JSON por un metodo propio del hub y NO dentro de
    /// <see cref="MatchmakingLobbyStatus"/>, a proposito: esa clase es parte del
    /// contrato messagepack del paquete compartido, y agregarle una Key obliga a
    /// republicar el paquete y a que cliente y server queden atados a la misma
    /// version. Por nombre de metodo el agregado es aditivo: un cliente viejo
    /// simplemente no escucha ese metodo. Es el mismo camino que uso el ghost
    /// cursor por la misma razon.
    /// </remarks>
    public class RankedPlayLiveMatch
    {
        [JsonProperty("room_id")]
        public long RoomId { get; set; }

        [JsonProperty("players")]
        public RankedPlayLivePlayer[] Players { get; set; } = [];

        /// <summary>
        /// En que anda la sala: eligiendo carta, calentando, jugando, etc.
        /// Texto plano y no un enum: si el server agrega una etapa nueva, un
        /// cliente viejo la muestra igual en vez de caerse a un default.
        /// </summary>
        [JsonProperty("stage")]
        public string Stage { get; set; } = string.Empty;

        [JsonProperty("round")]
        public int Round { get; set; }

        /// <summary>Titulo del mapa que se esta jugando, si hay uno elegido.</summary>
        [JsonProperty("beatmap")]
        public string? Beatmap { get; set; }

        [JsonProperty("beatmap_id")]
        public int? BeatmapId { get; set; }

        /// <summary>Si en este momento estan jugando (y no eligiendo o esperando).</summary>
        [JsonProperty("in_gameplay")]
        public bool InGameplay { get; set; }
    }

    public class RankedPlayLivePlayer
    {
        [JsonProperty("user_id")]
        public int UserId { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; } = string.Empty;

        /// <summary>Vida restante. El maximo es <see cref="MaxLife"/>.</summary>
        [JsonProperty("life")]
        public int Life { get; set; }

        [JsonProperty("max_life")]
        public int MaxLife { get; set; } = 1_000_000;

        [JsonProperty("rating")]
        public int Rating { get; set; }

        /// <summary>0..1, listo para dibujar una barra.</summary>
        [JsonIgnore]
        public float LifeFraction => MaxLife <= 0 ? 0 : Math.Clamp((float)Life / MaxLife, 0, 1);
    }
}
