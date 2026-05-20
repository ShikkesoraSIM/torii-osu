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
        /// Midnight Mauve: structural reskin (sharp corners, mounted
        /// legacy stats panel) with a deep-violet / fuchsia palette,
        /// the originally-curated Midnight look. Keeps the slanted
        /// song-select chrome (only Grayscale forces the unslanted
        /// layout).
        ///
        /// Enum identifier stays as <c>Midnight</c> so existing user
        /// configs that stored the value as a string don't lose their
        /// selection when the variant family was expanded. The display
        /// label is "Midnight (Mauve)" so users see the family name in
        /// the dropdown without a silent migration.
        /// </summary>
        [Description("Midnight (Mauve)")]
        Midnight,

        /// <summary>
        /// Midnight Crimson: same structural reskin as Mauve but with
        /// a deep-red / scarlet palette pull. The auto-tint hue lands
        /// at ~350° so vivid accents read as a burned-red night rather
        /// than violet, while neutrals stay close to the shared base.
        /// </summary>
        [Description("Midnight (Crimson)")]
        MidnightCrimson,

        /// <summary>
        /// Midnight Cerulean: same structural reskin as Mauve but with
        /// a deep cyan-teal palette pull (auto-tint hue ~200°). Reads
        /// as the "cold night" counterpart to Crimson's warm tones,
        /// useful for users who want the Midnight chrome without the
        /// red/violet warmth.
        /// </summary>
        [Description("Midnight (Cerulean)")]
        MidnightCerulean,
    }

    /// <summary>
    /// Narrowed identifier for which Midnight palette variant is in
    /// use. Surfaced by <see cref="osu.Game.Graphics.OsuColour.ActiveMidnightVariant"/>
    /// so palette helpers can branch on the hue family without
    /// needing to compare against every UIThemeOption value.
    ///
    /// Distinct from <see cref="UIThemeOption"/> because the variant
    /// is only meaningful when the active theme is one of the
    /// Midnight family entries — defaults to <see cref="Mauve"/> when
    /// the user has Torii or Grayscale selected, so reads from
    /// helpers that don't first check the family stay well-defined.
    /// </summary>
    public enum MidnightVariant
    {
        /// <summary>Magenta-violet identity (original Midnight curation, hue ~305°).</summary>
        Mauve,

        /// <summary>Deep red / scarlet identity (hue ~352°).</summary>
        Crimson,

        /// <summary>Deep blue-cyan identity (hue ~205°).</summary>
        Cerulean,
    }
}
