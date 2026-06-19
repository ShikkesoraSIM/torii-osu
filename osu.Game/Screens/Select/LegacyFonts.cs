// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// torii: la fuente central de la UI de song-select estilo legacy (stable). usa "Aller", la fuente
    /// real del osu!stable, convertida del TTF al formato bitmap-font del juego y metida en Fonts/Aller.
    /// Aller solo trae los pesos Light / Regular / Bold. cambia la tipografia aca y restilas toda la UI
    /// legacy de una.
    /// </summary>
    public static class LegacyFonts
    {
        public static FontUsage Get(float size, FontWeight weight = FontWeight.Regular)
            => OsuFont.GetFont(Typeface.Aller, size: size, weight: weight);
    }
}
