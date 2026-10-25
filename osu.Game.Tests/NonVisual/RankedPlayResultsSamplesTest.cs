// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Moq;
using NUnit.Framework;
using osu.Framework.Audio.Sample;
using osu.Framework.IO.Stores;
using osu.Game.Resources;
using osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay;

namespace osu.Game.Tests.NonVisual
{
    [TestFixture]
    public class RankedPlayResultsSamplesTest
    {
        [Test]
        public void TestLegacySamplePreferred()
        {
            var store = new Mock<ISampleStore>();
            store.Setup(s => s.Get(ResultsScreen.DAMAGE_HIT_SAMPLE)).Returns(new SampleVirtual("legacy"));

            using var sample = ResultsScreen.LoadResultSample(store.Object, ResultsScreen.DAMAGE_HIT_SAMPLE, ResultsScreen.DAMAGE_HIT_FALLBACK_SAMPLE);

            Assert.That(sample.Name, Is.EqualTo("legacy"));
            store.Verify(s => s.Get(ResultsScreen.DAMAGE_HIT_FALLBACK_SAMPLE), Times.Never);
        }

        [Test]
        public void TestRenamedSampleFallback()
        {
            var store = new Mock<ISampleStore>();
            store.Setup(s => s.Get(ResultsScreen.DAMAGE_HIT_FALLBACK_SAMPLE)).Returns(new SampleVirtual("medium"));

            using var sample = ResultsScreen.LoadResultSample(store.Object, ResultsScreen.DAMAGE_HIT_SAMPLE, ResultsScreen.DAMAGE_HIT_FALLBACK_SAMPLE);

            Assert.That(sample.Name, Is.EqualTo("medium"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TestMissingSampleDoesNotThrow(bool withFallback)
        {
            var store = new Mock<ISampleStore>();
            using var sample = ResultsScreen.LoadResultSample(store.Object, "missing", withFallback ? "also-missing" : null);

            Assert.That(sample.Length, Is.Zero);
            Assert.That(sample.GetChannel(), Is.Not.Null);
        }

        [Test]
        public void TestPackagedDamageSoundExists()
        {
            using var resources = new DllResourceStore(OsuResources.ResourceAssembly);
            string expectedName = resources.Get($"Samples/{ResultsScreen.DAMAGE_HIT_SAMPLE}.wav") != null
                ? ResultsScreen.DAMAGE_HIT_SAMPLE
                : ResultsScreen.DAMAGE_HIT_FALLBACK_SAMPLE;

            Assert.That(resources.Get($"Samples/{expectedName}.wav"), Is.Not.Null);

            var store = new Mock<ISampleStore>();
            store.Setup(s => s.Get(It.IsAny<string>())).Returns((string name) => resources.Get($"Samples/{name}.wav") != null ? new SampleVirtual(name) : null!);

            using var sample = ResultsScreen.LoadResultSample(store.Object, ResultsScreen.DAMAGE_HIT_SAMPLE, ResultsScreen.DAMAGE_HIT_FALLBACK_SAMPLE);

            Assert.That(sample.Name, Is.EqualTo(expectedName));
        }
    }
}
