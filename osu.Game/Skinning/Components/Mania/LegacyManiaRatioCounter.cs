// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;

namespace osu.Game.Skinning.Components.Mania
{
    /// <summary>
    /// Legacy-skin variant of <see cref="ManiaRatioCounter"/>. Uses the
    /// loaded skin's <c>score-N.png</c> bitmap digit sprites (and the
    /// <c>score-dot.png</c> glyph for the decimal separator) so the
    /// counter visually fits in next to the rest of the legacy HUD.
    ///
    /// If the skin doesn't ship its own score font, <see cref="LegacySpriteText"/>
    /// falls back to the default skin's bitmaps automatically — no
    /// special-casing needed here.
    /// </summary>
    public partial class LegacyManiaRatioCounter : ManiaRatioCounter
    {
        public LegacyManiaRatioCounter()
        {
            // Conservative default placement: sits in the top-right
            // corner like the classic accuracy counter, just below it
            // so the two read as a stack of complementary metrics.
            // Skin editor users can drag it anywhere from here.
            Anchor = Anchor.TopRight;
            Origin = Anchor.TopRight;

            Scale = new Vector2(0.55f);
            Margin = new MarginPadding { Top = 60, Right = 17 };
        }

        protected sealed override OsuSpriteText CreateSpriteText() => new LegacySpriteText(LegacyFont.Score)
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            FixedWidth = true,
        };
    }
}
