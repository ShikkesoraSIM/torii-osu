// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace osu.Game.Cosmetics.Definitions
{
    /// <summary>
    /// torii: definicion DATA-DRIVEN de un cosmetico. es lo que exporta la Cosmetic Creator y lo
    /// que Torii carga para la store: metadata + un dict de parametros que un factory aplica sobre
    /// la clase runtime correcta (trail/name colour/aura). la gracia es que NO hay codigo por
    /// cosmetico: la comunidad compone/parametriza tipos ya existentes y whitelisteados, asi que el
    /// mismo JSON se ve identico en la tool y en el juego (portable) y es seguro (sin code-exec).
    /// </summary>
    public class CosmeticDefinition
    {
        /// <summary>version del schema, para migrar formatos viejos sin romper.</summary>
        public int SchemaVersion { get; set; } = 1;

        /// <summary>id estable (ej "trail-sunset"). unico dentro de su tipo.</summary>
        public string Id { get; set; }

        /// <summary>nombre para mostrar.</summary>
        public string Name { get; set; }

        /// <summary>que clase de cosmetico es (define que factory lo construye).</summary>
        public CosmeticType Type { get; set; }

        /// <summary>bucket de rareza/precio para la store.</summary>
        public CosmeticTier Tier { get; set; } = CosmeticTier.Basic;

        /// <summary>precio placeholder; el server es authoritative cuando shippee.</summary>
        public int Price { get; set; }

        /// <summary>solo para trails: la familia de render (Dot/Ribbon/Particle). null en otros tipos.</summary>
        public CosmeticTrailFamily? Family { get; set; }

        /// <summary>los parametros del cosmetico (nombre de propiedad -> valor). el factory los aplica
        /// por reflection sobre la clase runtime. colores como "#RRGGBBAA", Vector2 como [x,y],
        /// enums por nombre, arrays de color como lista de hex. ver <see cref="CosmeticSettingsBinder"/>.</summary>
        [JsonProperty("settings")]
        public JObject Settings { get; set; } = new JObject();

        /// <summary>que parametros de <see cref="Settings"/> puede tunear el COMPRADOR en la store
        /// (ej color, tamano). vacio = nada tuneable. se usa en fases posteriores.</summary>
        public string[] ExposedParams { get; set; } = Array.Empty<string>();

        private static readonly JsonSerializerSettings serializer_settings = new JsonSerializerSettings
        {
            // enums por nombre (Type/Tier/Family) para que el JSON sea legible y estable ante
            // reordenamientos del enum.
            Converters = { new StringEnumConverter() },
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
        };

        public static CosmeticDefinition Parse(string json) =>
            JsonConvert.DeserializeObject<CosmeticDefinition>(json, serializer_settings);

        public string Serialize() => JsonConvert.SerializeObject(this, serializer_settings);
    }

    /// <summary>que clase de cosmetico describe una <see cref="CosmeticDefinition"/>.</summary>
    public enum CosmeticType
    {
        Trail,
        NameColour,
        Aura,
    }
}
