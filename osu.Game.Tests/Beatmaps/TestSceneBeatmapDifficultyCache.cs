// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Tests.Beatmaps.IO;
using osu.Game.Tests.Resources;
using osu.Game.Tests.Visual;

namespace osu.Game.Tests.Beatmaps
{
    [HeadlessTest]
    public partial class TestSceneBeatmapDifficultyCache : OsuTestScene
    {
        public const double BASE_STARS = 5.55;

        private static readonly Guid guid = Guid.NewGuid();

        private BeatmapSetInfo importedSet;

        private TestBeatmapDifficultyCache difficultyCache;

        private IBindable<StarDifficulty> starDifficultyBindable;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; }

        [Resolved]
        private BeatmapDifficultyCache actualDifficultyCache { get; set; }

        [BackgroundDependencyLoader]
        private void load(OsuGameBase osu)
        {
            importedSet = BeatmapImportHelper.LoadQuickOszIntoOsu(osu).GetResultSafely();
        }

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("setup difficulty cache", () =>
            {
                SelectedMods.Value = Array.Empty<Mod>();

                Child = difficultyCache = new TestBeatmapDifficultyCache();

                starDifficultyBindable = difficultyCache.GetBindableDifficulty(importedSet.Beatmaps.First());
            });

            AddUntilStep($"star difficulty -> {BASE_STARS}", () => starDifficultyBindable.Value.Stars == BASE_STARS);
        }

        [Test]
        [FlakyTest] // one fix attempted in https://github.com/ppy/osu/pull/37178, didn't work
        public void TestInvalidationFlow()
        {
            BeatmapInfo postEditBeatmapInfo = null;
            BeatmapInfo preEditBeatmapInfo = null;

            IBindable<StarDifficulty> bindableDifficulty = null;

            AddStep("get bindable stars", () =>
            {
                preEditBeatmapInfo = importedSet.Beatmaps.First();
                bindableDifficulty = actualDifficultyCache.GetBindableDifficulty(preEditBeatmapInfo);
            });

            AddUntilStep("wait for stars retrieved", () => bindableDifficulty.Value.Stars, () => Is.GreaterThan(0));

            AddStep("remove all hitobjects", () =>
            {
                var working = beatmapManager.GetWorkingBeatmap(preEditBeatmapInfo);

                ((IList<HitObject>)working.Beatmap.HitObjects).Clear();

                beatmapManager.Save(working.BeatmapInfo, working.Beatmap);
                postEditBeatmapInfo = working.BeatmapInfo;
            });

            AddAssert("stars is now zero", () => actualDifficultyCache.GetDifficultyAsync(postEditBeatmapInfo).GetResultSafely()!.Value.Stars, () => Is.Zero);
            AddUntilStep("bindable stars is now zero", () => bindableDifficulty.Value.Stars, () => Is.Zero);
        }

        [Test]
        public void TestStarDifficultyChangesOnModSettings()
        {
            OsuModDoubleTime dt = null;

            AddStep("set computation function", () => difficultyCache.ComputeDifficulty = lookup =>
            {
                var modRateAdjust = (ModRateAdjust)lookup.OrderedMods.SingleOrDefault(mod => mod is ModRateAdjust);
                return new StarDifficulty(BASE_STARS + modRateAdjust?.SpeedChange.Value ?? 0, 0);
            });

            AddStep("change selected mod to DT", () => SelectedMods.Value = new[] { dt = new OsuModDoubleTime { SpeedChange = { Value = 1.5 } } });
            AddUntilStep($"star difficulty -> {BASE_STARS + 1.5}", () => starDifficultyBindable.Value.Stars == BASE_STARS + 1.5);

            AddStep("change DT speed to 1.25", () => dt.SpeedChange.Value = 1.25);
            AddUntilStep($"star difficulty -> {BASE_STARS + 1.25}", () => starDifficultyBindable.Value.Stars == BASE_STARS + 1.25);

            AddStep("change selected mod to NC", () => SelectedMods.Value = new[] { new OsuModNightcore { SpeedChange = { Value = 1.75 } } });
            AddUntilStep($"star difficulty -> {BASE_STARS + 1.75}", () => starDifficultyBindable.Value.Stars == BASE_STARS + 1.75);
        }

        [Test]
        public void TestStarDifficultyChangesOnModSettingsCorrectlyTrackAcrossReferenceChanges()
        {
            OsuModDoubleTime dt = null;

            AddStep("set computation function", () => difficultyCache.ComputeDifficulty = lookup =>
            {
                var modRateAdjust = (ModRateAdjust)lookup.OrderedMods.SingleOrDefault(mod => mod is ModRateAdjust);
                return new StarDifficulty(BASE_STARS + modRateAdjust?.SpeedChange.Value ?? 0, 0);
            });

            AddStep("change selected mod to DT", () => SelectedMods.Value = new[] { dt = new OsuModDoubleTime { SpeedChange = { Value = 1.5 } } });
            AddUntilStep($"star difficulty -> {BASE_STARS + 1.5}", () => starDifficultyBindable.Value.Stars == BASE_STARS + 1.5);

            AddStep("change DT speed to 1.25", () => dt.SpeedChange.Value = 1.25);
            AddUntilStep($"star difficulty -> {BASE_STARS + 1.25}", () => starDifficultyBindable.Value.Stars == BASE_STARS + 1.25);

            AddStep("reconstruct DT mod with same settings", () => SelectedMods.Value = new[] { dt = (OsuModDoubleTime)dt.DeepClone() });
            AddUntilStep($"star difficulty -> {BASE_STARS + 1.25}", () => starDifficultyBindable.Value.Stars == BASE_STARS + 1.25);

            AddStep("change DT speed to 1.25", () => dt.SpeedChange.Value = 2);
            AddUntilStep($"star difficulty -> {BASE_STARS + 2}", () => starDifficultyBindable.Value.Stars == BASE_STARS + 2);
        }

        [Test]
        public void TestStarDifficultyAdjustHashCodeConflict()
        {
            OsuModDifficultyAdjust difficultyAdjust = null;

            AddStep("set computation function", () => difficultyCache.ComputeDifficulty = lookup =>
            {
                var modDifficultyAdjust = (ModDifficultyAdjust)lookup.OrderedMods.SingleOrDefault(mod => mod is ModDifficultyAdjust);
                return new StarDifficulty(BASE_STARS * (modDifficultyAdjust?.OverallDifficulty.Value ?? 1), 0);
            });

            AddStep("change selected mod to DA", () => SelectedMods.Value = new[] { difficultyAdjust = new OsuModDifficultyAdjust() });
            AddUntilStep($"star difficulty -> {BASE_STARS}", () => starDifficultyBindable.Value.Stars == BASE_STARS);

            AddStep("change DA difficulty to 0.5", () => difficultyAdjust.OverallDifficulty.Value = 0.5f);
            AddUntilStep($"star difficulty -> {BASE_STARS * 0.5f}", () => starDifficultyBindable.Value.Stars == BASE_STARS / 2);

            // hash code of 0 (the value) conflicts with the hash code of null (the initial/default value).
            // it's important that the mod reference and its underlying bindable references stay the same to demonstrate this failure.
            AddStep("change DA difficulty to 0", () => difficultyAdjust.OverallDifficulty.Value = 0);
            AddUntilStep("star difficulty -> 0", () => starDifficultyBindable.Value.Stars == 0);
        }

        [Test]
        public void TestKeyEqualsWithDifferentModInstances()
        {
            var key1 = new BeatmapDifficultyCache.DifficultyCacheLookup(new BeatmapInfo { ID = guid }, new RulesetInfo { OnlineID = 0 }, new Mod[] { new OsuModHardRock(), new OsuModHidden() });
            var key2 = new BeatmapDifficultyCache.DifficultyCacheLookup(new BeatmapInfo { ID = guid }, new RulesetInfo { OnlineID = 0 }, new Mod[] { new OsuModHardRock(), new OsuModHidden() });

            Assert.That(key1, Is.EqualTo(key2));
            Assert.That(key1.GetHashCode(), Is.EqualTo(key2.GetHashCode()));
        }

        [Test]
        public void TestKeyEqualsWithDifferentModOrder()
        {
            var key1 = new BeatmapDifficultyCache.DifficultyCacheLookup(new BeatmapInfo { ID = guid }, new RulesetInfo { OnlineID = 0 }, new Mod[] { new OsuModHardRock(), new OsuModHidden() });
            var key2 = new BeatmapDifficultyCache.DifficultyCacheLookup(new BeatmapInfo { ID = guid }, new RulesetInfo { OnlineID = 0 }, new Mod[] { new OsuModHidden(), new OsuModHardRock() });

            Assert.That(key1, Is.EqualTo(key2));
            Assert.That(key1.GetHashCode(), Is.EqualTo(key2.GetHashCode()));
        }

        [Test]
        public void TestKeyDoesntEqualWithDifferentModSettings()
        {
            var key1 = new BeatmapDifficultyCache.DifficultyCacheLookup(new BeatmapInfo { ID = guid }, new RulesetInfo { OnlineID = 0 },
                new Mod[] { new OsuModDoubleTime { SpeedChange = { Value = 1.1 } } });
            var key2 = new BeatmapDifficultyCache.DifficultyCacheLookup(new BeatmapInfo { ID = guid }, new RulesetInfo { OnlineID = 0 },
                new Mod[] { new OsuModDoubleTime { SpeedChange = { Value = 1.9 } } });

            Assert.That(key1, Is.Not.EqualTo(key2));
            Assert.That(key1.GetHashCode(), Is.Not.EqualTo(key2.GetHashCode()));
        }

        [Test]
        public void TestKeyEqualWithMatchingModSettings()
        {
            var key1 = new BeatmapDifficultyCache.DifficultyCacheLookup(new BeatmapInfo { ID = guid }, new RulesetInfo { OnlineID = 0 },
                new Mod[] { new OsuModDoubleTime { SpeedChange = { Value = 1.25 } } });
            var key2 = new BeatmapDifficultyCache.DifficultyCacheLookup(new BeatmapInfo { ID = guid }, new RulesetInfo { OnlineID = 0 },
                new Mod[] { new OsuModDoubleTime { SpeedChange = { Value = 1.25 } } });

            Assert.That(key1, Is.EqualTo(key2));
            Assert.That(key1.GetHashCode(), Is.EqualTo(key2.GetHashCode()));
        }

        [TestCase(1.3, DifficultyRating.Easy)]
        [TestCase(1.993, DifficultyRating.Easy)]
        [TestCase(1.998, DifficultyRating.Normal)]
        [TestCase(2.4, DifficultyRating.Normal)]
        [TestCase(2.693, DifficultyRating.Normal)]
        [TestCase(2.698, DifficultyRating.Hard)]
        [TestCase(3.5, DifficultyRating.Hard)]
        [TestCase(3.993, DifficultyRating.Hard)]
        [TestCase(3.997, DifficultyRating.Insane)]
        [TestCase(5.0, DifficultyRating.Insane)]
        [TestCase(5.292, DifficultyRating.Insane)]
        [TestCase(5.297, DifficultyRating.Expert)]
        [TestCase(6.2, DifficultyRating.Expert)]
        [TestCase(6.493, DifficultyRating.Expert)]
        [TestCase(6.498, DifficultyRating.ExpertPlus)]
        [TestCase(8.3, DifficultyRating.ExpertPlus)]
        public void TestDifficultyRatingMapping(double starRating, DifficultyRating expectedBracket)
        {
            var actualBracket = StarDifficulty.GetDifficultyRating(starRating);

            ClassicAssert.AreEqual(expectedBracket, actualBracket);
        }

        [Test]
        public void TestGetExistingDifficulty()
        {
            var testCache = new TestBeatmapDifficultyCache();
            var bInfo = new BeatmapInfo { ID = Guid.NewGuid(), StarRating = 4.0, Ruleset = new RulesetInfo { OnlineID = 0 } };
            var mods = new Mod[] { new OsuModDoubleTime() };

            // Not yet populated in cache
            Assert.That(testCache.GetExistingDifficulty(bInfo, null, mods), Is.Null);

            // Populate cache
            testCache.ComputeDifficulty = _ => new StarDifficulty(6.0, 0);
            var computed = testCache.GetDifficultyAsync(bInfo, null, mods).GetResultSafely();
            Assert.That(computed, Is.Not.Null);

            // Now existing difficulty returns the cached StarDifficulty
            var existing = testCache.GetExistingDifficulty(bInfo, null, mods);
            Assert.That(existing, Is.Not.Null);
            Assert.That(existing.Value.Stars, Is.EqualTo(6.0));
        }

        [Test]
        public void TestGetEffectiveStarRatingFallbackToApproximation()
        {
            var testCache = new TestBeatmapDifficultyCache();
            var bInfo = new BeatmapInfo { ID = Guid.NewGuid(), StarRating = 4.0, Ruleset = new RulesetInfo { OnlineID = 0 } };

            // Nomod returns base rating
            Assert.That(testCache.GetEffectiveStarRating(bInfo), Is.EqualTo(4.0));

            // Non-difficulty mods return base rating
            var nonDiffMods = new Mod[] { new OsuModNoFail(), new OsuModAutoplay() };
            Assert.That(testCache.GetEffectiveStarRating(bInfo, null, nonDiffMods), Is.EqualTo(4.0));

            // DT without cache: falls back to approximation
            var dtMods = new Mod[] { new OsuModDoubleTime() };
            double effectiveDt = testCache.GetEffectiveStarRating(bInfo, null, dtMods);
            Assert.That(effectiveDt, Is.GreaterThan(5.0).And.LessThan(6.5));

            // Once in cache, returns exact cached value
            testCache.ComputeDifficulty = _ => new StarDifficulty(7.2, 0);
            testCache.GetDifficultyAsync(bInfo, null, dtMods).GetResultSafely();
            Assert.That(testCache.GetEffectiveStarRating(bInfo, null, dtMods), Is.EqualTo(7.2));
        }

        /// <summary>
        /// Mods which change star rating in the difficulty calculator without applying to difficulty settings or rate.
        /// </summary>
        public static readonly object[] DIFFICULTY_MODS_WITHOUT_DIFFICULTY_INTERFACES =
        {
            new OsuModRelax(),
            new OsuModTouchDevice(),
            new OsuModAutopilot(),
            new OsuModMagnetised(),
            new OsuModFreezeFrame(),
        };

        private static readonly object[] non_difficulty_mods =
        {
            new OsuModNoFail(),
            new OsuModSuddenDeath(),
            new OsuModPerfect(),
            new OsuModSpunOut(),
            new OsuModClassic(),
            new OsuModStrictTracking(),
            new OsuModAutoplay(),
            new OsuModBlinds(),
            new OsuModTraceable(),
        };

        [TestCaseSource(nameof(DIFFICULTY_MODS_WITHOUT_DIFFICULTY_INTERFACES))]
        public void TestModAffectsDifficulty(Mod mod)
            => Assert.That(StarRatingApproximator.ModAffectsDifficulty(mod), Is.True);

        [TestCaseSource(nameof(non_difficulty_mods))]
        public void TestModDoesNotAffectDifficulty(Mod mod)
            => Assert.That(StarRatingApproximator.ModAffectsDifficulty(mod), Is.False);

        [TestCaseSource(nameof(DIFFICULTY_MODS_WITHOUT_DIFFICULTY_INTERFACES))]
        public void TestGetEffectiveStarRatingUsesCachedValue(Mod mod)
        {
            var testCache = new TestBeatmapDifficultyCache();
            var bInfo = new BeatmapInfo { ID = Guid.NewGuid(), StarRating = 4.0, Ruleset = new RulesetInfo { OnlineID = 0 } };
            var mods = new[] { mod };

            testCache.ComputeDifficulty = _ => new StarDifficulty(6.0, 0);
            testCache.GetDifficultyAsync(bInfo, null, mods).GetResultSafely();

            Assert.That(testCache.GetEffectiveStarRating(bInfo, null, mods), Is.EqualTo(6.0));
            Assert.That(testCache.CreateEffectiveStarRatingLookup(null, mods).GetEffectiveStarRating(bInfo), Is.EqualTo(6.0));
        }

        [Test]
        public void TestKeyFromOrderedModsEqualsStandardKey()
        {
            var key1 = new BeatmapDifficultyCache.DifficultyCacheLookup(new BeatmapInfo { ID = guid }, new RulesetInfo { OnlineID = 0 },
                new Mod[] { new OsuModHardRock(), new OsuModDoubleTime { SpeedChange = { Value = 1.3 } } });
            var key2 = BeatmapDifficultyCache.DifficultyCacheLookup.FromOrderedMods(new BeatmapInfo { ID = guid }, new RulesetInfo { OnlineID = 0 },
                BeatmapDifficultyCache.DifficultyCacheLookup.OrderMods(new Mod[] { new OsuModDoubleTime { SpeedChange = { Value = 1.3 } }, new OsuModHardRock() }));

            Assert.That(key1, Is.EqualTo(key2));
            Assert.That(key1.GetHashCode(), Is.EqualTo(key2.GetHashCode()));
        }

        [Test]
        public void TestEffectiveStarRatingLookupMatchesGetEffectiveStarRating()
        {
            var testCache = new TestBeatmapDifficultyCache();
            var bInfo = createDetachedBeatmap();

            testCache.ComputeDifficulty = _ => new StarDifficulty(7.2, 0);
            testCache.GetDifficultyAsync(bInfo, null, new Mod[] { new OsuModDoubleTime { SpeedChange = { Value = 1.3 } }, new OsuModHardRock() }).GetResultSafely();

            // Mods given in a different order, as different instances, still find the exact cached value.
            var cachedMods = new Mod[] { new OsuModHardRock(), new OsuModDoubleTime { SpeedChange = { Value = 1.3 } } };
            Assert.That(testCache.CreateEffectiveStarRatingLookup(null, cachedMods).GetEffectiveStarRating(bInfo), Is.EqualTo(7.2));

            // A different speed isn't cached, so the same approximation as GetEffectiveStarRating is used.
            var uncachedMods = new Mod[] { new OsuModHardRock(), new OsuModDoubleTime { SpeedChange = { Value = 1.5 } } };
            double approximated = testCache.CreateEffectiveStarRatingLookup(null, uncachedMods).GetEffectiveStarRating(bInfo);
            Assert.That(approximated, Is.Not.EqualTo(7.2));
            Assert.That(approximated, Is.EqualTo(testCache.GetEffectiveStarRating(bInfo, null, uncachedMods)));

            // Without difficulty-altering mods, the base star rating is returned.
            Assert.That(testCache.CreateEffectiveStarRatingLookup(null, null).GetEffectiveStarRating(bInfo), Is.EqualTo(4.0));
            Assert.That(testCache.CreateEffectiveStarRatingLookup(null, new Mod[] { new OsuModNoFail() }).GetEffectiveStarRating(bInfo), Is.EqualTo(4.0));
        }

        [Test]
        public void TestApproximateStarRatingFormulas()
        {
            var bInfo = new BeatmapInfo { StarRating = 4.0 };

            // Nomod / empty mods
            Assert.That(StarRatingApproximator.Approximate(bInfo, null, Array.Empty<Mod>()), Is.EqualTo(4.0));

            // HardRock: increases difficulty
            double hrStars = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { new OsuModHardRock() });
            Assert.That(hrStars, Is.GreaterThan(4.3).And.LessThan(4.7));

            // Easy: decreases difficulty, by 0-10% on real ~4★ beatmaps with CS, AR and OD 4-6 (10th-90th percentile of the exact calculator)
            double ezStars = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { new OsuModEasy() });
            Assert.That(ezStars, Is.GreaterThan(3.55).And.LessThan(4.0));

            // Flashlight: increases difficulty
            double flStars = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { new OsuModFlashlight() });
            Assert.That(flStars, Is.GreaterThan(4.4).And.LessThan(5.0));

            // Hidden: reading increase, 13-22% on real ~4★ beatmaps with CS, AR and OD 4-6 (10th-90th percentile of the exact calculator)
            double hdStars = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { new OsuModHidden() });
            Assert.That(hdStars, Is.GreaterThan(4.5).And.LessThan(4.9));

            // DoubleTime: scaled by SR physics
            double dtStars = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { new OsuModDoubleTime() });
            Assert.That(dtStars, Is.GreaterThan(5.2).And.LessThan(6.2));

            // HalfTime: reduced by SR physics
            double htStars = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { new OsuModHalfTime() });
            Assert.That(htStars, Is.GreaterThan(2.9).And.LessThan(3.5));

            // Combined DT + HR: higher than DT alone
            double dtHrStars = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { new OsuModDoubleTime(), new OsuModHardRock() });
            Assert.That(dtHrStars, Is.GreaterThan(dtStars));

            // 0 star map edge case
            var zeroInfo = new BeatmapInfo { StarRating = 0.0 };
            Assert.That(StarRatingApproximator.Approximate(zeroInfo, null, new Mod[] { new OsuModDoubleTime() }), Is.EqualTo(0.0));
        }

        [Test]
        public void TestApproximateStarRatingDifficultyAdjust()
        {
            var bInfo = new BeatmapInfo
            {
                StarRating = 4.0,
                Difficulty = new BeatmapDifficulty { CircleSize = 4, ApproachRate = 9, OverallDifficulty = 8, DrainRate = 5 },
            };

            double approximate(Action<OsuModDifficultyAdjust> configure)
            {
                var difficultyAdjust = new OsuModDifficultyAdjust { ExtendedLimits = { Value = true } };
                configure(difficultyAdjust);
                return StarRatingApproximator.Approximate(bInfo, null, new Mod[] { difficultyAdjust });
            }

            // No overrides, overrides matching the beatmap and HP (unused by osu! star rating) leave it unchanged.
            Assert.That(approximate(_ => { }), Is.EqualTo(4.0));
            Assert.That(approximate(da =>
            {
                da.CircleSize.Value = 4;
                da.ApproachRate.Value = 9;
                da.OverallDifficulty.Value = 8;
                da.DrainRate.Value = 10;
            }), Is.EqualTo(4.0).Within(1e-9));

            // Ranges are around the median response of the exact calculator measured on real beatmaps.
            Assert.That(approximate(da => da.CircleSize.Value = 0) / 4, Is.InRange(0.80, 0.90));
            Assert.That(approximate(da => da.CircleSize.Value = 7) / 4, Is.InRange(1.25, 1.45));
            Assert.That(approximate(da => da.CircleSize.Value = 11) / 4, Is.InRange(3.4, 4.4));

            // Lowering approach rate raises star rating, increasingly so the further it goes.
            double ar5 = approximate(da => da.ApproachRate.Value = 5);
            double ar0 = approximate(da => da.ApproachRate.Value = 0);
            double arMinus10 = approximate(da => da.ApproachRate.Value = -10);
            Assert.That(ar5, Is.GreaterThan(4.0));
            Assert.That(ar0, Is.GreaterThan(ar5));
            Assert.That(ar0 / 4, Is.InRange(1.2, 1.4));
            Assert.That(arMinus10 / 4, Is.InRange(1.35, 1.55));

            // Raising it barely matters up to AR 10, then jumps once the preempt drops below 450ms.
            Assert.That(approximate(da => da.ApproachRate.Value = 10) / 4, Is.InRange(0.97, 1.01));
            Assert.That(approximate(da => da.ApproachRate.Value = 11) / 4, Is.InRange(1.10, 1.25));

            Assert.That(approximate(da => da.OverallDifficulty.Value = 10) / 4, Is.InRange(1.0, 1.02));
            Assert.That(approximate(da => da.OverallDifficulty.Value = 0) / 4, Is.InRange(0.96, 1.0));
        }

        [TestCase(2f, 10f, 3f)]
        [TestCase(5.5f, 7f, 6f)]
        [TestCase(7f, 9.5f, 10f)]
        public void TestApproximateStarRatingDifficultyAdjustMatchingBeatmapIsUnchanged(float circleSize, float approachRate, float overallDifficulty)
        {
            var bInfo = new BeatmapInfo
            {
                StarRating = 5.0,
                Difficulty = new BeatmapDifficulty { CircleSize = circleSize, ApproachRate = approachRate, OverallDifficulty = overallDifficulty },
            };

            var difficultyAdjust = new OsuModDifficultyAdjust
            {
                CircleSize = { Value = circleSize },
                ApproachRate = { Value = approachRate },
                OverallDifficulty = { Value = overallDifficulty },
            };

            Assert.That(StarRatingApproximator.Approximate(bInfo, null, new Mod[] { difficultyAdjust }), Is.EqualTo(5.0).Within(1e-9));
        }

        [TestCase(2f)]
        [TestCase(4f)]
        [TestCase(7f)]
        public void TestApproximateStarRatingDifficultyAdjustCircleSizeIsMonotonic(float baseCircleSize)
        {
            var bInfo = new BeatmapInfo
            {
                StarRating = 5.0,
                Difficulty = new BeatmapDifficulty { CircleSize = baseCircleSize, ApproachRate = 9, OverallDifficulty = 8 },
            };

            double previous = 0;

            for (float circleSize = 0; circleSize <= 11; circleSize += 0.1f)
            {
                var difficultyAdjust = new OsuModDifficultyAdjust { ExtendedLimits = { Value = true }, CircleSize = { Value = circleSize } };
                double stars = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { difficultyAdjust });

                Assert.That(stars, Is.GreaterThan(previous), $"star rating went down at circle size {circleSize}");
                previous = stars;
            }
        }

        // Ranges are around the fitted response, inside the 1st-99th percentile of the exact calculator's response measured on real beatmaps.
        private static readonly object[] skill_mod_ranges =
        {
            new object[] { new OsuModRelax(), 1.0, 0.77, 0.81 },
            new object[] { new OsuModRelax(), 4.0, 0.70, 0.76 },
            new object[] { new OsuModRelax(), 15.0, 0.64, 0.70 },
            new object[] { new OsuModAutopilot(), 1.0, 0.37, 0.42 },
            new object[] { new OsuModAutopilot(), 4.0, 0.44, 0.49 },
            new object[] { new OsuModAutopilot(), 15.0, 0.51, 0.56 },
            new object[] { new OsuModTouchDevice(), 1.0, 0.87, 0.91 },
            new object[] { new OsuModTouchDevice(), 4.0, 0.85, 0.89 },
            new object[] { new OsuModTouchDevice(), 15.0, 0.83, 0.88 },
            new object[] { new OsuModMagnetised(), 1.0, 0.71, 0.76 },
            new object[] { new OsuModMagnetised(), 4.0, 0.77, 0.82 },
            new object[] { new OsuModMagnetised(), 15.0, 0.83, 0.88 },
            new object[] { new OsuModFreezeFrame(), 1.0, 1.29, 1.35 },
            new object[] { new OsuModFreezeFrame(), 4.0, 1.11, 1.17 },
            new object[] { new OsuModFreezeFrame(), 15.0, 1.0, 1.0 },
        };

        [TestCaseSource(nameof(skill_mod_ranges))]
        public void TestApproximateStarRatingSkillMods(Mod mod, double stars, double minimumRatio, double maximumRatio)
        {
            var bInfo = new BeatmapInfo { StarRating = stars };
            Assert.That(StarRatingApproximator.Approximate(bInfo, null, new[] { mod }) / stars, Is.InRange(minimumRatio, maximumRatio));
        }

        private static readonly object[] skill_mods =
        {
            new OsuModRelax(),
            new OsuModAutopilot(),
            new OsuModTouchDevice(),
            new OsuModMagnetised { AttractionStrength = { Value = 0.05f } },
            new OsuModMagnetised(),
            new OsuModMagnetised { AttractionStrength = { Value = 1 } },
            new OsuModFreezeFrame(),
        };

        [TestCaseSource(nameof(skill_mods))]
        public void TestApproximateStarRatingSkillModsKeepNomodOrder(Mod mod)
        {
            double previous = 0;

            for (double baseStars = 0.1; baseStars <= 15; baseStars += 0.1)
            {
                double stars = StarRatingApproximator.Approximate(new BeatmapInfo { StarRating = baseStars }, null, new[] { mod });

                // Freeze Frame never lowered star rating in the exact calculator; the others only scale skills down.
                if (mod is OsuModFreezeFrame)
                    Assert.That(stars, Is.GreaterThanOrEqualTo(baseStars), $"star rating went down at {baseStars}★");
                else
                    Assert.That(stars, Is.LessThanOrEqualTo(baseStars), $"star rating went up at {baseStars}★");

                Assert.That(stars, Is.GreaterThan(previous), $"order changed at {baseStars}★");
                previous = stars;
            }
        }

        [Test]
        public void TestApproximateStarRatingMagnetisedStrength()
        {
            var bInfo = new BeatmapInfo { StarRating = 4.0 };
            double previous = 4.0;

            for (int i = 1; i <= 20; i++)
            {
                float strength = i * 0.05f;
                double stars = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { new OsuModMagnetised { AttractionStrength = { Value = strength } } });
                Assert.That(stars, Is.LessThan(previous), $"star rating didn't go down at strength {strength}");
                previous = stars;
            }

            Assert.That(StarRatingApproximator.Approximate(bInfo, null, new Mod[] { new OsuModMagnetised { AttractionStrength = { Value = 0.05f } } }) / 4, Is.InRange(0.97, 0.99));
            Assert.That(previous / 4, Is.InRange(0.60, 0.67));
        }

        [TestCaseSource(nameof(skill_mods))]
        public void TestApproximateStarRatingSkillModsMultiplyWithDifficultyAdjust(Mod mod)
        {
            var bInfo = new BeatmapInfo
            {
                StarRating = 5.0,
                Difficulty = new BeatmapDifficulty { CircleSize = 4, ApproachRate = 9, OverallDifficulty = 8 },
            };

            var difficultyAdjust = new OsuModDifficultyAdjust { CircleSize = { Value = 6 }, ApproachRate = { Value = 7 } };

            double adjusted = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { difficultyAdjust });
            double modFactor = StarRatingApproximator.Approximate(bInfo, null, new[] { mod }) / 5;

            Assert.That(StarRatingApproximator.Approximate(bInfo, null, new[] { difficultyAdjust, mod }), Is.EqualTo(adjusted * modFactor).Within(1e-9));
        }

        [Test]
        public void TestApproximateStarRatingReflectsRateSlider()
        {
            var beatmap = TestResources.CreateTestBeatmapSetInfo(1).Beatmaps[0];
            beatmap.StarRating = 5.0;

            var dt12 = new OsuModDoubleTime { SpeedChange = { Value = 1.2 } };
            var dt15 = new OsuModDoubleTime { SpeedChange = { Value = 1.5 } };
            var dt18 = new OsuModDoubleTime { SpeedChange = { Value = 1.8 } };

            double sr12 = StarRatingApproximator.Approximate(beatmap, null, new[] { dt12 });
            double sr15 = StarRatingApproximator.Approximate(beatmap, null, new[] { dt15 });
            double sr18 = StarRatingApproximator.Approximate(beatmap, null, new[] { dt18 });

            Assert.That(sr12, Is.LessThan(sr15));
            Assert.That(sr15, Is.LessThan(sr18));
        }

        private static Mod rateMod(double rate) => rate > 1
            ? new OsuModDoubleTime { SpeedChange = { Value = rate } }
            : new OsuModHalfTime { SpeedChange = { Value = rate } };

        private static BeatmapInfo osuBeatmap(double stars, float approachRate) => new BeatmapInfo
        {
            StarRating = stars,
            Difficulty = new BeatmapDifficulty { CircleSize = 4, ApproachRate = approachRate, OverallDifficulty = 8 },
        };

        // Ranges are the 10th-90th percentile of the exact calculator's response, measured on real beatmaps near that star rating and approach rate.
        private static readonly object[] rate_ranges =
        {
            new object[] { new[] { rateMod(1.2) }, 5.0, 9f, 1.16, 1.18 },
            new object[] { new[] { rateMod(1.5) }, 2.5, 6f, 1.34, 1.40 },
            new object[] { new[] { rateMod(1.5) }, 5.0, 9f, 1.41, 1.49 },
            new object[] { new[] { rateMod(1.5) }, 7.0, 10f, 1.55, 1.71 },
            new object[] { new[] { rateMod(2.0) }, 5.0, 9f, 1.96, 2.14 },
            new object[] { new[] { rateMod(2.0) }, 7.0, 10f, 2.23, 2.57 },
            new object[] { new[] { rateMod(0.75) }, 5.0, 9f, 0.78, 0.80 },
            new object[] { new[] { rateMod(0.5) }, 5.0, 9f, 0.56, 0.60 },
            new object[] { new Mod[] { rateMod(1.5), new OsuModHardRock() }, 5.0, 9f, 1.63, 1.75 },
            new object[] { new Mod[] { rateMod(1.5), new OsuModHardRock() }, 7.0, 10f, 1.65, 1.82 },
            new object[] { new Mod[] { rateMod(1.5), new OsuModEasy() }, 3.0, 6f, 1.25, 1.36 },
            new object[] { new Mod[] { rateMod(1.5), new OsuModEasy() }, 7.0, 10f, 1.38, 1.58 },
        };

        [TestCaseSource(nameof(rate_ranges))]
        public void TestApproximateStarRatingRate(Mod[] mods, double stars, float approachRate, double minimumRatio, double maximumRatio)
            => Assert.That(StarRatingApproximator.Approximate(osuBeatmap(stars, approachRate), null, mods) / stars, Is.InRange(minimumRatio, maximumRatio));

        private static readonly Mod[][] rate_companion_mods =
        {
            Array.Empty<Mod>(),
            new Mod[] { new OsuModHardRock() },
            new Mod[] { new OsuModEasy() },
            new Mod[] { new OsuModDifficultyAdjust { ExtendedLimits = { Value = true }, ApproachRate = { Value = 11 } } },
        };

        [Test]
        public void TestApproximateStarRatingIsMonotonicInRate([Values(1.0, 5.0, 10.0)] double baseStars, [Values(0f, 5f, 9f, 9.5f, 10f)] float approachRate)
        {
            var bInfo = osuBeatmap(baseStars, approachRate);

            foreach (var companions in rate_companion_mods)
            {
                double previous = 0;

                for (int i = 50; i <= 200; i++)
                {
                    var mods = i == 100 ? companions : companions.Append(rateMod(i / 100.0)).ToArray();
                    double stars = StarRatingApproximator.Approximate(bInfo, null, mods);

                    Assert.That(stars, Is.GreaterThan(previous), $"star rating went down at rate {i / 100.0} with {string.Join(", ", companions.Select(m => m.Acronym))}");
                    previous = stars;
                }
            }
        }

        [TestCase(1.2, 9f)]
        [TestCase(2.0, 10f)]
        [TestCase(0.5, 10f)]
        public void TestApproximateStarRatingRateKeepsNomodOrder(double rate, float approachRate)
        {
            foreach (var companions in rate_companion_mods)
            {
                double previous = 0;

                for (double baseStars = 0.1; baseStars <= 15; baseStars += 0.1)
                {
                    double stars = StarRatingApproximator.Approximate(osuBeatmap(baseStars, approachRate), null, companions.Append(rateMod(rate)).ToArray());

                    Assert.That(stars, Is.GreaterThan(previous), $"order changed at {baseStars}★");
                    previous = stars;
                }
            }
        }

        [Test]
        public void TestApproximateStarRatingRateApproachRateTerm()
        {
            double approximate(float approachRate, params Mod[] mods) => StarRatingApproximator.Approximate(osuBeatmap(5, approachRate), null, mods);

            // Up to AR 9 only the rate matters.
            Assert.That(approximate(0, rateMod(1.5)), Is.EqualTo(approximate(9, rateMod(1.5))));

            // Above it a speed-up raises star rating further, increasingly so at higher rates.
            double highApproachRateBoost(double rate) => approximate(10, rateMod(rate)) / approximate(9, rateMod(rate));
            Assert.That(highApproachRateBoost(1.5), Is.GreaterThan(1));
            Assert.That(highApproachRateBoost(2.0), Is.GreaterThan(highApproachRateBoost(1.5)));

            // Half time ignores approach rate.
            Assert.That(approximate(10, rateMod(0.75)), Is.EqualTo(approximate(5, rateMod(0.75))));

            // The term uses the approach rate after Hard Rock (AR 7.5 -> 10, AR 6 -> 8.4) and Difficulty Adjust.
            // Hard Rock's own factor depends on approach rate too, so it's divided out.
            double hardRockSpeedUp(float approachRate) => approximate(approachRate, rateMod(1.5), new OsuModHardRock()) / approximate(approachRate, new OsuModHardRock());
            Assert.That(hardRockSpeedUp(7.5f) / hardRockSpeedUp(6), Is.EqualTo(highApproachRateBoost(1.5)).Within(1e-9));

            var difficultyAdjust = new OsuModDifficultyAdjust { ApproachRate = { Value = 10 } };
            Assert.That(approximate(5, rateMod(1.5), difficultyAdjust) / approximate(5, difficultyAdjust), Is.EqualTo(approximate(10, rateMod(1.5)) / 5).Within(1e-9));
        }

        [TestCase(1.2)]
        [TestCase(1.5)]
        [TestCase(2.0)]
        public void TestApproximateStarRatingRateApproachRateTermByStarRating(double rate)
        {
            double highApproachRateBoost(double stars) => StarRatingApproximator.Approximate(osuBeatmap(stars, 10), null, new[] { rateMod(rate) })
                                                          / StarRatingApproximator.Approximate(osuBeatmap(stars, 9), null, new[] { rateMod(rate) });

            // Below 6★ the boost grows as star rating falls; from 6★ up it's the same.
            Assert.That(highApproachRateBoost(2), Is.GreaterThan(highApproachRateBoost(4)));
            Assert.That(highApproachRateBoost(4), Is.GreaterThan(highApproachRateBoost(6)));
            Assert.That(highApproachRateBoost(6), Is.EqualTo(highApproachRateBoost(10)).Within(1e-9));
        }

        private static readonly Mod[] speed_up_scaled_mods =
        {
            new OsuModDifficultyAdjust { ApproachRate = { Value = 7 } },
            new OsuModDifficultyAdjust { ExtendedLimits = { Value = true }, ApproachRate = { Value = 3 } },
            new OsuModRelax(),
            new OsuModFreezeFrame(),
        };

        // Ranges are the 10th-90th percentile of the exact calculator's response relative to the same speed-up alone,
        // measured on real beatmaps near that star rating and approach rate.
        private static readonly object[] speed_up_scaled_ranges =
        {
            new object[] { new OsuModDifficultyAdjust { ApproachRate = { Value = 7 } }, 1.2, 5.0, 9f, 1.01, 1.06 },
            new object[] { new OsuModDifficultyAdjust { ApproachRate = { Value = 7 } }, 1.5, 5.0, 9f, 1.00, 1.04 },
            new object[] { new OsuModFreezeFrame(), 1.2, 3.0, 6f, 1.13, 1.39 },
            new object[] { new OsuModFreezeFrame(), 2.0, 3.0, 6f, 1.05, 1.20 },
            new object[] { new OsuModFreezeFrame(), 1.2, 5.0, 9.3f, 1.02, 1.13 },
            new object[] { new OsuModFreezeFrame(), 2.0, 5.0, 9.3f, 0.97, 1.01 },
            new object[] { new OsuModRelax(), 1.2, 2.0, 9f, 0.72, 0.81 },
            new object[] { new OsuModRelax(), 2.0, 2.0, 9f, 0.71, 0.81 },
            new object[] { new OsuModRelax(), 1.2, 7.0, 9f, 0.61, 0.76 },
            new object[] { new OsuModRelax(), 2.0, 7.0, 9f, 0.53, 0.71 },
        };

        [TestCaseSource(nameof(speed_up_scaled_ranges))]
        public void TestApproximateStarRatingSpeedUpScaledMods(Mod mod, double rate, double stars, float approachRate, double minimumRatio, double maximumRatio)
        {
            var bInfo = osuBeatmap(stars, approachRate);
            double speedUpOnly = StarRatingApproximator.Approximate(bInfo, null, new[] { rateMod(rate) });

            Assert.That(StarRatingApproximator.Approximate(bInfo, null, new[] { rateMod(rate), mod }) / speedUpOnly, Is.InRange(minimumRatio, maximumRatio));
        }

        [TestCaseSource(nameof(speed_up_scaled_mods))]
        public void TestApproximateStarRatingSpeedUpScalingIsOffWithoutSpeedUp(Mod mod)
        {
            var bInfo = osuBeatmap(5, 9);
            double factor = StarRatingApproximator.Approximate(bInfo, null, new[] { mod }) / 5;

            foreach (double rate in new[] { 0.5, 0.75, 0.99 })
            {
                double slowDownOnly = StarRatingApproximator.Approximate(bInfo, null, new[] { rateMod(rate) });
                Assert.That(StarRatingApproximator.Approximate(bInfo, null, new[] { rateMod(rate), mod }) / slowDownOnly, Is.EqualTo(factor).Within(1e-9));
            }
        }

        [TestCase(7f)]
        [TestCase(3f)]
        public void TestApproximateStarRatingLoweredApproachRateUnderSpeedUp(float approachRate)
        {
            var bInfo = osuBeatmap(5, 9);
            var difficultyAdjust = new OsuModDifficultyAdjust { ExtendedLimits = { Value = true }, ApproachRate = { Value = approachRate } };
            double previous = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { difficultyAdjust }) / 5;

            // The boost from lowering approach rate shrinks as the rate goes up, but never turns into a reduction up to DT 2.0.
            // The calculator does reduce star rating there for some beatmaps (lowering AR 9 to 7 at DT 2.0), which this doesn't follow.
            for (int i = 101; i <= 200; i++)
            {
                double speedUpOnly = StarRatingApproximator.Approximate(bInfo, null, new[] { rateMod(i / 100.0) });
                double boost = StarRatingApproximator.Approximate(bInfo, null, new[] { rateMod(i / 100.0), difficultyAdjust }) / speedUpOnly;

                Assert.That(boost, Is.GreaterThan(1).And.LessThan(previous), $"at rate {i / 100.0}");
                previous = boost;
            }

            // Raising approach rate isn't scaled.
            var raised = new OsuModDifficultyAdjust { ApproachRate = { Value = 10 } };
            Assert.That(StarRatingApproximator.Approximate(bInfo, null, new[] { rateMod(2.0), raised }) / StarRatingApproximator.Approximate(osuBeatmap(5, 10), null, new[] { rateMod(2.0) }),
                Is.EqualTo(StarRatingApproximator.Approximate(bInfo, null, new Mod[] { raised }) / 5).Within(1e-9));
        }

        [Test]
        public void TestApproximateStarRatingSpeedUpScaledModsKeepNomodOrder([Values(1.2, 2.0)] double rate, [Values(6f, 10f)] float approachRate)
        {
            foreach (var mod in speed_up_scaled_mods)
            {
                double previous = 0;

                for (double baseStars = 0.1; baseStars <= 15; baseStars += 0.1)
                {
                    double stars = StarRatingApproximator.Approximate(osuBeatmap(baseStars, approachRate), null, new[] { rateMod(rate), mod });

                    Assert.That(stars, Is.GreaterThan(previous), $"order changed at {baseStars}★ with {mod.Acronym}");
                    previous = stars;
                }
            }
        }

        private static readonly Mod[][] hidden_companion_mods =
        {
            Array.Empty<Mod>(),
            new[] { rateMod(0.5) },
            new[] { rateMod(2.0) },
            new Mod[] { new OsuModHardRock() },
            new Mod[] { new OsuModEasy() },
            new Mod[] { new OsuModDifficultyAdjust { ExtendedLimits = { Value = true }, ApproachRate = { Value = -10 } } },
        };

        [Test]
        public void TestApproximateStarRatingHiddenKeepsOrderAndBounds([Values(0f, 5f, 9f, 10f)] float approachRate)
        {
            foreach (var companions in hidden_companion_mods)
            {
                var mods = companions.Append(new OsuModHidden()).ToArray();
                double previous = 0;

                for (double baseStars = 0.1; baseStars <= 15; baseStars += 0.1)
                {
                    var bInfo = osuBeatmap(baseStars, approachRate);
                    double withoutHidden = StarRatingApproximator.Approximate(bInfo, null, companions);
                    double stars = StarRatingApproximator.Approximate(bInfo, null, mods);

                    // The Hidden factor never lowers star rating, at most doubles it, and keeps the order without it.
                    Assert.That(stars / withoutHidden, Is.InRange(1, 2), $"at {baseStars}★ with {string.Join(", ", companions.Select(m => m.Acronym))}");
                    Assert.That(stars, Is.GreaterThan(previous), $"order changed at {baseStars}★ with {string.Join(", ", companions.Select(m => m.Acronym))}");
                    previous = stars;
                }
            }
        }

        [Test]
        public void TestApproximateStarRatingFlashlightKeepsOrderAndBounds([Values(0f, 5f, 9f, 10f)] float approachRate)
        {
            foreach (var companions in hidden_companion_mods.Append(new Mod[] { new OsuModHidden() }).Append(new Mod[] { rateMod(1.5), new OsuModHidden() }))
            {
                var mods = companions.Append(new OsuModFlashlight()).ToArray();
                double previous = 0;

                for (double baseStars = 0.1; baseStars <= 15; baseStars += 0.1)
                {
                    var bInfo = osuBeatmap(baseStars, approachRate);
                    double withoutFlashlight = StarRatingApproximator.Approximate(bInfo, null, companions);
                    double stars = StarRatingApproximator.Approximate(bInfo, null, mods);

                    // Flashlight adds a skill, so it never lowers star rating. The largest measured ratio was ×1.64.
                    Assert.That(stars / withoutFlashlight, Is.InRange(1, 1.65), $"at {baseStars}★ with {string.Join(", ", companions.Select(m => m.Acronym))}");
                    Assert.That(stars, Is.GreaterThan(previous), $"order changed at {baseStars}★ with {string.Join(", ", companions.Select(m => m.Acronym))}");
                    previous = stars;
                }
            }
        }

        [Test]
        public void TestApproximateStarRatingUnknownObjectCount()
        {
            var unknown = osuBeatmap(5, 9);
            var known = osuBeatmap(5, 9);
            known.TotalObjectCount = 500;

            Assert.That(unknown.TotalObjectCount, Is.EqualTo(-1));
            Assert.That(StarRatingApproximator.Approximate(unknown, null, new Mod[] { new OsuModHidden() }),
                Is.EqualTo(StarRatingApproximator.Approximate(known, null, new Mod[] { new OsuModHidden() })));

            // More objects means more Hidden reading difficulty.
            known.TotalObjectCount = 2000;
            Assert.That(StarRatingApproximator.Approximate(known, null, new Mod[] { new OsuModHidden() }),
                Is.GreaterThan(StarRatingApproximator.Approximate(unknown, null, new Mod[] { new OsuModHidden() })));

            known.TotalObjectCount = 500;
            Assert.That(StarRatingApproximator.Approximate(unknown, null, new Mod[] { new OsuModFlashlight() }),
                Is.EqualTo(StarRatingApproximator.Approximate(known, null, new Mod[] { new OsuModFlashlight() })));
        }

        [Test]
        public void TestInitialValueUsesEffectiveStarRating()
        {
            BeatmapInfo beatmap = null;
            double initialStars = 0;
            double expectedStars = 0;

            AddStep("create beatmap", () => beatmap = createDetachedBeatmap());

            AddStep("get bindable without mods", () => initialStars = difficultyCache.GetBindableDifficulty(beatmap).Value.Stars);
            AddAssert("initial value is base stars", () => initialStars, () => Is.EqualTo(4.0));

            AddStep("select DT", () => SelectedMods.Value = new[] { new OsuModDoubleTime() });
            AddStep("get bindable with DT", () =>
            {
                expectedStars = difficultyCache.GetEffectiveStarRating(beatmap, Ruleset.Value, SelectedMods.Value);
                initialStars = difficultyCache.GetBindableDifficulty(beatmap).Value.Stars;
            });
            AddAssert("effective stars differ from base stars", () => expectedStars, () => Is.Not.EqualTo(4.0));
            AddAssert("initial value is effective stars", () => initialStars, () => Is.EqualTo(expectedStars));
        }

        [Test]
        public void TestTrackedBindableSeededOnModChange()
        {
            BeatmapInfo beatmap = null;
            IBindable<StarDifficulty> bindable = null;
            double expectedStars = 0;

            AddStep("create beatmap", () => beatmap = createDetachedBeatmap());
            AddStep("set computation function", () => difficultyCache.ComputeDifficulty = _ => new StarDifficulty(4.5, 0));

            AddStep("get bindable", () => bindable = difficultyCache.GetBindableDifficulty(beatmap));
            AddUntilStep("bindable at exact stars", () => bindable.Value.Stars, () => Is.EqualTo(4.5));

            AddStep("hold computations", () => difficultyCache.HoldComputations());
            AddStep("select DT", () =>
            {
                SelectedMods.Value = new[] { new OsuModDoubleTime() };
                expectedStars = difficultyCache.GetEffectiveStarRating(beatmap, Ruleset.Value, SelectedMods.Value);
            });
            AddAssert("effective stars differ from previous stars", () => expectedStars, () => Is.Not.EqualTo(4.5));
            AddUntilStep("bindable seeded with effective stars", () => bindable.Value.Stars, () => Is.EqualTo(expectedStars));

            AddStep("release computations", () => difficultyCache.ReleaseComputations(new StarDifficulty(7.2, 0)));
            AddUntilStep("bindable at exact DT stars", () => bindable.Value.Stars, () => Is.EqualTo(7.2));
        }

        [Test]
        public void TestCancelledBindableNotSeeded()
        {
            BeatmapInfo beatmap = null;
            CancellationTokenSource cancellationSource = null;
            IBindable<StarDifficulty> cancelledBindable = null;
            IBindable<StarDifficulty> liveBindable = null;
            double expectedStars = 0;

            AddStep("create beatmap", () => beatmap = createDetachedBeatmap());
            AddStep("set computation function", () => difficultyCache.ComputeDifficulty = _ => new StarDifficulty(4.5, 0));

            AddStep("get bindables", () =>
            {
                cancelledBindable = difficultyCache.GetBindableDifficulty(beatmap, (cancellationSource = new CancellationTokenSource()).Token);
                liveBindable = difficultyCache.GetBindableDifficulty(beatmap);
            });
            AddUntilStep("both bindables at exact stars", () => cancelledBindable.Value.Stars == 4.5 && liveBindable.Value.Stars == 4.5);

            AddStep("cancel one bindable", () => cancellationSource.Cancel());
            AddStep("hold computations", () => difficultyCache.HoldComputations());
            AddStep("select DT", () =>
            {
                SelectedMods.Value = new[] { new OsuModDoubleTime() };
                expectedStars = difficultyCache.GetEffectiveStarRating(beatmap, Ruleset.Value, SelectedMods.Value);
            });
            AddUntilStep("live bindable seeded", () => liveBindable.Value.Stars, () => Is.EqualTo(expectedStars));
            AddAssert("cancelled bindable unchanged", () => cancelledBindable.Value.Stars, () => Is.EqualTo(4.5));

            AddStep("release computations", () => difficultyCache.ReleaseComputations(new StarDifficulty(7.2, 0)));
        }

        [Test]
        public void TestNonDifficultyModChangeKeepsAttributes()
        {
            BeatmapInfo beatmap = null;
            IBindable<StarDifficulty> bindable = null;
            int requestsBeforeModChange = 0;

            AddStep("create beatmap", () => beatmap = createDetachedBeatmap());
            AddStep("set computation function", () => difficultyCache.ComputeDifficulty = _ =>
                new StarDifficulty(new DifficultyAttributes { StarRating = 4.0 }, new PerformanceAttributes { Total = 123 }));

            AddStep("get bindable", () => bindable = difficultyCache.GetBindableDifficulty(beatmap));
            AddUntilStep("bindable has performance attributes", () => bindable.Value.PerformanceAttributes, () => Is.Not.Null);

            AddStep("hold computations", () =>
            {
                difficultyCache.HoldComputations();
                requestsBeforeModChange = difficultyCache.ComputationRequests;
            });
            AddStep("select NF", () => SelectedMods.Value = new[] { new OsuModNoFail() });
            AddUntilStep("tracked bindables updated", () => difficultyCache.ComputationRequests, () => Is.GreaterThan(requestsBeforeModChange));
            AddAssert("performance attributes kept", () => bindable.Value.PerformanceAttributes, () => Is.Not.Null);

            AddStep("release computations", () => difficultyCache.ReleaseComputations(new StarDifficulty(4.0, 0)));
        }

        [Test]
        public void TestTrackedDifficultyUpdatedReportsEachBindable()
        {
            var updates = new List<(IBeatmapInfo beatmap, StarDifficulty difficulty)>();
            var beatmap = createDetachedBeatmap();
            IBindable<StarDifficulty> first = null;
            IBindable<StarDifficulty> second = null;

            AddStep("set computation function", () => difficultyCache.ComputeDifficulty = _ => new StarDifficulty(7.5, 0));
            AddStep("subscribe", () => difficultyCache.TrackedDifficultyUpdated += (b, d) => updates.Add((b, d)));

            AddStep("get two bindables for one beatmap", () =>
            {
                first = difficultyCache.GetBindableDifficulty(beatmap);
                second = difficultyCache.GetBindableDifficulty(beatmap);
            });

            AddUntilStep("both bindables updated", () => first.Value.Stars == 7.5 && second.Value.Stars == 7.5);
            AddAssert("reported once per bindable", () => updates.Count(u => u.beatmap.Equals(beatmap)), () => Is.EqualTo(2));
            AddAssert("reported computed difficulty", () => updates.Where(u => u.beatmap.Equals(beatmap)).Select(u => u.difficulty.Stars), () => Is.All.EqualTo(7.5));
        }

        [Test]
        public void TestTrackedDifficultyUpdatedNotReportedForCancelledBindable()
        {
            var reportedBeatmaps = new List<IBeatmapInfo>();
            var cancelledBeatmap = createDetachedBeatmap();
            var otherBeatmap = createDetachedBeatmap();
            var cancellationSource = new CancellationTokenSource();
            IBindable<StarDifficulty> cancelled = null;
            IBindable<StarDifficulty> other = null;

            AddStep("subscribe", () => difficultyCache.TrackedDifficultyUpdated += (b, _) => reportedBeatmaps.Add(b));
            AddStep("hold computations", () => difficultyCache.HoldComputations());

            AddStep("get bindables", () =>
            {
                cancelled = difficultyCache.GetBindableDifficulty(cancelledBeatmap, cancellationSource.Token);
                other = difficultyCache.GetBindableDifficulty(otherBeatmap);
            });

            AddStep("cancel first bindable", () => cancellationSource.Cancel());
            AddStep("release computations", () => difficultyCache.ReleaseComputations(new StarDifficulty(7.5, 0)));

            AddUntilStep("other bindable updated", () => other.Value.Stars == 7.5 && reportedBeatmaps.Contains(otherBeatmap));
            AddAssert("cancelled bindable not updated", () => cancelled.Value.Stars, () => Is.EqualTo(cancelledBeatmap.StarRating));
            AddAssert("cancelled bindable not reported", () => reportedBeatmaps.Contains(cancelledBeatmap), () => Is.False);
        }

        private static BeatmapInfo createDetachedBeatmap() => new BeatmapInfo { ID = Guid.NewGuid(), StarRating = 4.0, Ruleset = new RulesetInfo { OnlineID = 0 } };

        private partial class TestBeatmapDifficultyCache : BeatmapDifficultyCache
        {
            public Func<DifficultyCacheLookup, StarDifficulty> ComputeDifficulty { get; set; }

            public int ComputationRequests => Volatile.Read(ref computationRequests);

            private int computationRequests;

            private TaskCompletionSource<StarDifficulty?> pendingComputation;

            /// <summary>
            /// Holds all subsequent computations until <see cref="ReleaseComputations"/> is called.
            /// </summary>
            public void HoldComputations() => pendingComputation = new TaskCompletionSource<StarDifficulty?>(TaskCreationOptions.RunContinuationsAsynchronously);

            public void ReleaseComputations(StarDifficulty result)
            {
                var pending = pendingComputation;
                pendingComputation = null;
                pending?.SetResult(result);
            }

            protected override Task<StarDifficulty?> ComputeValueAsync(DifficultyCacheLookup lookup, CancellationToken token = default)
            {
                Interlocked.Increment(ref computationRequests);

                if (pendingComputation != null)
                    return pendingComputation.Task;

                return Task.FromResult<StarDifficulty?>(ComputeDifficulty?.Invoke(lookup) ?? new StarDifficulty(BASE_STARS, 0));
            }
        }
    }
}
