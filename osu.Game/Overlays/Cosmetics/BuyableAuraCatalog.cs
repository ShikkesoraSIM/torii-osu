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
            // Stardust — the single points-buyable aura for now. Priced above a
            // gradient name colour (800) since it is a larger, animated effect,
            // but well within reach so it feels like a goal, not a paywall.
            new Entry(AuraRegistry.GetById(StardustAuraPreset.ID), 2500, CosmeticTier.Special),
        };

        public static Entry GetById(string id) => All.FirstOrDefault(e => e.Id == id);
    }
}
