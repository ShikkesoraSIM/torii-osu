// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Taiko.Objects;
using osu.Game.Rulesets.Taiko.Scoring;

namespace osu.Game.Rulesets.Taiko.Mods
{
    public class TaikoModNightcore : ModNightcore<TaikoHitObject>
    {
        public override void ApplyToDifficulty(BeatmapDifficulty difficulty)
        {
            if (AdjustApproachRate.Value)
            {
                difficulty.SliderMultiplier /= SpeedChange.Value;
            }

            if (AdjustWindows.Value)
            {
                double greatWindow = IBeatmapDifficultyInfo.DifficultyRange(
                    difficulty.OverallDifficulty,
                    TaikoHitWindows.GREAT_WINDOW_RANGE);

                greatWindow *= SpeedChange.Value;
                difficulty.OverallDifficulty = (float)IBeatmapDifficultyInfo.InverseDifficultyRange(greatWindow, TaikoHitWindows.GREAT_WINDOW_RANGE);
            }
        }
    }
}
