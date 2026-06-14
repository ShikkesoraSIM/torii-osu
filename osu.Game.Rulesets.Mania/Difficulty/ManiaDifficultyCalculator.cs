// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mania.Difficulty.Skills;
using osu.Game.Rulesets.Mania.MathUtils;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Mania.Difficulty
{
    public class ManiaDifficultyCalculator : DifficultyCalculator
    {
        private readonly bool isForCurrentRuleset;
        private readonly double originalOverallDifficulty;

        public override int Version => 20241007;

        public ManiaDifficultyCalculator(IRulesetInfo ruleset, IWorkingBeatmap beatmap)
            : base(ruleset, beatmap)
        {
            isForCurrentRuleset = beatmap.BeatmapInfo.Ruleset.MatchesOnlineID(ruleset);
            originalOverallDifficulty = beatmap.BeatmapInfo.Difficulty.OverallDifficulty;
        }

        protected override DifficultyAttributes CreateDifficultyAttributes(IBeatmap beatmap, Mod[] mods, Skill[] skills)
        {
            double clockRate = ModUtils.CalculateRateWithMods(mods);

            if (beatmap.HitObjects.Count == 0)
                return new ManiaDifficultyAttributes { Mods = mods };

            HitWindows hitWindows = new ManiaHitWindows();
            hitWindows.SetDifficulty(beatmap.Difficulty.OverallDifficulty);

            ManiaDifficultyAttributes attributes = new ManiaDifficultyAttributes
            {
                StarRating = skills[0].DifficultyValue(),
                Variety = ((SunnySkill)skills[0]).VarietyValue(),
                AccScalar = ((SunnySkill)skills[0]).AccScalarValue(),
                TotalNotes = beatmap.HitObjects.Count,
                Mods = mods,
                // In osu-stable mania, rate-adjustment mods don't affect the hit window.
                // This is done the way it is to introduce fractional differences in order to match osu-stable for the time being.
                GreatHitWindow = getHitWindow300(mods, clockRate),
                MaxCombo = beatmap.HitObjects.Sum(maxComboForObject),
            };

            return attributes;
        }

        private static int maxComboForObject(HitObject hitObject)
        {
            if (hitObject is HoldNote hold)
                return 1 + (int)((hold.EndTime - hold.StartTime) / 100);

            return 1;
        }

        protected override IEnumerable<DifficultyHitObject> CreateDifficultyHitObjects(IBeatmap beatmap, Mod[] mods)
        {
            double clockRate = ModUtils.CalculateRateWithMods(mods);
            var sortedObjects = beatmap.HitObjects.ToArray();

            sortedObjects = sortedObjects.OrderBy(obj => obj.StartTime).ThenBy(obj => ((ManiaHitObject)obj).Column).ToArray();

            int columns = ((ManiaBeatmap)beatmap).TotalColumns;

            var objects = new List<DifficultyHitObject>(sortedObjects.Length);

            for (int i = 1; i < sortedObjects.Length; i++)
            {
                var currentObject = new ManiaDifficultyHitObject(sortedObjects[i], sortedObjects[i - 1], clockRate, objects, objects.Count);

                objects.Add(currentObject);
            }

            return objects;
        }

        // Sorting is done in CreateDifficultyHitObjects, since the full list of hitobjects is required.
        protected override IEnumerable<DifficultyHitObject> SortObjects(IEnumerable<DifficultyHitObject> input) => input;

        protected override Skill[] CreateSkills(IBeatmap beatmap, Mod[] mods)
        {
            double clockRate = ModUtils.CalculateRateWithMods(mods);
            return new Skill[]
            {
                new SunnySkill(mods, ((ManiaBeatmap)beatmap).TotalColumns, beatmap.Difficulty.OverallDifficulty, ((ManiaBeatmap)beatmap).HitObjects.Count, getHitWindow300(mods, clockRate))
            };
        }

        protected override Mod[] DifficultyAdjustmentMods
        {
            get
            {
                var mods = new Mod[]
                {
                    new ManiaModDoubleTime(),
                    new ManiaModHalfTime(),
                    new ManiaModEasy(),
                    new ManiaModHardRock(),
                };

                if (isForCurrentRuleset)
                    return mods;

                // if we are a convert, we can be played in any key mod.
                return mods.Concat(new Mod[]
                {
                    new ManiaModKey1(),
                    new ManiaModKey2(),
                    new ManiaModKey3(),
                    new ManiaModKey4(),
                    new ManiaModKey5(),
                    new MultiMod(new ManiaModKey5(), new ManiaModDualStages()),
                    new ManiaModKey6(),
                    new MultiMod(new ManiaModKey6(), new ManiaModDualStages()),
                    new ManiaModKey7(),
                    new MultiMod(new ManiaModKey7(), new ManiaModDualStages()),
                    new ManiaModKey8(),
                    new MultiMod(new ManiaModKey8(), new ManiaModDualStages()),
                    new ManiaModKey9(),
                    new MultiMod(new ManiaModKey9(), new ManiaModDualStages()),
                }).ToArray();
            }
        }

        private double getHitWindow300(Mod[] mods, double clockRate)
        {
            if (isForCurrentRuleset)
            {
                double anti_od = Math.Min(10.0, Math.Max(0, 10.0 - originalOverallDifficulty));
                return applyModAdjustments(34 + 3 * anti_od, mods, clockRate);
            }

            if (Math.Round(originalOverallDifficulty) > 4)
                return applyModAdjustments(34, mods, clockRate);

            return applyModAdjustments(47, mods, clockRate);

            static double applyModAdjustments(double value, Mod[] mods, double clockRate)
            {   
                value *= clockRate;
                value += 1e-6; // to make sure rounding is correct

                if (mods.Any(m => m is ManiaModHardRock))
                    value /= 1.4;
                else if (mods.Any(m => m is ManiaModEasy))
                    value *= 1.4;

                return ((int)(value) + 0.5) / clockRate;
            }
        }
    }
}
