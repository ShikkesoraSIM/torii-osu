// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Scoring;

namespace osu.Game.Rulesets.Osu.Mods
{
    public class OsuModNightcore : ModNightcore<OsuHitObject>
    {
        public override void ApplyToDifficulty(BeatmapDifficulty difficulty)
        {
            if (AdjustApproachRate.Value)
            {
                double preempt = IBeatmapDifficultyInfo.DifficultyRange(
                    difficulty.ApproachRate,
                    OsuHitObject.PREEMPT_RANGE);

                preempt *= SpeedChange.Value;
                difficulty.ApproachRate = (float)IBeatmapDifficultyInfo.InverseDifficultyRange(preempt, OsuHitObject.PREEMPT_RANGE);
            }

            if (AdjustWindows.Value)
            {
                double greatWindow = IBeatmapDifficultyInfo.DifficultyRange(
                    difficulty.OverallDifficulty,
                    OsuHitWindows.GREAT_WINDOW_RANGE);

                greatWindow *= SpeedChange.Value;
                difficulty.OverallDifficulty = (float)IBeatmapDifficultyInfo.InverseDifficultyRange(greatWindow, OsuHitWindows.GREAT_WINDOW_RANGE);
            }
        }
    }
}
