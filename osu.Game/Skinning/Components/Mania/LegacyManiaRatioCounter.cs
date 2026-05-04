// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;

namespace osu.Game.Skinning.Components.Mania
{
    /// <summary>
    /// Legacy-skin variant of <see cref="ManiaRatioCounter"/> — renders the
    /// PA ratio using the loaded skin's <c>score-N.png</c> bitmap digit
    /// sprites (and the <c>score-dot.png</c> glyph for the decimal
    /// separator), so the counter visually fits next to the rest of a
    /// legacy HUD.
    ///
    /// Implementation is intentionally identical to <see cref="LegacyScoreCounter"/>:
    /// just hand back a <see cref="LegacySpriteText"/> bound to
    /// <see cref="LegacyFont.Score"/>. No probe, no fallback, no
    /// dependency-injected skin source — when there's no legacy skin
    /// active this component shouldn't be visible in the first place.
    /// The skin layout machinery already swaps in the appropriate
    /// non-legacy variant (<see cref="CustomizableManiaRatioCounter"/>)
    /// for skins that don't ship the bitmap digits, so adding a
    /// per-component fallback here would just duplicate that selection
    /// logic in the wrong layer and visually muddle the "this is the
    /// LEGACY visual, period" intent of the class name.
    /// </summary>
    public partial class LegacyManiaRatioCounter : ManiaRatioCounter
    {
        public LegacyManiaRatioCounter()
        {
            // Conservative default placement: top-right corner, just
            // below where the classic accuracy counter sits, so the two
            // read as a stack of complementary metrics. Skin editor
            // users can drag it anywhere from here.
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
