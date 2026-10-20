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

            // Easy: decreases difficulty
            double ezStars = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { new OsuModEasy() });
            Assert.That(ezStars, Is.GreaterThan(3.1).And.LessThan(3.5));

            // Flashlight: increases difficulty
            double flStars = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { new OsuModFlashlight() });
            Assert.That(flStars, Is.GreaterThan(4.4).And.LessThan(5.0));

            // Hidden: slight reading increase
            double hdStars = StarRatingApproximator.Approximate(bInfo, null, new Mod[] { new OsuModHidden() });
            Assert.That(hdStars, Is.GreaterThan(4.1).And.LessThan(4.2));

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
