// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// torii: la fuente central de la UI de song-select estilo legacy (stable). por default usa la
    /// fuente moderna de lazer (Torus); con <see cref="UseAllerFont"/> pasa a "Aller", la fuente real
    /// del osu!stable (convertida del TTF a bitmap-font en Fonts/Aller; solo trae Light / Regular / Bold).
    /// cambia esto aca y restilas toda la UI legacy de una.
    /// </summary>
    public static class LegacyFonts
    {
        /// <summary>
        /// Cuando es true la UI legacy usa la fuente Aller de osu!stable; cuando es false (default) usa
        /// la fuente moderna de lazer. Lo setea <see cref="osu.Game.OsuGameBase"/> al inicio y en cada
        /// cambio del setting <c>ToriiLegacyFont</c>, asi re-entrar al song select ya toma la fuente nueva.
        /// </summary>
        public static bool UseAllerFont { get; set; }

        public static FontUsage Get(float size, FontWeight weight = FontWeight.Regular)
            => UseAllerFont
                ? OsuFont.GetFont(Typeface.Aller, size: size, weight: weight)
                : OsuFont.GetFont(size: size, weight: weight);
    }
}
