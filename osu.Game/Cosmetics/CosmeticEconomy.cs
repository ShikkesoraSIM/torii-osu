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
        /// <summary>One-time, account-wide unlock for the length / size / density
        /// sliders on every owned trail. Cheap on purpose (a light grind sink) — it
        /// is a tweak feature, not a cosmetic, and the trail it customises already
        /// cost points. Keep in sync with the server price for "customisation-unlock".</summary>
        public const int AdjustableLengthUnlock = 100;

        /// <summary>Kept equal to the single account-wide unlock above.</summary>
        public const int AdjustableDensityUnlock = 100;

        /// <summary>Range for the DENSITY slider (count per unit travel). Kept
        /// moderate so neither end looks broken (too sparse / too fat).</summary>
        public const float MinDensityMultiplier = 0.6f;
        public const float MaxDensityMultiplier = 1.4f;

        /// <summary>Range for the SIZE / thickness slider.</summary>
        public const float MinSizeMultiplier = 0.6f;
        public const float MaxSizeMultiplier = 1.6f;

        /// <summary>The absolute, trail-independent SHORT end of the length
        /// slider (ms). Length 0 maps here for every dot/ribbon trail, so the
        /// minimum looks the same short length whatever the trail's default is.
        /// Length 1 maps back to the trail's own catalog default.</summary>
        public const double LengthFloorMilliseconds = 90;
    }
}
