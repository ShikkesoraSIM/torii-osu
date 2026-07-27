// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.Osu.Tests
{
    /// <summary>
    /// torii: the Hit Sounder moves hitsounds onto your keypresses, which means
    /// it has to take them OFF the notes or every hit sounds twice. That strip
    /// happens in <c>ApplyToBeatmap</c> and is the part worth guarding: it edits
    /// the playable beatmap in place, and getting it wrong is either a doubled
    /// sound or a silent slider.
    /// </summary>
    [TestFixture]
    public class OsuModHitSounderTest
    {
        private static HitSampleInfo whistle(string bank = HitSampleInfo.BANK_DRUM)
            => new HitSampleInfo(HitSampleInfo.HIT_WHISTLE, bank);

        private static Beatmap<OsuHitObject> beatmapWith(params OsuHitObject[] objects)
        {
            var beatmap = new Beatmap<OsuHitObject> { BeatmapInfo = { Ruleset = new OsuRuleset().RulesetInfo } };
            beatmap.HitObjects.AddRange(objects);
            return beatmap;
        }

        [Test]
        public void TestTopLevelSamplesAreStripped()
        {
            var circle = new HitCircle { StartTime = 1000, Samples = new List<HitSampleInfo> { whistle() } };
            var beatmap = beatmapWith(circle);

            new OsuModHitSounder().ApplyToBeatmap(beatmap);

            Assert.That(circle.Samples, Is.Empty, "the note kept its samples, so a hit would sound twice");
        }

        [Test]
        public void TestNestedSamplesAreLeftAlone()
        {
            // Slider ticks and tails are not driven by your keys, so silencing
            // them would just make long sliders go quiet.
            var slider = new Slider
            {
                StartTime = 1000,
                Samples = new List<HitSampleInfo> { whistle() },
                Path = new SliderPath(PathType.LINEAR, new[] { new osuTK.Vector2(), new osuTK.Vector2(100, 0) }),
                NodeSamples = new List<IList<HitSampleInfo>>
                {
                    new List<HitSampleInfo> { whistle() },
                    new List<HitSampleInfo> { whistle() },
                },
            };

            var beatmap = beatmapWith(slider);
            slider.ApplyDefaults(beatmap.ControlPointInfo, beatmap.Difficulty);

            int nestedWithSamplesBefore = slider.NestedHitObjects.Count(h => h.Samples.Any());

            new OsuModHitSounder().ApplyToBeatmap(beatmap);

            Assert.That(slider.Samples, Is.Empty, "the slider head should be silent, your key covers it");
            Assert.That(
                slider.NestedHitObjects.Count(h => h.Samples.Any()),
                Is.EqualTo(nestedWithSamplesBefore),
                "nested samples (ticks, tail) must survive");
        }

        [Test]
        public void TestStrippingDoesNotLoseTheSamplesWeNeedToReplay()
        {
            // The whole point of stripping is that the mod already holds a copy,
            // so a press can still borrow the mapper's hitsound. If the copy were
            // taken by reference the strip would empty it too and every press
            // would fall back to the plain bank.
            var circle = new HitCircle { StartTime = 1000, Samples = new List<HitSampleInfo> { whistle() } };
            var beatmap = beatmapWith(circle);

            var mod = new OsuModHitSounder();
            mod.ApplyToBeatmap(beatmap);

            Assert.That(mod.SamplesForTesting(1000).Select(s => s.LookupNames.First()),
                Has.Some.Contains(HitSampleInfo.HIT_WHISTLE),
                "a press on the note should still borrow its whistle");
        }

        [Test]
        public void TestPressFarFromAnyNoteFallsBackToTheSectionBank()
        {
            var circle = new HitCircle { StartTime = 1000, Samples = new List<HitSampleInfo> { whistle() } };
            var beatmap = beatmapWith(circle);

            var mod = new OsuModHitSounder();
            mod.ApplyToBeatmap(beatmap);

            // Ten seconds away is a break by any measure: no whistle should leak
            // into it, just the plain hitnormal.
            var names = mod.SamplesForTesting(11000).SelectMany(s => s.LookupNames).ToArray();

            Assert.That(names, Has.None.Contains(HitSampleInfo.HIT_WHISTLE), "a lone whistle in a break sounds wrong");
            Assert.That(names, Has.Some.Contains(HitSampleInfo.HIT_NORMAL));
        }
    }
}
