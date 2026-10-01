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
            public string Id { get; }

            /// <summary>Null when the shipped definition failed to load; the store skips those.</summary>
            public AuraPreset Preset { get; }

            public int Price { get; }
            public CosmeticTier Tier { get; }

            public Entry(string id, int price, CosmeticTier tier)
            {
                Id = id;
                Preset = AuraRegistry.GetById(id);
                Price = price;
                Tier = tier;
            }
        }

        public static readonly IReadOnlyList<Entry> All = new[]
        {
            // Summer is also earned via its event group; buying it is just an alternative path.
            new Entry(SummerAuraPreset.ID, 3000, CosmeticTier.Premium),
            // autumn 2026
            new Entry("autumn-leaffall", 2500, CosmeticTier.Special),
            new Entry("autumn-leaffall-gust", 2500, CosmeticTier.Special),
            new Entry("autumn-leaffall-dusk", 2500, CosmeticTier.Special),
            new Entry("autumn-maple-wind", 4500, CosmeticTier.Premium),
            new Entry(StardustAuraPreset.ID, 10000, CosmeticTier.Premium),
        };

        public static Entry GetById(string id) => All.FirstOrDefault(e => e.Id == id);
    }
}
