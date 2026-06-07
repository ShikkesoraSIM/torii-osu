// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osuTK.Graphics;

namespace osu.Game.Cosmetics
{
    /// <summary>
    /// Client-side catalog of username-colour cosmetics: flat solids (cheap),
    /// two-colour gradients, and animated rainbow styles (the "wow" tier). All
    /// bought with points, same as cursor trails.
    /// </summary>
    public static class CosmeticNameColourCatalog
    {
        public static readonly IReadOnlyList<CosmeticNameColour> Colours = new[]
        {
            // ── Solids ──────────────────────────────────────────────────────
            solid("name-crimson", "Crimson", 200, new Color4(235, 70, 80, 255)),
            solid("name-ocean", "Ocean", 200, new Color4(70, 160, 245, 255)),
            solid("name-mint", "Mint", 200, new Color4(95, 225, 170, 255)),
            solid("name-gold", "Gold", 200, new Color4(245, 200, 75, 255)),
            solid("name-violet", "Amethyst", 200, new Color4(180, 120, 240, 255)),
            solid("name-coral", "Coral", 200, new Color4(255, 130, 110, 255)),

            // ── Gradients ───────────────────────────────────────────────────
            gradient("name-sunset", "Sunset", 800, new Color4(255, 170, 60, 255), new Color4(235, 60, 150, 255)),
            gradient("name-tide", "Tide", 800, new Color4(70, 200, 255, 255), new Color4(110, 100, 245, 255)),
            gradient("name-forest", "Forest", 800, new Color4(150, 230, 90, 255), new Color4(40, 175, 150, 255)),
            gradient("name-berry", "Berry", 800, new Color4(255, 110, 200, 255), new Color4(150, 90, 240, 255)),

            // ── Animated ────────────────────────────────────────────────────
            rainbow("name-rainbow", "Rainbow", 2600),
        };

        private static CosmeticNameColour solid(string id, string name, int price, Color4 colour)
            => new CosmeticNameColour(id, name, CosmeticTier.Basic, price, NameColourStyle.Solid, colour);

        private static CosmeticNameColour gradient(string id, string name, int price, Color4 a, Color4 b)
            => new CosmeticNameColour(id, name, CosmeticTier.Special, price, NameColourStyle.Gradient, a, b);

        private static CosmeticNameColour rainbow(string id, string name, int price)
            => new CosmeticNameColour(id, name, CosmeticTier.Premium, price, NameColourStyle.Rainbow, Color4.White);
    }
}
