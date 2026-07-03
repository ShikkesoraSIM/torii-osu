// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics;

namespace osu.Game.Cosmetics.Definitions
{
    /// <summary>
    /// torii: construye el drawable de un cursor-trail a partir de una <see cref="CosmeticDefinition"/>
    /// data-driven. reusa 1:1 las clases de trail que ya existen (ToriiCosmeticTrail / CosmeticRibbonTrail
    /// / CosmeticParticleTrail) y solo les aplica los parametros del JSON. o sea: el mismo trail que hoy
    /// se arma con un lambda hardcodeado en CosmeticCatalog ahora se puede armar 100% desde datos.
    /// </summary>
    public static class CosmeticTrailFactory
    {
        /// <summary>true si esta definicion es un trail que este factory sabe construir.</summary>
        public static bool CanBuild(CosmeticDefinition def) => def != null && def.Type == CosmeticType.Trail;

        /// <summary>construye el trail (implementa <see cref="ICosmeticTrail"/>) desde la definicion.</summary>
        public static Drawable Create(CosmeticDefinition def)
        {
            if (def == null)
                throw new ArgumentNullException(nameof(def));
            if (def.Type != CosmeticType.Trail)
                throw new ArgumentException($"CosmeticDefinition '{def.Id}' no es un trail (Type={def.Type}).", nameof(def));

            switch (def.Family ?? CosmeticTrailFamily.Dot)
            {
                case CosmeticTrailFamily.Ribbon:
                {
                    var trail = new CosmeticRibbonTrail();
                    CosmeticSettingsBinder.Apply(trail, def.Settings);
                    return trail;
                }

                case CosmeticTrailFamily.Particle:
                {
                    var trail = new CosmeticParticleTrail();

                    // la forma de particula viene por nombre (whitelist), no como codigo.
                    string shape = (string)def.Settings?["ParticleShape"];
                    trail.ParticleFactory = CosmeticParticleShapes.Get(shape);

                    // aplicamos el resto de los params, salteando los que no son propiedades reflectables.
                    CosmeticSettingsBinder.Apply(trail, def.Settings, "ParticleShape", "ParticleFactory");
                    return trail;
                }

                default: // Dot
                {
                    var trail = new ToriiCosmeticTrail();
                    CosmeticSettingsBinder.Apply(trail, def.Settings);
                    return trail;
                }
            }
        }
    }
}
