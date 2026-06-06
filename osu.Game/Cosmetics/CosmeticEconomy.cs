// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Cosmetics
{
    /// <summary>
    /// Points-economy knobs for cosmetics. PLACEHOLDER values for design; the
    /// authoritative pricing + ownership lives server-side once the shop ships.
    ///
    /// Deliberate progression sink: buying a trail gives it at its DEFAULT length
    /// and density (locked to the catalog values). The ABILITY to retune a
    /// trail's length/density is a separate, account-wide unlock you buy once.
    /// So an endgame player sitting on a big balance can't instantly own AND
    /// max-customise everything at the same time - there's always a next thing
    /// to spend points on, and a freshly-bought trail still looks "stock" until
    /// you've earned the customise unlock.
    /// </summary>
    public static class CosmeticEconomy
    {
        /// <summary>One-time, account-wide unlock: adjust trail LENGTH on owned
        /// trails.</summary>
        public const int AdjustableLengthUnlock = 2500;

        /// <summary>One-time, account-wide unlock: adjust trail DENSITY /
        /// thickness on owned trails.</summary>
        public const int AdjustableDensityUnlock = 2500;

        /// <summary>Range for the LENGTH slider, as a multiplier over the trail's
        /// default. Weighted hard toward SHORTENING: the default (1x) is already
        /// a long-ish look, a long trail reads as a weird smear, so most of the
        /// slider trims it down. Only a hair of headroom above default.</summary>
        public const float MinLengthMultiplier = 0.25f;
        public const float MaxLengthMultiplier = 1.2f;

        /// <summary>Range for the DENSITY / thickness slider. Kept moderate so
        /// neither end looks broken (too sparse / too fat).</summary>
        public const float MinDensityMultiplier = 0.6f;
        public const float MaxDensityMultiplier = 1.4f;
    }
}
