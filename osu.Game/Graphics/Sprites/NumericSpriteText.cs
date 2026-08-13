// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Graphics.Sprites
{
    /// <summary>
    /// An <see cref="OsuSpriteText"/> for numbers, where <c>fixedWidth</c> should size each
    /// character off a digit instead of the framework default of 'm'.
    /// </summary>
    /// <remarks>
    /// The default reference character is 'm', which is far wider than any digit, so a
    /// fixed-width number ends up with a lot of dead space between characters. How much
    /// depends entirely on the font: it was barely noticeable with Torus and very visible
    /// with Nunito. <see cref="Skinning.LegacySpriteText"/> and the argon HUD counters
    /// already do this same override.
    /// </remarks>
    public partial class NumericSpriteText : OsuSpriteText
    {
        protected override char FixedWidthReferenceCharacter => '5';

        /// <summary>
        /// The framework defaults, plus '%'.
        /// </summary>
        /// <remarks>
        /// '%' is wider than a digit in most fonts, so forcing it into a digit-sized
        /// slot makes it overlap whatever precedes it. It never needs to line up in a
        /// column either, so it is better off at its natural width.
        /// </remarks>
        protected override char[] FixedWidthExcludeCharacters { get; } = { '.', ',', ':', ' ', '\u00A0', '\u202F', '%' };
    }
}
