// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;
using osu.Game.Localisation;

namespace osu.Game.Graphics.Cursor
{
    /// <summary>
    /// Selector for which cursor visual <see cref="MenuCursorContainer"/>
    /// should render in menu / song-select / overlay contexts (anywhere
    /// outside an actual gameplay playfield).
    ///
    /// Three modes — see the per-value summaries. The selector replaces
    /// the previous boolean <c>UseGameplayCursorInMenus</c>, which
    /// conflated "use skin's gameplay cursor" with "use a stylised
    /// non-skin cursor that happens to look gameplay-shaped". Splitting
    /// them out lets users pick the Torii stylised look as a deliberate
    /// preference even when their skin DOES ship its own cursor.png.
    /// </summary>
    public enum MenuCursorStyle
    {
        /// <summary>
        /// Lazer's built-in menu cursor (the textured arrow at
        /// <c>Cursor/menu-cursor</c>) with the additive pink click
        /// flash. This is the upstream default; we keep it as the
        /// first value of the enum so it's the default for users
        /// who never touch the setting.
        /// </summary>
        [System.ComponentModel.Description("Lazer default")]
        LazerDefault,

        /// <summary>
        /// The user's skin gameplay cursor (<c>cursor.png</c> +
        /// optional <c>cursormiddle.png</c>) rendered through the
        /// same pipeline the osu! ruleset uses in the playfield —
        /// same scaling (GameplayCursorSize), same Expand / Contract
        /// click feel, same continuous rotation when the skin
        /// declares <c>cursorrotate</c>. Live-rebuilds when the
        /// active skin changes.
        /// </summary>
        [System.ComponentModel.Description("Skin cursor")]
        SkinCursor,

        /// <summary>
        /// The Torii stylised cursor — a translucent pink ring with
        /// a white centre dot, scaled by GameplayCursorSize. Same
        /// click and rotation behaviour as <see cref="SkinCursor"/>,
        /// but doesn't depend on the active skin shipping its own
        /// cursor textures (and intentionally OVERRIDES them when
        /// they exist — this is a "use Torii regardless" choice).
        /// </summary>
        [System.ComponentModel.Description("Torii cursor")]
        ToriiCursor,
    }
}
