// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Catch.Objects;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Catch.Mods
{
    public class CatchModDoubleTime : ModDoubleTime
    {
        public override BindableBool AdjustWindows { get; } = new BindableBool();

        public override void ApplyToDifficulty(BeatmapDifficulty difficulty)
        {
            if (AdjustApproachRate.Value)
            {
                double preempt = IBeatmapDifficultyInfo.DifficultyRange(difficulty.ApproachRate, CatchHitObject.PREEMPT_RANGE);
                preempt *= SpeedChange.Value;
                difficulty.ApproachRate = (float)IBeatmapDifficultyInfo.InverseDifficultyRange(preempt, CatchHitObject.PREEMPT_RANGE);
            }
        }
    }
}
