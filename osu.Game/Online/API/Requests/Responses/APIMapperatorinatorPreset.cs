// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>
    /// torii: una combinacion de opciones de Mapperatorinator guardada con nombre. Vive
    /// en el server, asi que sigue estando despues de formatear la maquina.
    /// </summary>
    public class APIMapperatorinatorPreset
    {
        [JsonProperty(@"id")]
        public int Id { get; set; }

        [JsonProperty(@"name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>The generation settings, in the same shape the sidecar uses.</summary>
        [JsonProperty(@"settings")]
        public string Settings { get; set; } = string.Empty;

        [JsonProperty(@"updated_at")]
        public DateTimeOffset UpdatedAt { get; set; }

        /// <summary>De quien lo sacaste, cuando lo sacaste del mapa de otra persona.</summary>
        [JsonProperty(@"origin_username")]
        public string? OriginUsername { get; set; }

        /// <summary>Cuanta gente se llevo este preset tuyo.</summary>
        [JsonProperty(@"forks")]
        public int Forks { get; set; }

        [JsonProperty(@"forked_by")]
        public List<string> ForkedBy { get; set; } = new List<string>();
    }

    public class APIMapperatorinatorPresetList
    {
        [JsonProperty(@"presets")]
        public List<APIMapperatorinatorPreset> Presets { get; set; } = new List<APIMapperatorinatorPreset>();
    }
}
