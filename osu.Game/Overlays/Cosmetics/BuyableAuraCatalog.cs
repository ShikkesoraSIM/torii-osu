// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Collections.Generic;
using System.Linq;
using osu.Game.Cosmetics;
using osu.Game.Graphics.UserEffects;
using osu.Game.Graphics.UserEffects.Presets;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>
    /// Pairs the (few) auras that can be BOUGHT with points to their store
    /// metadata (price + tier). Every other aura is earned from a group/role
    /// and resolved through <see cref="AuraRegistry"/> directly; those never
    /// appear here. Ownership of a bought aura is tracked client-side by
    /// <see cref="ToriiCosmeticsManager"/> under the aura id, exactly like a
    /// trail or name colour.
    /// </summary>
    public static class BuyableAuraCatalog
    {
        public class Entry
        {
            public AuraPreset Preset { get; }
            public int Price { get; }
            public CosmeticTier Tier { get; }
            public string Id => Preset.AuraId;

            public Entry(AuraPreset preset, int price, CosmeticTier tier)
            {
                Preset = preset;
                Price = price;
                Tier = tier;
            }
        }

        public static readonly IReadOnlyList<Entry> All = new[]
        {
            // For now the store sells only the Summer aura. Stardust stays
            // registered as a preset (previewable in the inventory gallery) but
            // is not on sale yet. Summer is also earned via its event group;
            // buying it is just an alternative path to the same aura.
            new Entry(AuraRegistry.GetById(SummerAuraPreset.ID), 3000, CosmeticTier.Premium),
        };

        public static Entry GetById(string id) => All.FirstOrDefault(e => e.Id == id);
    }
}
