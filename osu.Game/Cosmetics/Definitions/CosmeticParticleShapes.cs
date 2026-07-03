// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;

namespace osu.Game.Cosmetics.Definitions
{
    /// <summary>
    /// torii: whitelist de "formas" de particula que un trail de familia Particle puede usar. la
    /// <see cref="CosmeticDefinition"/> las referencia por NOMBRE (string) en vez de un lambda de C#,
    /// asi la data no puede meter codigo arbitrario. la Creator ofrece esta lista como opciones.
    /// </summary>
    public static class CosmeticParticleShapes
    {
        private static readonly Dictionary<string, Func<int, Drawable>> map = new Dictionary<string, Func<int, Drawable>>(StringComparer.OrdinalIgnoreCase)
        {
            ["star"] = CosmeticParticles.Star,
            ["heart"] = CosmeticParticles.Heart,
            ["snowflake"] = CosmeticParticles.Snowflake,
            ["note"] = CosmeticParticles.Note,
            ["petal"] = CosmeticParticles.Petal,
            ["bubble"] = CosmeticParticles.Bubble,
            ["flame"] = CosmeticParticles.Flame,
            ["sparkle"] = CosmeticParticles.Sparkle,
            ["rainbow-sparkle"] = CosmeticParticles.RainbowSparkle,
            ["confetti"] = CosmeticParticles.Confetti,
            ["smoke"] = CosmeticParticles.Smoke,
            ["geometric"] = CosmeticParticles.Geometric,
            ["galaxy-dust"] = CosmeticParticles.GalaxyDust,
            ["pixel"] = CosmeticParticles.Pixel,
            ["bolt"] = CosmeticParticles.Bolt,
        };

        /// <summary>los nombres disponibles (para poblar el dropdown de la Creator).</summary>
        public static IReadOnlyCollection<string> Names => map.Keys;

        /// <summary>resuelve una forma por nombre; cae a Bubble si es desconocida (defensivo).</summary>
        public static Func<int, Drawable> Get(string name)
            => name != null && map.TryGetValue(name, out var factory) ? factory : CosmeticParticles.Bubble;

        public static bool Has(string name) => name != null && map.ContainsKey(name);
    }
}
