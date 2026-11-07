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
        private const int total_hits = 1000;

        private static ScoreInfo createScore(double cs, double od, Mod[]? mods = null, int okCount = 0, int mehCount = 0, int missCount = 0)
        {
            int greatCount = total_hits - okCount - mehCount - missCount;

            return new ScoreInfo
            {
                Ruleset = new OsuRuleset().RulesetInfo,
                BeatmapInfo = new BeatmapInfo
                {
                    Difficulty = new BeatmapDifficulty
                    {
                        CircleSize = (float)cs,
                        OverallDifficulty = (float)od,
                        ApproachRate = 8.5f,
                        DrainRate = 5
                    }
                },
                Accuracy = (greatCount * 300.0 + okCount * 100.0 + mehCount * 50.0) / (total_hits * 300.0),
                MaxCombo = total_hits - missCount,
                Statistics = new Dictionary<HitResult, int>
                {
                    { HitResult.Great, greatCount },
                    { HitResult.Ok, okCount },
                    { HitResult.Meh, mehCount },
                    { HitResult.Miss, missCount },
                    { HitResult.SliderTailHit, 0 },
                    { HitResult.LargeTickMiss, 0 }
                },
                Mods = mods ?? Array.Empty<Mod>()
            };
        }

        private static OsuPerformanceAttributes calculate(double cs, double od, Mod[]? mods = null, int okCount = 0, int mehCount = 0, int missCount = 0,
                                                        int spinnerCount = 0)
        {
            // atributos fijos para aislar el pp; no reemplaza una prueba con mapas reales y el backend.
            var attributes = new OsuDifficultyAttributes
            {
                AimDifficulty = 4.0,
                SpeedDifficulty = 3.5,
                MaxCombo = total_hits,
                HitCircleCount = total_hits - spinnerCount,
                SliderCount = 0,
                SpinnerCount = spinnerCount,
                AimDifficultStrainCount = 30,
                SpeedDifficultStrainCount = 30
            };

            return (OsuPerformanceAttributes)new OsuPerformanceCalculator().Calculate(createScore(cs, od, mods, okCount, mehCount, missCount), attributes);
        }

        [TestCase(0, 1.0)]
        [TestCase(30, 0.98)]
        [TestCase(75, 0.95)]
        [TestCase(150, 0.90)]
        public void TestFixtureAccuracyMatchesHitCounts(int okCount, double expectedAccuracy)
        {
            var score = createScore(4, 8.5, okCount: okCount);

            Assert.That(score.Statistics[HitResult.Great], Is.EqualTo(total_hits - okCount));
            Assert.That(score.Statistics[HitResult.Ok], Is.EqualTo(okCount));
            Assert.That(score.Accuracy, Is.EqualTo(expectedAccuracy).Within(1e-12));
        }

        [TestCase(4.0, 8.5)]
        [TestCase(6.5, 9.5)]
        public void TestHighODHasFullAim(double cs, double od)
        {
            var baseAttributes = calculate(cs, od);
            var relaxAttributes = calculate(cs, od, new Mod[] { new OsuModRelax() });

            Assert.That(relaxAttributes.Aim, Is.EqualTo(baseAttributes.Aim).Within(0.001));
            Assert.That(relaxAttributes.Speed, Is.Zero);
            Assert.That(relaxAttributes.Accuracy, Is.Zero);
        }

        [Test]
        public void TestHardRockPlayHasFullAim()
        {
            var baseAttributes = calculate(4.0, 8.0, new Mod[] { new OsuModHardRock() });
            var relaxAttributes = calculate(4.0, 8.0, new Mod[] { new OsuModRelax(), new OsuModHardRock() });

            Assert.That(relaxAttributes.Aim, Is.EqualTo(baseAttributes.Aim).Within(0.001));
        }

        [Test]
        public void TestDifficultyAdjustHighCSLowODIsProgressivelyNerfed()
        {
            double previousAim = calculate(4, 10).Aim;

            foreach (float od in new[] { 8f, 5f, 2f, 0f })
            {
                var da = new OsuModDifficultyAdjust
                {
                    CircleSize = { Value = 7 },
                    OverallDifficulty = { Value = od }
                };
                var adjusted = calculate(4, 10, new Mod[] { new OsuModRelax(), da });
                var native = calculate(7, od, new Mod[] { new OsuModRelax() });

                Assert.That(adjusted.Aim, Is.EqualTo(native.Aim).Within(0.001), $"da od {od}");
                Assert.That(adjusted.Aim, Is.LessThan(previousAim), $"da od {od}");
                previousAim = adjusted.Aim;
            }

            Assert.That(previousAim / calculate(7, 0).Aim, Is.EqualTo(0.3375).Within(1e-6));
        }

        [TestCase(6f, 3.8f, 1.35, 0.9195253663)]
        [TestCase(6f, 9f, 0.75, 0.9791313483)]
        [TestCase(7f, 2f, 1.5, 0.8406024688)]
        public void TestDifficultyAdjustUsesRateAdjustedOD(float cs, float od, double rate, double expectedAimFactor)
        {
            var da = new OsuModDifficultyAdjust
            {
                CircleSize = { Value = cs },
                OverallDifficulty = { Value = od }
            };
            ModRateAdjust rateMod = rate < 1 ? new OsuModHalfTime() : new OsuModDoubleTime();
            rateMod.SpeedChange.Value = rate;

            var baseline = calculate(4, 10, new Mod[] { da, rateMod });
            var relax = calculate(4, 10, new Mod[] { new OsuModRelax(), da, rateMod });

            Assert.That(relax.Aim / baseline.Aim, Is.EqualTo(expectedAimFactor).Within(1e-6));
        }

        [Test]
        public void TestEasyUsesPostModStats()
        {
            var easy = calculate(4, 8, new Mod[] { new OsuModRelax(), new OsuModEasy() });
            var native = calculate(2, 4, new Mod[] { new OsuModRelax() });
            var baseline = calculate(4, 8, new Mod[] { new OsuModEasy() });

            Assert.That(easy.Aim, Is.EqualTo(native.Aim).Within(0.001));
            Assert.That(easy.Aim / baseline.Aim, Is.EqualTo(0.9116116524).Within(1e-6));
        }

        [TestCase(4, 5, 0.9425900842)]
        [TestCase(4, 0, 0.75)]
        [TestCase(7, 0, 0.3375)]
        public void TestNativeLowODIsAlsoAdjusted(double cs, double od, double expectedAimFactor)
        {
            var baseline = calculate(cs, od);
            var relax = calculate(cs, od, new Mod[] { new OsuModRelax() });

            Assert.That(relax.Aim / baseline.Aim, Is.EqualTo(expectedAimFactor).Within(1e-6));
        }

        [TestCase(4)]
        [TestCase(7)]
        public void TestAimIsContinuousAtCircleSizeBoundaries(double cs)
        {
            const double delta = 0.001;
            var lower = calculate(cs - delta, 5, new Mod[] { new OsuModRelax() });
            var upper = calculate(cs + delta, 5, new Mod[] { new OsuModRelax() });

            Assert.That(lower.Aim / upper.Aim, Is.EqualTo(1).Within(0.001));
        }

        [TestCase(4.9, 5)]
        [TestCase(7.9, 8)]
        [TestCase(8.4, 8.5)]
        public void TestSameRoundedHitWindowHasSameAim(double lowerOD, double upperOD)
        {
            var lower = calculate(6, lowerOD, new Mod[] { new OsuModRelax() });
            var upper = calculate(6, upperOD, new Mod[] { new OsuModRelax() });

            Assert.That(lower.Aim, Is.EqualTo(upper.Aim).Within(0.001));
        }

        [Test]
        public void TestRelaxAccuracyScaling()
        {
            var mods = new Mod[] { new OsuModRelax() };
            var ss = calculate(4, 8.5, mods);
            var acc98 = calculate(4, 8.5, mods, okCount: 30);
            var acc95 = calculate(4, 8.5, mods, okCount: 75);
            var acc90 = calculate(4, 8.5, mods, okCount: 150);

            Assert.That(acc98.Aim, Is.LessThan(ss.Aim));
            Assert.That(acc95.Aim, Is.LessThan(acc98.Aim));
            Assert.That(acc90.Aim, Is.LessThan(acc95.Aim));
        }

        [TestCase(0, 1, 0)]
        [TestCase(0, 0, 1)]
        public void TestMehsAndMissesReduceRelaxAim(int okCount, int mehCount, int missCount)
        {
            var mods = new Mod[] { new OsuModRelax() };
            var ss = calculate(4, 8.5, mods);
            var imperfect = calculate(4, 8.5, mods, okCount, mehCount, missCount);

            Assert.That(imperfect.Aim, Is.LessThan(ss.Aim));
        }

        [Test]
        public void TestSpunOutPenaltyStillApplies()
        {
            const int spinner_count = 100;
            var noSo = calculate(4, 8.5, new Mod[] { new OsuModRelax() }, spinnerCount: spinner_count);
            var withSo = calculate(4, 8.5, new Mod[] { new OsuModRelax(), new OsuModSpunOut() }, spinnerCount: spinner_count);

            Assert.That(withSo.Total / noSo.Total, Is.EqualTo(1 - Math.Pow((double)spinner_count / total_hits, 0.85)).Within(1e-6));
        }
    }
}
