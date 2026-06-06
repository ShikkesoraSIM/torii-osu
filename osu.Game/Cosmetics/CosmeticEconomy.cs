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

        /// <summary>Range a player may push length/density once unlocked, as a
        /// multiplier over the trail's default.</summary>
        public const float MinAdjustMultiplier = 0.5f;
        public const float MaxAdjustMultiplier = 2.0f;
    }
}
