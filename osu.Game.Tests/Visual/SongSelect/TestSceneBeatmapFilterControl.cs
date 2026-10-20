// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Screens.Select;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Tests.Visual.SongSelect
{
    public partial class TestSceneBeatmapFilterControl : SongSelectComponentsTestScene
    {
        private FilterControl filterControl = null!;

        protected override Anchor ComponentAnchor => Anchor.TopRight;
        protected override float InitialRelativeWidth => 0.7f;

        [SetUp]
        public void SetUp() => Schedule(createFilterControl);

        [Test]
        public void TestSearch()
        {
            AddStep("search for text", () => filterControl.Search("test search"));
        }

        [Test]
        public void TestSettingChangeTrackedAfterModReplacedByEqualInstance()
        {
            var first = new OsuModDoubleTime();
            var second = new OsuModDoubleTime();
            int filterCount = 0;

            AddUntilStep("wait for load", () => filterControl.IsLoaded);
            AddStep("filter by star rating", () => filterControl.Search("stars>1"));
            AddStep("select double time", () => SelectedMods.Value = new Mod[] { first });
            AddStep("hook filter event", () => filterControl.CriteriaChanged += _ => filterCount++);

            // Equal by value, so this shouldn't re-filter, but settings of the new instance should be tracked from here on.
            AddStep("replace with equal instance", () => SelectedMods.Value = new Mod[] { second });
            AddAssert("did not filter", () => filterCount, () => Is.Zero);

            AddStep("change rate", () => second.SpeedChange.Value = 1.8);
            AddUntilStep("filtered once", () => filterCount, () => Is.EqualTo(1));
        }

        [Test]
        public void TestSettingChangeTrackedForModSelectedBeforeLoad()
        {
            var doubleTime = new OsuModDoubleTime();
            int filterCount = 0;

            AddStep("select double time", () => SelectedMods.Value = new Mod[] { doubleTime });
            AddStep("recreate filter control", createFilterControl);
            AddUntilStep("wait for load", () => filterControl.IsLoaded);
            AddStep("filter by star rating", () => filterControl.Search("stars>1"));
            AddStep("hook filter event", () => filterControl.CriteriaChanged += _ => filterCount++);

            AddStep("change rate", () => doubleTime.SpeedChange.Value = 1.8);
            AddUntilStep("filtered once", () => filterCount, () => Is.EqualTo(1));
        }

        [TestCaseSource(typeof(TestSceneBeatmapDifficultyCache), nameof(TestSceneBeatmapDifficultyCache.DIFFICULTY_MODS_WITHOUT_DIFFICULTY_INTERFACES))]
        public void TestDifficultyModToggleRefilters(Mod sourceMod)
        {
            var mod = sourceMod.DeepClone();
            int filterCount = 0;

            AddUntilStep("wait for load", () => filterControl.IsLoaded);
            AddStep("filter by star rating", () => filterControl.Search("stars>1"));
            AddStep("hook filter event", () => filterControl.CriteriaChanged += _ => filterCount++);

            AddStep($"select {mod.Acronym}", () => SelectedMods.Value = new[] { mod });
            AddUntilStep("filtered once", () => filterCount, () => Is.EqualTo(1));

            AddStep($"deselect {mod.Acronym}", () => SelectedMods.Value = Array.Empty<Mod>());
            AddUntilStep("filtered twice", () => filterCount, () => Is.EqualTo(2));
        }

        [Test]
        public void TestMagnetisedStrengthChangeRefilters()
        {
            var magnetised = new OsuModMagnetised();
            int filterCount = 0;

            AddUntilStep("wait for load", () => filterControl.IsLoaded);
            AddStep("filter by star rating", () => filterControl.Search("stars>1"));
            AddStep("select magnetised", () => SelectedMods.Value = new Mod[] { magnetised });
            AddStep("hook filter event", () => filterControl.CriteriaChanged += _ => filterCount++);

            AddStep("change attraction strength", () => magnetised.AttractionStrength.Value = 0.8f);
            AddUntilStep("filtered once", () => filterCount, () => Is.EqualTo(1));
        }

        private void createFilterControl()
        {
            Child = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Child = filterControl = new FilterControl
                {
                    State = { Value = Visibility.Visible },
                    RelativeSizeAxes = Axes.X,
                },
            };
        }
    }
}
