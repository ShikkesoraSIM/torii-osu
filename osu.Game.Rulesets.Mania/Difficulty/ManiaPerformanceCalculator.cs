// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Mania.Difficulty
{
    public class ManiaPerformanceCalculator : PerformanceCalculator
    {
        private int countPerfect;
        private int countGreat;
        private int countGood;
        private int countOk;
        private int countMeh;
        private int countMiss;
        private double scoreAccuracy;

        public ManiaPerformanceCalculator()
            : base(new ManiaRuleset())
        {
        }

        protected override PerformanceAttributes CreatePerformanceAttributes(ScoreInfo score, DifficultyAttributes attributes)
        {
            var maniaAttributes = (ManiaDifficultyAttributes)attributes;

            countPerfect = score.Statistics.GetValueOrDefault(HitResult.Perfect);
            countGreat = score.Statistics.GetValueOrDefault(HitResult.Great);
            countGood = score.Statistics.GetValueOrDefault(HitResult.Good);
            countOk = score.Statistics.GetValueOrDefault(HitResult.Ok);
            countMeh = score.Statistics.GetValueOrDefault(HitResult.Meh);
            countMiss = score.Statistics.GetValueOrDefault(HitResult.Miss);
            scoreAccuracy = calculateCustomAccuracy();

            double multiplier = 1.0;

            if (score.Mods.Any(m => m is ModNoFail))
                multiplier *= 0.75;
            if (score.Mods.Any(m => m is ModEasy))
                multiplier *= 0.90;

            double difficultyValue = computeDifficultyValue(maniaAttributes);
            double varietyMultiplier = this.varietyMultiplier(maniaAttributes.Variety);
            double accMultiplier = this.accMultiplier(scoreAccuracy, maniaAttributes.AccScalar);
            double lengthMultiplier = this.lengthMultiplier(maniaAttributes.TotalNotes, attributes.StarRating);
            double totalValue = difficultyValue * multiplier * varietyMultiplier * accMultiplier * lengthMultiplier;

            return new ManiaPerformanceAttributes
            {
                Difficulty = difficultyValue,
                VarietyMultiplier = varietyMultiplier,
                AccMultiplier = accMultiplier,
                LengthMultiplier = lengthMultiplier,
                Total = totalValue
            };
        }

        private double computeDifficultyValue(ManiaDifficultyAttributes attributes)
        {
            // The "proportion" of pp based on accuracy
            double proportion = calculatePerformanceProportion(scoreAccuracy);

            double difficultyValue = 9.8 * Math.Pow(Math.Max(attributes.StarRating - 0.15, 0.05), 2.2) // Star rating to pp curve
                                     * proportion; // scaled by the proportion

            return difficultyValue;
        }

        private double totalHits => countPerfect + countOk + countGreat + countGood + countMeh + countMiss;

        /// <summary>
        /// Accuracy used to weight judgements independently from the score's actual accuracy.
        /// </summary>
        private double calculateCustomAccuracy()
        {
            if (totalHits == 0)
                return 0;

            return (countPerfect * 305 + countGreat * 300 + countGood * 200 + countOk * 100 + countMeh * 50) / (totalHits * 305);
        }

        private double calculatePerformanceProportion(double acc)
        {

            if (acc > 0.80)
                return 4.5 * (acc-0.8) / Math.Pow(100*(1-acc)+Math.Pow(0.9, 20), 0.05);

            return 0;
        }

        private double varietyMultiplier(double variety)
        {
            double floor = 0.945;
            double cap = 1.055;
            double L = cap - floor;
            double v0 = 3.25;
            double k = 3;

            double sigmoidVariety = floor + L / (1 + Math.Exp(-k * (variety - v0)));
            return sigmoidVariety;
        }

        private double accMultiplier(double acc, double acc_scalar)
        {
            double sigmoid_scaler = 0.87 + 0.26 / (1.0 + Math.Exp(-20 * (acc_scalar - 1)));
            return sigmoid_scaler * (2 * Math.Pow(acc, 20) - 1) + 2 - 2 * Math.Pow(acc, 20);
        }

        private double lengthMultiplier(double totalNotes, double starRating)
        {
            return 1.1 / (1.0 + Math.Sqrt(starRating / (2 * totalNotes)));
        }
    }
}
