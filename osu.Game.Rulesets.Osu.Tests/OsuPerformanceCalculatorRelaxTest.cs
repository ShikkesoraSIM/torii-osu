// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Difficulty;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Osu.Tests
{
    [TestFixture]
    public class OsuPerformanceCalculatorRelaxTest
    {
        private static OsuPerformanceAttributes calculate(double cs, double od, double accuracy = 1.0, Mod[]? mods = null)
        {
            var beatmapInfo = new BeatmapInfo
            {
                Difficulty = new BeatmapDifficulty
                {
                    CircleSize = (float)cs,
                    OverallDifficulty = (float)od,
                    ApproachRate = (float)od,
                    DrainRate = 5
                }
            };

            const int total_hits = 1000;
            int greatCount = (int)Math.Round(total_hits * accuracy);
            int okCount = total_hits - greatCount;

            var statistics = new Dictionary<HitResult, int>
            {
                { HitResult.Great, greatCount },
                { HitResult.Ok, okCount },
                { HitResult.Meh, 0 },
                { HitResult.Miss, 0 },
                { HitResult.SliderTailHit, 0 },
                { HitResult.LargeTickMiss, 0 }
            };

            var attributes = new OsuDifficultyAttributes
            {
                AimDifficulty = 4.0,
                SpeedDifficulty = 3.5,
                MaxCombo = total_hits,
                HitCircleCount = total_hits,
                SliderCount = 0,
                SpinnerCount = 0,
                AimDifficultStrainCount = 30,
                SpeedDifficultStrainCount = 30
            };

            var score = new ScoreInfo
            {
                Ruleset = new OsuRuleset().RulesetInfo,
                BeatmapInfo = beatmapInfo,
                Accuracy = accuracy,
                MaxCombo = total_hits,
                Statistics = statistics,
                Mods = mods ?? Array.Empty<Mod>()
            };

            var calc = new OsuPerformanceCalculator();
            return (OsuPerformanceAttributes)calc.Calculate(score, attributes);
        }

        [Test]
        public void TestStandardNomodPlayHasFullAim()
        {
            // CS 4.0, OD 8.5 (standard beatmap)
            var baseAttributes = calculate(4.0, 8.5, 1.0);
            var relaxAttributes = calculate(4.0, 8.5, 1.0, new Mod[] { new OsuModRelax() });

            // Nomod and Relax aim on a standard map should match 1.0x (speed and acc are 0 on relax)
            Assert.That(relaxAttributes.Aim, Is.EqualTo(baseAttributes.Aim).Within(0.001));
            Assert.That(relaxAttributes.Speed, Is.EqualTo(0));
            Assert.That(relaxAttributes.Accuracy, Is.EqualTo(0));
        }

        [Test]
        public void TestHardRockPlayHasFullAim()
        {
            // Standard map with HR (CS 5.2, OD 10)
            var baseAttributes = calculate(5.2, 10.0, 1.0);
            var relaxAttributes = calculate(5.2, 10.0, 1.0, new Mod[] { new OsuModRelax(), new OsuModHardRock() });

            // HR on standard maps has OD 10, which requires tight timing; aim value must not be nerfed
            Assert.That(relaxAttributes.Aim, Is.EqualTo(baseAttributes.Aim).Within(0.001));
        }

        [Test]
        public void TestHighCSWithHighODHasFullAim()
        {
            // Legit small circle map with tight timing (CS 6.5, OD 9.5)
            var baseAttributes = calculate(6.5, 9.5, 1.0);
            var relaxAttributes = calculate(6.5, 9.5, 1.0, new Mod[] { new OsuModRelax() });

            // High CS with high OD is genuine aim skill, not an exploit
            Assert.That(relaxAttributes.Aim, Is.EqualTo(baseAttributes.Aim).Within(0.001));
        }

        [Test]
        public void TestDifficultyAdjustHighCSLowODIsProgressivelyNerfed()
        {
            var baseAttributes = calculate(7.0, 10.0, 1.0);

            var od8 = calculate(7.0, 8.0, 1.0, new Mod[] { new OsuModRelax() });
            var od5 = calculate(7.0, 5.0, 1.0, new Mod[] { new OsuModRelax() });
            var od2 = calculate(7.0, 2.0, 1.0, new Mod[] { new OsuModRelax() });
            var od0 = calculate(7.0, 0.0, 1.0, new Mod[] { new OsuModRelax() });

            // Aim must drop monotonically as OD drops on CS 7
            Assert.That(od8.Aim, Is.LessThan(baseAttributes.Aim));
            Assert.That(od5.Aim, Is.LessThan(od8.Aim));
            Assert.That(od2.Aim, Is.LessThan(od5.Aim));
            Assert.That(od0.Aim, Is.LessThan(od2.Aim));

            // OD 0 on CS 7 should lose ~66% aim value
            double ratioOD0 = od0.Aim / baseAttributes.Aim;
            Assert.That(ratioOD0, Is.InRange(0.30, 0.38));

            // OD 2 on CS 7 should lose ~48% aim value
            double ratioOD2 = od2.Aim / baseAttributes.Aim;
            Assert.That(ratioOD2, Is.InRange(0.48, 0.55));
        }

        [Test]
        public void TestZeroODOnNormalCSHasModestLeniencyAdjustment()
        {
            var od9 = calculate(4.0, 9.0, 1.0, new Mod[] { new OsuModRelax() });
            var od0 = calculate(4.0, 0.0, 1.0, new Mod[] { new OsuModRelax() });

            // On CS 4, OD 0 gives a massive 160ms swipe window, so aim should receive a ~25% adjustment
            double ratio = od0.Aim / od9.Aim;
            Assert.That(ratio, Is.EqualTo(0.75).Within(0.01));
        }

        [Test]
        public void TestRelaxAccuracyScaling()
        {
            var ss = calculate(4.0, 8.5, 1.0, new Mod[] { new OsuModRelax() });
            var acc98 = calculate(4.0, 8.5, 0.98, new Mod[] { new OsuModRelax() });
            var acc95 = calculate(4.0, 8.5, 0.95, new Mod[] { new OsuModRelax() });
            var acc90 = calculate(4.0, 8.5, 0.90, new Mod[] { new OsuModRelax() });

            // 100% SS must not be penalized
            var baseNomod = calculate(4.0, 8.5, 1.0);
            Assert.That(ss.Aim, Is.EqualTo(baseNomod.Aim).Within(0.001));

            // In Relax, 100s count as near-misses via okMultiplier, so lower accuracy progressively drops aim value
            Assert.That(acc98.Aim, Is.LessThan(ss.Aim));
            Assert.That(acc95.Aim, Is.LessThan(acc98.Aim));
            Assert.That(acc90.Aim, Is.LessThan(acc95.Aim));
        }

        [Test]
        public void TestSpunOutPenaltyStillApplies()
        {
            var noSo = calculate(4.0, 8.5, 1.0, new Mod[] { new OsuModRelax() });

            var attributesWithSpinners = new OsuDifficultyAttributes
            {
                AimDifficulty = 4.0,
                SpeedDifficulty = 3.5,
                MaxCombo = 1000,
                HitCircleCount = 800,
                SliderCount = 100,
                SpinnerCount = 100,
                AimDifficultStrainCount = 30,
                SpeedDifficultStrainCount = 30
            };

            var scoreSo = new ScoreInfo
            {
                Ruleset = new OsuRuleset().RulesetInfo,
                BeatmapInfo = new BeatmapInfo
                {
                    Difficulty = new BeatmapDifficulty
                    {
                        CircleSize = 4.0f,
                        OverallDifficulty = 8.5f,
                        ApproachRate = 8.5f,
                        DrainRate = 5
                    }
                },
                Accuracy = 1.0,
                MaxCombo = 1000,
                Statistics = new Dictionary<HitResult, int>
                {
                    { HitResult.Great, 1000 },
                    { HitResult.Ok, 0 },
                    { HitResult.Meh, 0 },
                    { HitResult.Miss, 0 },
                    { HitResult.SliderTailHit, 0 },
                    { HitResult.LargeTickMiss, 0 }
                },
                Mods = new Mod[] { new OsuModRelax(), new OsuModSpunOut() }
            };

            var calc = new OsuPerformanceCalculator();
            var resultSo = (OsuPerformanceAttributes)calc.Calculate(scoreSo, attributesWithSpinners);

            Assert.That(resultSo.Total, Is.LessThan(noSo.Total));
        }
    }
}
