// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Taiko;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osu.Game.Tests.Beatmaps;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.SongSelect
{
    [TestFixture]
    public partial class BeatmapCarouselFilterSortingTest
    {
        [Test]
        public async Task TestSorting()
        {
            List<BeatmapSetInfo> beatmapSets = new List<BeatmapSetInfo>();

            const string zzz_lowercase = "zzzzz";
            const string zzz_uppercase = "ZZZZZ";
            const int diff_count = 5;

            for (int i = 0; i < 20; i++)
            {
                var set = TestResources.CreateTestBeatmapSetInfo(diff_count);

                if (i == 4)
                    set.Beatmaps.ForEach(b => b.Metadata.Artist = zzz_uppercase);

                if (i == 8)
                    set.Beatmaps.ForEach(b => b.Metadata.Artist = zzz_lowercase);

                if (i == 12)
                    set.Beatmaps.ForEach(b => b.Metadata.Author.Username = zzz_uppercase);

                if (i == 16)
                    set.Beatmaps.ForEach(b => b.Metadata.Author.Username = zzz_lowercase);

                beatmapSets.Add(set);
            }

            var results = await runSorting(SortMode.Author, beatmapSets);

            Assert.That(results.Last().Metadata.Author.Username, Is.EqualTo(zzz_uppercase));
            Assert.That(results.SkipLast(diff_count).Last().Metadata.Author.Username, Is.EqualTo(zzz_lowercase));

            results = await runSorting(SortMode.Artist, beatmapSets);

            Assert.That(results.Last().Metadata.Artist, Is.EqualTo(zzz_uppercase));
            Assert.That(results.SkipLast(diff_count).Last().Metadata.Artist, Is.EqualTo(zzz_lowercase));
        }

        [Test]
        public async Task TestSortingDateSubmitted()
        {
            List<BeatmapSetInfo> beatmapSets = new List<BeatmapSetInfo>();

            const string zzz_string = "zzzzz";

            for (int i = 0; i < 10; i++)
            {
                var set = TestResources.CreateTestBeatmapSetInfo(5);

                // A total of 6 sets have date submitted (4 don't)
                // A total of 5 sets have artist string (3 of which also have date submitted)

                if (i >= 2 && i < 8) // i = 2, 3, 4, 5, 6, 7 have submitted date
                    set.DateSubmitted = DateTimeOffset.Now.AddMinutes(i);
                if (i < 5) // i = 0, 1, 2, 3, 4 have matching string
                    set.Beatmaps.ForEach(b => b.Metadata.Artist = zzz_string);

                set.Beatmaps.ForEach(b => b.Metadata.Title = $"submitted: {set.DateSubmitted}");

                beatmapSets.Add(set);
            }

            var results = await runSorting(SortMode.DateSubmitted, beatmapSets);

            Assert.That(results.Count(), Is.EqualTo(50));

            Assert.That(results.Reverse().TakeWhile(b => b.BeatmapSet!.DateSubmitted == null).Count(), Is.EqualTo(20), () => "missing dates should be at the end");
            Assert.That(results.TakeWhile(b => b.BeatmapSet!.DateSubmitted != null).Count(), Is.EqualTo(30), () => "non-missing dates should be at the start");
        }

        [Test]
        public async Task TestSortByBpmUsesTitleAsTiebreaker()
        {
            List<BeatmapSetInfo> beatmapSets = [];

            // 2 sets with same BPM but different titles
            const int diff_count = 1;
            {
                var set = TestResources.CreateTestBeatmapSetInfo(diff_count);
                set.DateAdded = new DateTimeOffset(2025, 6, 11, 10, 0, 0, TimeSpan.Zero);
                set.Beatmaps.ForEach(b =>
                {
                    b.ID = Guid.Parse("00000000-0000-0000-0000-000000000000");
                    b.BPM = 175;
                    b.Metadata.Title = "ZZZ";
                });
                beatmapSets.Add(set);
            }
            {
                var set = TestResources.CreateTestBeatmapSetInfo(diff_count);
                set.DateAdded = new DateTimeOffset(2025, 6, 10, 10, 0, 0, TimeSpan.Zero);
                set.Beatmaps.ForEach(b =>
                {
                    b.ID = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
                    b.BPM = 175;
                    b.Metadata.Title = "AAA";
                });
                beatmapSets.Add(set);
            }

            var results = (await runSorting(SortMode.BPM, beatmapSets)).ToList();

            Assert.That(results[0].Metadata.Title, Is.EqualTo("AAA"));
            Assert.That(results[1].Metadata.Title, Is.EqualTo("ZZZ"));
        }

        [Test]
        public async Task TestSortByArtistUsesTitleAsTiebreaker()
        {
            List<BeatmapSetInfo> beatmapSets = new List<BeatmapSetInfo>();

            const int diff_count = 5;

            for (int i = 0; i < 20; i++)
            {
                var set = TestResources.CreateTestBeatmapSetInfo(diff_count);

                if (i == 4)
                {
                    set.Beatmaps.ForEach(b =>
                    {
                        b.Metadata.Artist = "ZZZ";
                        b.Metadata.Title = "AAA";
                    });
                }

                if (i == 8)
                {
                    set.Beatmaps.ForEach(b =>
                    {
                        b.Metadata.Artist = "ZZZ";
                        b.Metadata.Title = "ZZZ";
                    });
                }

                beatmapSets.Add(set);
            }

            var results = await runSorting(SortMode.Artist, beatmapSets);

            Assert.That(() =>
            {
                var lastItem = results.Last();
                return lastItem.Metadata.Artist == "ZZZ" && lastItem.Metadata.Title == "ZZZ";
            });

            Assert.That(() =>
            {
                var secondLastItem = results.SkipLast(diff_count).Last();
                return secondLastItem.Metadata.Artist == "ZZZ" && secondLastItem.Metadata.Title == "AAA";
            });
        }

        /// <summary>
        /// Ensures stability is maintained on different sort modes for items with equal properties.
        /// </summary>
        [Test]
        public async Task TestSortingStabilityDateAdded()
        {
            List<BeatmapSetInfo> beatmapSets = new List<BeatmapSetInfo>();

            for (int i = 0; i < 10; i++)
            {
                var set = TestResources.CreateTestBeatmapSetInfo();

                set.DateAdded = DateTimeOffset.FromUnixTimeSeconds(i);

                // only need to set the first as they are a shared reference.
                var beatmap = set.Beatmaps.First();

                beatmap.Metadata.Artist = "a";
                beatmap.Metadata.Title = "b";

                beatmapSets.Add(set);
            }

            var results = await runSorting(SortMode.Title, beatmapSets);

            Assert.That(results.Select(b => b.BeatmapSet!.DateAdded), Is.Ordered.Descending);

            results = await runSorting(SortMode.Artist, beatmapSets);

            Assert.That(results.Select(b => b.BeatmapSet!.DateAdded), Is.Ordered.Descending);
        }

        [Test]
        public async Task TestSortByDifficultyNomod()
        {
            var set = TestResources.CreateTestBeatmapSetInfo(5);
            set.Beatmaps[0].StarRating = 5.0;
            set.Beatmaps[1].StarRating = 1.0;
            set.Beatmaps[2].StarRating = 4.0;
            set.Beatmaps[3].StarRating = 2.0;
            set.Beatmaps[4].StarRating = 3.0;

            var results = (await runSorting(SortMode.Difficulty, new List<BeatmapSetInfo> { set })).ToList();

            Assert.That(results.Select(b => b.StarRating), Is.EqualTo(new[] { 1.0, 2.0, 3.0, 4.0, 5.0 }));
        }

        [Test]
        public async Task TestSortByDifficultyWithModAdjustment()
        {
            var setA = TestResources.CreateTestBeatmapSetInfo(1);
            setA.Beatmaps[0].StarRating = 4.0;

            var setB = TestResources.CreateTestBeatmapSetInfo(1);
            setB.Beatmaps[0].StarRating = 5.0;

            // Nomod: setA (4.0) < setB (5.0)
            var nomodResults = (await runSorting(SortMode.Difficulty, new List<BeatmapSetInfo> { setB, setA })).ToList();
            Assert.That(nomodResults[0], Is.EqualTo(setA.Beatmaps[0]));
            Assert.That(nomodResults[1], Is.EqualTo(setB.Beatmaps[0]));

            // DoubleTime scales both up (setA ≈ 5.71, setB ≈ 7.32) and preserves ascending order.
            var dtResults = (await runSorting(SortMode.Difficulty, new List<BeatmapSetInfo> { setB, setA }, new[] { new OsuModDoubleTime() })).ToList();
            Assert.That(dtResults[0], Is.EqualTo(setA.Beatmaps[0]));
            Assert.That(dtResults[1], Is.EqualTo(setB.Beatmaps[0]));
        }

        [Test]
        public async Task TestSortByDifficultyRespectsModDifference()
        {
            var setA = TestResources.CreateTestBeatmapSetInfo(1);
            setA.Beatmaps[0].StarRating = 5.0;
            setA.Beatmaps[0].Difficulty.ApproachRate = 10;

            var setB = TestResources.CreateTestBeatmapSetInfo(1);
            setB.Beatmaps[0].StarRating = 5.2;
            setB.Beatmaps[0].Difficulty.ApproachRate = 5;

            // Nomod: setA (5.0) < setB (5.2)
            var nomodResults = (await runSorting(SortMode.Difficulty, new List<BeatmapSetInfo> { setB, setA })).ToList();
            Assert.That(nomodResults[0], Is.EqualTo(setA.Beatmaps[0]));
            Assert.That(nomodResults[1], Is.EqualTo(setB.Beatmaps[0]));

            // Easy raises high-AR maps relative to low-AR maps: setA ≈ 5.31, setB ≈ 4.84, so the order flips.
            var ezResults = (await runSorting(SortMode.Difficulty, new List<BeatmapSetInfo> { setA, setB }, new[] { new OsuModEasy() })).ToList();
            Assert.That(ezResults[0], Is.EqualTo(setB.Beatmaps[0]));
            Assert.That(ezResults[1], Is.EqualTo(setA.Beatmaps[0]));
        }

        [Test]
        public async Task TestSortByDifficultyCompoundMods()
        {
            // DT + HR: HR scales with circle size, so a slightly easier high-CS map overtakes a low-CS map.
            // setA: 4.9 base, CS 9 ≈ 8.96; setB: 5.0 base, CS 2 ≈ 7.48.
            var setA = TestResources.CreateTestBeatmapSetInfo(1);
            setA.Beatmaps[0].StarRating = 4.9;
            setA.Beatmaps[0].Difficulty.CircleSize = 9;

            var setB = TestResources.CreateTestBeatmapSetInfo(1);
            setB.Beatmaps[0].StarRating = 5.0;
            setB.Beatmaps[0].Difficulty.CircleSize = 2;

            var modsDtHr = new Mod[] { new OsuModDoubleTime(), new OsuModHardRock() };
            var resultsDtHr = (await runSorting(SortMode.Difficulty, new List<BeatmapSetInfo> { setA, setB }, modsDtHr)).ToList();
            Assert.That(resultsDtHr[0], Is.EqualTo(setB.Beatmaps[0]));
            Assert.That(resultsDtHr[1], Is.EqualTo(setA.Beatmaps[0]));

            // EZ + HT: EZ keeps more difficulty on high-AR maps, so a high-AR map overtakes a harder low-AR map.
            // setC: 4.0 base, AR 10 ≈ 3.75; setD: 4.5 base, AR 5 ≈ 3.37.
            var setC = TestResources.CreateTestBeatmapSetInfo(1);
            setC.Beatmaps[0].StarRating = 4.0;
            setC.Beatmaps[0].Difficulty.ApproachRate = 10;

            var setD = TestResources.CreateTestBeatmapSetInfo(1);
            setD.Beatmaps[0].StarRating = 4.5;
            setD.Beatmaps[0].Difficulty.ApproachRate = 5;

            var modsEzHt = new Mod[] { new OsuModEasy(), new OsuModHalfTime() };
            var resultsEzHt = (await runSorting(SortMode.Difficulty, new List<BeatmapSetInfo> { setC, setD }, modsEzHt)).ToList();
            Assert.That(resultsEzHt[0], Is.EqualTo(setD.Beatmaps[0]));
            Assert.That(resultsEzHt[1], Is.EqualTo(setC.Beatmaps[0]));
        }

        [Test]
        public async Task TestSortByDifficultyNonDifficultyModsPreserveBaseOrder()
        {
            var setA = TestResources.CreateTestBeatmapSetInfo(1);
            setA.Beatmaps[0].StarRating = 3.0;

            var setB = TestResources.CreateTestBeatmapSetInfo(1);
            setB.Beatmaps[0].StarRating = 4.0;

            var setC = TestResources.CreateTestBeatmapSetInfo(1);
            setC.Beatmaps[0].StarRating = 5.0;

            var nonDifficultyMods = new Mod[] { new OsuModNoFail(), new OsuModSpunOut(), new OsuModAutoplay() };
            var results = (await runSorting(SortMode.Difficulty, new List<BeatmapSetInfo> { setC, setA, setB }, nonDifficultyMods)).ToList();
            Assert.That(results[0], Is.EqualTo(setA.Beatmaps[0]));
            Assert.That(results[1], Is.EqualTo(setB.Beatmaps[0]));
            Assert.That(results[2], Is.EqualTo(setC.Beatmaps[0]));
        }

        [Test]
        public async Task TestSortByDifficultyZeroStarsHandledGracefully()
        {
            var setZero = TestResources.CreateTestBeatmapSetInfo(1);
            setZero.Beatmaps[0].StarRating = 0.0;

            var setNormal = TestResources.CreateTestBeatmapSetInfo(1);
            setNormal.Beatmaps[0].StarRating = 3.5;

            var results = (await runSorting(SortMode.Difficulty, new List<BeatmapSetInfo> { setNormal, setZero }, new[] { new OsuModDoubleTime() })).ToList();
            Assert.That(results[0], Is.EqualTo(setZero.Beatmaps[0]));
            Assert.That(results[1], Is.EqualTo(setNormal.Beatmaps[0]));
        }

        [Test]
        public async Task TestSortByDifficultyExactCachedOverrulesApproximation()
        {
            var setA = TestResources.CreateTestBeatmapSetInfo(1);
            setA.Beatmaps[0].StarRating = 4.0;

            var setB = TestResources.CreateTestBeatmapSetInfo(1);
            setB.Beatmaps[0].StarRating = 5.0;

            var mods = new[] { new OsuModDoubleTime() };

            // Under approximation alone, setA (≈ 5.71) would sort before setB (≈ 7.32).
            // Only setA is pre-populated in the cache, with an exact value of 8.0, so it should sort after setB.
            var cache = new TestBeatmapDifficultyCache
            {
                PresetDifficulty = _ => new StarDifficulty(8.0, 0)
            };

            await cache.GetDifficultyAsync(setA.Beatmaps[0], null, mods);

            var results = (await runSorting(SortMode.Difficulty, new List<BeatmapSetInfo> { setA, setB }, mods, cache)).ToList();
            Assert.That(results[0], Is.EqualTo(setB.Beatmaps[0]));
            Assert.That(results[1], Is.EqualTo(setA.Beatmaps[0]));
        }

        [TestCaseSource(typeof(TestSceneBeatmapDifficultyCache), nameof(TestSceneBeatmapDifficultyCache.DIFFICULTY_MODS_WITHOUT_DIFFICULTY_INTERFACES))]
        public async Task TestSortByDifficultyUsesExactCachedValueForMod(Mod mod)
        {
            var setA = TestResources.CreateTestBeatmapSetInfo(1);
            setA.Beatmaps[0].StarRating = 4.0;

            var setB = TestResources.CreateTestBeatmapSetInfo(1);
            setB.Beatmaps[0].StarRating = 5.0;

            var mods = new[] { mod };

            // Without a cached value setA sorts before setB, as the approximation keeps the base star rating for this mod.
            // Only setA is pre-populated in the cache, with an exact value of 8.0, so it should sort after setB.
            var cache = new TestBeatmapDifficultyCache
            {
                PresetDifficulty = _ => new StarDifficulty(8.0, 0)
            };

            await cache.GetDifficultyAsync(setA.Beatmaps[0], null, mods);

            var results = (await runSorting(SortMode.Difficulty, new List<BeatmapSetInfo> { setA, setB }, mods, cache)).ToList();
            Assert.That(results[0], Is.EqualTo(setB.Beatmaps[0]));
            Assert.That(results[1], Is.EqualTo(setA.Beatmaps[0]));
        }

        [Test]
        public async Task TestLastEffectiveStarRatingsHoldSortedStarRatings()
        {
            var setA = TestResources.CreateTestBeatmapSetInfo(1);
            setA.Beatmaps[0].StarRating = 4.0;

            var setB = TestResources.CreateTestBeatmapSetInfo(1);
            setB.Beatmaps[0].StarRating = 5.0;

            var mods = new[] { new OsuModDoubleTime() };

            // Only the first beatmap has an exact star rating cached, so the second is sorted by an approximation.
            var cache = new TestBeatmapDifficultyCache
            {
                PresetDifficulty = _ => new StarDifficulty(8.0, 0)
            };

            await cache.GetDifficultyAsync(setA.Beatmaps[0], null, mods);

            var sorter = new BeatmapCarouselFilterSorting(() => new FilterCriteria { Sort = SortMode.Difficulty, Mods = mods })
            {
                DifficultyCache = cache
            };

            await sorter.Run(new[] { setA, setB }.SelectMany(s => s.Beatmaps).Select(b => new CarouselItem(b)), CancellationToken.None);

            Assert.That(sorter.LastEffectiveStarRatings[setA.Beatmaps[0]], Is.EqualTo(8.0));
            Assert.That(sorter.LastEffectiveStarRatings[setB.Beatmaps[0]], Is.EqualTo(StarRatingApproximator.Approximate(setB.Beatmaps[0], null, mods)));
        }

        [Test]
        public async Task TestSortByDifficultyTiedEffectiveStarsFallsBackToBaseStars()
        {
            var setA = TestResources.CreateTestBeatmapSetInfo(1);
            setA.Beatmaps[0].StarRating = 4.2;
            setA.Beatmaps[0].Metadata.Title = "ZZZ";

            var setB = TestResources.CreateTestBeatmapSetInfo(1);
            setB.Beatmaps[0].StarRating = 4.5;
            setB.Beatmaps[0].Metadata.Title = "AAA";

            var mods = new[] { new OsuModDoubleTime() };

            // Both beatmaps have the same exact cached effective rating, so base star rating must break the tie
            // (ahead of the title fallback, which would otherwise put setB first).
            var cache = new TestBeatmapDifficultyCache
            {
                PresetDifficulty = _ => new StarDifficulty(5.0, 0)
            };

            await cache.GetDifficultyAsync(setA.Beatmaps[0], null, mods);
            await cache.GetDifficultyAsync(setB.Beatmaps[0], null, mods);

            var results = (await runSorting(SortMode.Difficulty, new List<BeatmapSetInfo> { setB, setA }, mods, cache)).ToList();
            Assert.That(results[0], Is.EqualTo(setA.Beatmaps[0]));
            Assert.That(results[1], Is.EqualTo(setB.Beatmaps[0]));
        }

        [Test]
        public async Task TestSortByDifficultyTiedStarsFallsBackToTitle()
        {
            var setZzz = TestResources.CreateTestBeatmapSetInfo(1);
            setZzz.Beatmaps[0].StarRating = 5.0;
            setZzz.Beatmaps[0].Metadata.Title = "ZZZ";

            var setAaa = TestResources.CreateTestBeatmapSetInfo(1);
            setAaa.Beatmaps[0].StarRating = 5.0;
            setAaa.Beatmaps[0].Metadata.Title = "AAA";

            // When stars tie, Title alphabetical order breaks the tie
            var results = (await runSorting(SortMode.Difficulty, new List<BeatmapSetInfo> { setZzz, setAaa })).ToList();
            Assert.That(results[0], Is.EqualTo(setAaa.Beatmaps[0]));
            Assert.That(results[1], Is.EqualTo(setZzz.Beatmaps[0]));
        }

        [Test]
        public async Task TestWithinSetOrderWithModAdjustment()
        {
            var set = TestResources.CreateTestBeatmapSetInfo(2);
            var easier = set.Beatmaps[0];
            var harder = set.Beatmaps[1];

            easier.StarRating = 4.95;
            easier.Difficulty.CircleSize = 7;
            harder.StarRating = 5.0;
            harder.Difficulty.CircleSize = 4;

            var nomodResults = (await runSorting(SortMode.Title, new List<BeatmapSetInfo> { set })).ToList();
            Assert.That(nomodResults[0], Is.EqualTo(easier));
            Assert.That(nomodResults[1], Is.EqualTo(harder));

            // Hard rock scales higher circle sizes more, so the nomod-easier difficulty (≈ 5.49) overtakes the other (≈ 5.45).
            var hrResults = (await runSorting(SortMode.Title, new List<BeatmapSetInfo> { set }, new[] { new OsuModHardRock() })).ToList();
            Assert.That(hrResults[0], Is.EqualTo(harder));
            Assert.That(hrResults[1], Is.EqualTo(easier));
        }

        [Test]
        public async Task TestWithinSetOrderExactCachedOverrulesApproximation()
        {
            var set = TestResources.CreateTestBeatmapSetInfo(2);
            set.Beatmaps[0].StarRating = 4.0;
            set.Beatmaps[1].StarRating = 5.0;

            var mods = new[] { new OsuModDoubleTime() };

            // Only the first difficulty is pre-populated in the cache, with an exact value of 8.0, so it should sort last.
            var cache = new TestBeatmapDifficultyCache
            {
                PresetDifficulty = _ => new StarDifficulty(8.0, 0)
            };

            await cache.GetDifficultyAsync(set.Beatmaps[0], null, mods);

            var results = (await runSorting(SortMode.Title, new List<BeatmapSetInfo> { set }, mods, cache)).ToList();
            Assert.That(results[0], Is.EqualTo(set.Beatmaps[1]));
            Assert.That(results[1], Is.EqualTo(set.Beatmaps[0]));
        }

        [Test]
        public async Task TestWithinSetOrderTiedEffectiveStarsFallsBackToBaseStars()
        {
            var set = TestResources.CreateTestBeatmapSetInfo(2);
            set.Beatmaps[0].StarRating = 4.5;
            set.Beatmaps[1].StarRating = 4.2;

            var mods = new[] { new OsuModDoubleTime() };

            // Both difficulties have the same exact cached effective rating, so base star rating must break the tie.
            var cache = new TestBeatmapDifficultyCache
            {
                PresetDifficulty = _ => new StarDifficulty(5.0, 0)
            };

            await cache.GetDifficultyAsync(set.Beatmaps[0], null, mods);
            await cache.GetDifficultyAsync(set.Beatmaps[1], null, mods);

            var results = (await runSorting(SortMode.Title, new List<BeatmapSetInfo> { set }, mods, cache)).ToList();
            Assert.That(results[0], Is.EqualTo(set.Beatmaps[1]));
            Assert.That(results[1], Is.EqualTo(set.Beatmaps[0]));
        }

        [Test]
        public async Task TestWithinSetOrderGroupsByRulesetFirst()
        {
            var set = TestResources.CreateTestBeatmapSetInfo(2, new[] { new TaikoRuleset().RulesetInfo, new OsuRuleset().RulesetInfo });
            set.Beatmaps[0].StarRating = 3.0;
            set.Beatmaps[1].StarRating = 5.0;

            // Ruleset takes precedence over star rating within a set.
            var results = (await runSorting(SortMode.Title, new List<BeatmapSetInfo> { set }, new[] { new OsuModDoubleTime() })).ToList();
            Assert.That(results[0].Ruleset.ShortName, Is.EqualTo("osu"));
            Assert.That(results[1].Ruleset.ShortName, Is.EqualTo("taiko"));
        }

        [Test]
        public async Task TestSortByDifficultyTiedDifficultiesInSameSetOrderedById()
        {
            var set = TestResources.CreateTestBeatmapSetInfo(tied_difficulty_count);
            set.Beatmaps.ForEach(b => b.StarRating = 5.0);

            await assertTiedDifficultiesOrderedById(SortMode.Difficulty, set);
        }

        [Test]
        public async Task TestWithinSetOrderTiedDifficultiesOrderedById()
        {
            var set = TestResources.CreateTestBeatmapSetInfo(tied_difficulty_count);
            set.Beatmaps.ForEach(b => b.StarRating = 5.0);

            await assertTiedDifficultiesOrderedById(SortMode.Title, set);
        }

        /// <summary>
        /// The sort is unstable, but only for more than 16 items (below that, it uses a stable insertion sort).
        /// </summary>
        private const int tied_difficulty_count = 24;

        /// <summary>
        /// Sorts difficulties which tie on every other comparison in several input orders, and checks they always end up in the same order.
        /// </summary>
        private static async Task assertTiedDifficultiesOrderedById(SortMode sort, BeatmapSetInfo set)
        {
            var expectedIds = set.Beatmaps.Select(b => b.ID).OrderDescending().ToArray();
            var random = new Random(20261016);

            for (int i = 0; i < 5; i++)
            {
                var shuffled = set.Beatmaps.OrderBy(_ => random.Next()).ToList();
                var results = await runSorting(sort, shuffled);

                Assert.That(results.Select(b => b.ID), Is.EqualTo(expectedIds));
            }
        }

        private static Task<IEnumerable<BeatmapInfo>> runSorting(SortMode sort, List<BeatmapSetInfo> beatmapSets, IReadOnlyList<Mod>? mods = null, BeatmapDifficultyCache? difficultyCache = null)
            => runSorting(sort, beatmapSets.SelectMany(s => s.Beatmaps), mods, difficultyCache);

        private static async Task<IEnumerable<BeatmapInfo>> runSorting(SortMode sort, IEnumerable<BeatmapInfo> beatmaps, IReadOnlyList<Mod>? mods = null, BeatmapDifficultyCache? difficultyCache = null)
        {
            var sorter = new BeatmapCarouselFilterSorting(() => new FilterCriteria { Sort = sort, Mods = mods })
            {
                DifficultyCache = difficultyCache
            };
            var carouselItems = await sorter.Run(beatmaps.Select(b => new CarouselItem(b)), CancellationToken.None);
            return carouselItems.Select(ci => ci.Model).OfType<BeatmapInfo>();
        }

        private partial class TestBeatmapDifficultyCache : BeatmapDifficultyCache
        {
            public Func<DifficultyCacheLookup, StarDifficulty>? PresetDifficulty { get; set; }

            protected override Task<StarDifficulty?> ComputeValueAsync(DifficultyCacheLookup lookup, CancellationToken token = default)
            {
                if (PresetDifficulty != null)
                    return Task.FromResult<StarDifficulty?>(PresetDifficulty(lookup));

                return Task.FromResult<StarDifficulty?>(new StarDifficulty(lookup.BeatmapInfo.StarRating, 0));
            }
        }
    }
}
