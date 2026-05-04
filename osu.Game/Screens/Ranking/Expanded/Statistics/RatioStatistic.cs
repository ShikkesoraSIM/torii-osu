// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Screens.Ranking.Expanded.Statistics
{
    /// <summary>
    /// VSRG "ratio" stat (Perfects / Greats) shown in the
    /// <see cref="ExpandedPanelMiddleContent"/>'s top stat row, between
    /// max combo and PP. Mania-only — gameplay-side it shows up live in
    /// the HUD via <c>ManiaRatioCounter</c>; this is the post-play
    /// summary card so the number is preserved on the results screen
    /// for sharing / replays.
    ///
    /// Edge cases mirror the live counter for consistency:
    /// - Below 15 total Perfect+Great hits we show "--" (not enough
    ///   data to be meaningful — typical for very short maps or fail
    ///   plays).
    /// - With non-zero Perfects but zero Greats we show "MAX".
    /// </summary>
    public partial class RatioStatistic : StatisticDisplay
    {
        private const int min_judgements_required = 15;

        private readonly double ratio;
        private readonly bool isMax;
        private readonly bool notEnoughData;

        private RollingCounter<double> counter = null!;

        public RatioStatistic(ScoreInfo score)
            : base(@"Ratio")
        {
            int perfects = score.Statistics.GetValueOrDefault(HitResult.Perfect);
            int greats = score.Statistics.GetValueOrDefault(HitResult.Great);

            int total = perfects + greats;

            notEnoughData = total < min_judgements_required;
            isMax = !notEnoughData && greats == 0 && perfects > 0;
            ratio = notEnoughData || isMax ? 0 : (double)perfects / greats;
        }

        public override void Appear()
        {
            base.Appear();

            // Counter is a RollingCounter<double>; rolling NaN /
            // Infinity through it is messy, so we just set the
            // numeric value and let the formatter render the
            // sentinel state when the flags are set.
            counter.Current.Value = ratio;
        }

        protected override Drawable CreateContent() => counter = new RatioCounter(this);

        private partial class RatioCounter : RollingCounter<double>
        {
            private readonly RatioStatistic owner;

            public RatioCounter(RatioStatistic owner)
            {
                this.owner = owner;
            }

            protected override double RollingDuration => 250;
            protected override Easing RollingEasing => Easing.OutQuad;

            protected override LocalisableString FormatCount(double count)
            {
                if (owner.notEnoughData)
                    return @"--";

                if (owner.isMax)
                    return @"MAX";

                return count.ToString("0.00");
            }

            protected override OsuSpriteText CreateSpriteText() => base.CreateSpriteText().With(s =>
            {
                s.Font = OsuFont.Torus.With(size: 20, fixedWidth: true);
                s.Spacing = new Vector2(-2, 0);
            });
        }
    }
}
