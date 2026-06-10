// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osuTK.Graphics;

namespace osu.Game.Cosmetics
{
    /// <summary>Rarity bucket for a store cosmetic. Drives the daily store rotation
    /// (how many of each rarity are featured) and the Store Admin grouping.</summary>
    public enum CosmeticRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary,
    }

    /// <summary>
    /// Central rarity table for store cosmetics. THIS is the one place to retune how
    /// items are bucketed: the daily-store rotation picks a fixed number of each
    /// rarity from the eligible pool (see <see cref="ToriiCosmeticsManager.GetDailyStore"/>),
    /// and the Store Admin panel groups items by it. Ids not listed default to
    /// <see cref="CosmeticRarity.Common"/>.
    /// </summary>
    public static class CosmeticRarities
    {
        /// <summary>Display order, cheapest first.</summary>
        public static readonly CosmeticRarity[] Order =
        {
            CosmeticRarity.Common,
            CosmeticRarity.Uncommon,
            CosmeticRarity.Rare,
            CosmeticRarity.Epic,
            CosmeticRarity.Legendary,
        };

        private static readonly Dictionary<string, CosmeticRarity> map = new Dictionary<string, CosmeticRarity>
        {
            // ── Cursor trails ───────────────────────────────────────────────
            // Common: cheap solids + the two plainest particle trails.
            ["trail-pearl"] = CosmeticRarity.Common,
            ["trail-crimson"] = CosmeticRarity.Common,
            ["trail-ocean"] = CosmeticRarity.Common,
            ["trail-mint"] = CosmeticRarity.Common,
            ["trail-gold"] = CosmeticRarity.Common,
            ["trail-violet"] = CosmeticRarity.Common,
            ["trail-bubbles"] = CosmeticRarity.Common,
            ["trail-smoke"] = CosmeticRarity.Common,
            // Uncommon: gradients + lighter particles.
            ["trail-sunset"] = CosmeticRarity.Uncommon,
            ["trail-ember"] = CosmeticRarity.Uncommon,
            ["trail-frost"] = CosmeticRarity.Uncommon,
            ["trail-arcade"] = CosmeticRarity.Uncommon,
            ["trail-starlight"] = CosmeticRarity.Uncommon,
            ["trail-melody"] = CosmeticRarity.Uncommon,
            // Rare: nicer particles + the simpler ribbons.
            ["trail-lovestruck"] = CosmeticRarity.Rare,
            ["trail-sakura"] = CosmeticRarity.Rare,
            ["trail-frostfall"] = CosmeticRarity.Rare,
            ["trail-confetti"] = CosmeticRarity.Rare,
            ["trail-prism"] = CosmeticRarity.Rare,
            ["trail-serpent"] = CosmeticRarity.Rare,
            ["trail-wisp"] = CosmeticRarity.Rare,
            ["trail-heartbeat"] = CosmeticRarity.Rare,
            // Epic: heavy particles + fancy single-tone ribbons.
            ["trail-inferno"] = CosmeticRarity.Epic,
            ["trail-storm"] = CosmeticRarity.Epic,
            ["trail-comet"] = CosmeticRarity.Epic,
            ["trail-glitch"] = CosmeticRarity.Epic,
            ["trail-neon-flux"] = CosmeticRarity.Epic,
            // Legendary: the show-stoppers.
            ["trail-aurora"] = CosmeticRarity.Legendary,
            ["trail-rainbow-engined"] = CosmeticRarity.Legendary,
            ["trail-galaxy"] = CosmeticRarity.Legendary,
            ["trail-stardust"] = CosmeticRarity.Legendary,
            ["trail-comet-prime"] = CosmeticRarity.Legendary,
            ["trail-spectrum"] = CosmeticRarity.Legendary,
            ["trail-neon-surge"] = CosmeticRarity.Legendary,
            ["trail-nebula"] = CosmeticRarity.Legendary,
            ["trail-rainbow-ribbon"] = CosmeticRarity.Legendary,

            // ── Name colours (only two visual variants today) ───────────────
            // Common: flat solids.
            ["name-crimson"] = CosmeticRarity.Common,
            ["name-ocean"] = CosmeticRarity.Common,
            ["name-mint"] = CosmeticRarity.Common,
            ["name-gold"] = CosmeticRarity.Common,
            ["name-violet"] = CosmeticRarity.Common,
            ["name-coral"] = CosmeticRarity.Common,
            // Rare: two-colour gradients.
            ["name-sunset"] = CosmeticRarity.Rare,
            ["name-tide"] = CosmeticRarity.Rare,
            ["name-forest"] = CosmeticRarity.Rare,
            ["name-berry"] = CosmeticRarity.Rare,
        };

        public static CosmeticRarity Of(string id) =>
            id != null && map.TryGetValue(id, out var r) ? r : CosmeticRarity.Common;

        public static string DisplayName(CosmeticRarity rarity)
        {
            switch (rarity)
            {
                case CosmeticRarity.Common: return "Common";
                case CosmeticRarity.Uncommon: return "Uncommon";
                case CosmeticRarity.Rare: return "Rare";
                case CosmeticRarity.Epic: return "Epic";
                case CosmeticRarity.Legendary: return "Legendary";
                default: return rarity.ToString();
            }
        }

        /// <summary>Signature colour per rarity (grey → green → blue → purple → gold).</summary>
        public static Color4 ColourOf(CosmeticRarity rarity)
        {
            switch (rarity)
            {
                case CosmeticRarity.Common: return new Color4(165, 172, 182, 255);
                case CosmeticRarity.Uncommon: return new Color4(95, 220, 130, 255);
                case CosmeticRarity.Rare: return new Color4(80, 170, 245, 255);
                case CosmeticRarity.Epic: return new Color4(180, 120, 240, 255);
                case CosmeticRarity.Legendary: return new Color4(245, 195, 70, 255);
                default: return new Color4(165, 172, 182, 255);
            }
        }
    }
}
