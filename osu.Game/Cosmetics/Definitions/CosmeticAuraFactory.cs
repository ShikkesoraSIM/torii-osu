// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Graphics.UserEffects;

namespace osu.Game.Cosmetics.Definitions
{
    /// <summary>
    /// torii: construye un aura (un <see cref="AuraPreset"/> data-driven) a partir de una
    /// <see cref="CosmeticDefinition"/>. Análogo a <see cref="CosmeticTrailFactory"/> para trails: el
    /// mismo JSON que exporta la Cosmetic Creator se vuelve un aura que Torii renderiza igual que las
    /// hardcodeadas. Los <see cref="CosmeticDefinition.Settings"/> se deserializan al modelo
    /// <see cref="DataDrivenAura"/> con Newtonsoft (todo primitivos/strings/listas), sin code-exec.
    /// </summary>
    public static class CosmeticAuraFactory
    {
        /// <summary>true si esta definición es un aura que este factory sabe construir.</summary>
        public static bool CanBuild(CosmeticDefinition def) => def != null && def.Type == CosmeticType.Aura;

        /// <summary>construye el aura (un <see cref="AuraPreset"/>) desde la definición.</summary>
        public static AuraPreset Create(CosmeticDefinition def)
        {
            if (def == null)
                throw new ArgumentNullException(nameof(def));
            if (def.Type != CosmeticType.Aura)
                throw new ArgumentException($"CosmeticDefinition '{def.Id}' no es un aura (Type={def.Type}).", nameof(def));

            // el modelo entero (mezcla de partículas + glow + ornamentos) sale del sub-JSON de Settings.
            // ToObject es defensivo: un campo mal tipeado cae al default de la prop en vez de tirar.
            var data = def.Settings?.ToObject<DataDrivenAura>() ?? new DataDrivenAura();
            return new DataDrivenAuraPreset(def.Id, data);
        }
    }
}
