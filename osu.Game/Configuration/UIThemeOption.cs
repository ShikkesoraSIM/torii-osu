// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.ComponentModel;

namespace osu.Game.Configuration
{
    /// <summary>
    /// Cosmetic chrome palette for the UI. Read once at startup by
    /// <see cref="osu.Game.Graphics.OsuColour"/> and
    /// <see cref="osu.Game.Overlays.OverlayColourProvider"/>; changing
    /// the value requires a process restart because the resolved
    /// palette is baked into every drawable at construction.
    ///
    /// The grayscale option is a bake of fsyori's UI palette rework
    /// (originally at github.com/fsyori/osu reskin branch) — every
    /// chrome accent gets desaturated via luminance preservation
    /// rather than carrying a hardcoded second palette file, so
    /// brightness relationships between related shades survive intact
    /// (PinkLighter stays brighter than PinkDarker, etc.).
    ///
    /// Torii does not bundle a companion gameplay skin. Users wanting
    /// the literal stable-era texture chrome on top of the palette
    /// can drop any stable .osk that ships <c>user-bg</c>,
    /// <c>levelbar</c>, <c>songselect-bottom</c>, etc. into their
    /// skins folder and select it; the in-code panels
    /// (<see cref="osu.Game.Screens.SelectV2.LegacyUserStatsPanel"/>,
    /// <see cref="osu.Game.Screens.SelectV2.LegacyFooterChromeStrip"/>)
    /// pick those textures up via <see cref="osu.Game.Skinning.ISkinSource.GetTexture"/>.
    /// Otherwise the panels render their own Torii-Nova fallback chrome.
    /// </summary>
    public enum UIThemeOption
    {
        /// <summary>
        /// Default Torii palette: full-saturation accents (pink, blue,
        /// yellow, etc.) on the standard dark backgrounds.
        /// </summary>
        [Description("Torii")]
        Torii,

        /// <summary>
        /// Grayscale by fsyori: chrome accents stripped of saturation.
        /// Mounts the stable-style legacy user-stats panel in song
        /// select and switches the corner-radius / lightness / mod-
        /// colour mapping across the UI to match fsyori's reskin.
        /// </summary>
        [Description("Grayscale by fsyori")]
        GrayscaleByFsyori,

        /// <summary>
        /// Midnight: same structural reskin as grayscale (sharp corners,
        /// legacy stats panel mounted) but with a deep-purple / fuchsia
        /// palette in place of the desaturated grays. Keeps the slanted
        /// song-select chrome (does NOT force the unslanted layout that
        /// grayscale uses).
        /// </summary>
        [Description("Midnight")]
        Midnight,
    }
}
