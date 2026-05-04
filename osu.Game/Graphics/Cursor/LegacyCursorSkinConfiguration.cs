// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Graphics.Cursor
{
    /// <summary>
    /// A subset of <c>OsuSkinConfiguration</c> covering ONLY the cursor-related
    /// skin.ini <c>[General]</c> keys. Mirrored verbatim into <c>osu.Game</c>
    /// so the menu-context cursor pipeline (<see cref="SkinnableGameplayCursor"/>,
    /// <see cref="MenuCursorTrail"/>) can read these values without taking a
    /// reference on <c>osu.Game.Rulesets.Osu</c> (which would create a circular
    /// project dependency — the ruleset already references <c>osu.Game</c>).
    ///
    /// The lookup mechanism is name-based: <see cref="LegacySkin.genericLookup"/>
    /// keys the parsed <c>[General]</c> dictionary by the enum value's
    /// <see cref="object.ToString"/>. As long as the names here match the names
    /// in <c>OsuSkinConfiguration</c> exactly, the lookup resolves to the same
    /// value the osu! ruleset would read in gameplay.
    /// </summary>
    internal enum LegacyCursorSkinConfiguration
    {
        /// <summary>
        /// Whether the cursor (and trail particles when present) should be
        /// origin-centred on the host's reported mouse position. Defaults to
        /// <c>true</c> when the skin doesn't declare an opinion. Old osu!
        /// stable skins occasionally set this to <c>false</c> so the cursor's
        /// top-left pixel is the click point — uncommon but supported.
        /// </summary>
        CursorCentre,

        /// <summary>
        /// Whether the cursor should continuously spin in gameplay (and, in
        /// our case, in menus when the user opts into a gameplay cursor
        /// style). Defaults to <c>true</c> when unset, matching osu! stable
        /// and lazer's <see cref="LegacyCursor"/> behaviour. Skins that ship
        /// a directional or asymmetric cursor explicitly set this to <c>0</c>
        /// to keep the visual upright.
        /// </summary>
        CursorRotate,

        /// <summary>
        /// Whether trail parts should rotate to match the cursor's current
        /// rotation. Honoured by <see cref="MenuCursorTrail"/>'s
        /// <c>AllowPartRotation</c>. Defaults to <c>true</c> when unset.
        /// </summary>
        CursorTrailRotate,
    }
}
