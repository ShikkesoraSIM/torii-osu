// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Screens.Ranking.Expanded.Accuracy;
using osuTK;

namespace osu.Game.Screens.Ranking.Expanded.Statistics
{
    public partial class StatisticCounter : RollingCounter<int>
    {
        protected override double RollingDuration => AccuracyCircle.ACCURACY_TRANSFORM_DURATION;

        protected override Easing RollingEasing => AccuracyCircle.ACCURACY_TRANSFORM_EASING;

        // slot de digito y no de 'm' (ver NumericSpriteText). El -0.6 deja el ancho
        // efectivo por caracter en 9.4px, que es exactamente lo que daba Torus.
        protected override OsuSpriteText CreateSpriteText() => new NumericSpriteText().With(s =>
        {
            s.Font = OsuFont.Torus.With(size: 20, fixedWidth: true);
            s.Spacing = new Vector2(0f, 0);
        });
    }
}
