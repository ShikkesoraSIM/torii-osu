// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Cosmetics;
using osu.Game.Cosmetics.Definitions;
using osuTK.Graphics;

namespace osu.Game.Tests.NonVisual.Cosmetics
{
    /// <summary>
    /// torii: prueba el loop data-driven de la fase 1: una CosmeticDefinition (JSON) se serializa,
    /// se vuelve a parsear, y el CosmeticTrailFactory reconstruye el trail con TODAS las props bien
    /// (colores, enums, vectores, paleta, blending, shape de particula). o sea: un trail hecho con
    /// datos = identico al hecho con codigo. es la base de que la Creator exporte y Torii cargue.
    /// </summary>
    [TestFixture]
    public class CosmeticDefinitionTest
    {
        private static CosmeticDefinition roundTrip(CosmeticDefinition def)
            => CosmeticDefinition.Parse(def.Serialize());

        [Test]
        public void TestDotGradientRoundTrip()
        {
            var def = new CosmeticDefinition
            {
                Id = "trail-sunset",
                Name = "Sunset",
                Type = CosmeticType.Trail,
                Tier = CosmeticTier.Special,
                Price = 900,
                Family = CosmeticTrailFamily.Dot,
                Settings = new JObject
                {
                    ["ColourMode"] = "Gradient",
                    ["PrimaryColour"] = "#FFAA3C",
                    ["SecondaryColour"] = "#EB3C96",
                    ["FadeDurationOverride"] = 620,
                    ["IntervalMultiplierOverride"] = 0.38f,
                    ["Thickness"] = 24f,
                    ["Blending"] = "Additive",
                },
            };

            var parsed = roundTrip(def);
            Assert.That(parsed.Type, Is.EqualTo(CosmeticType.Trail));
            Assert.That(parsed.Family, Is.EqualTo(CosmeticTrailFamily.Dot));

            var trail = (ToriiCosmeticTrail)CosmeticTrailFactory.Create(parsed);
            Assert.That(trail.ColourMode, Is.EqualTo(ToriiCosmeticTrail.TrailColourMode.Gradient));
            assertColour(trail.PrimaryColour, new Color4(255, 170, 60, 255));
            assertColour(trail.SecondaryColour, new Color4(235, 60, 150, 255));
            Assert.That(trail.FadeDurationOverride, Is.EqualTo(620).Within(0.01));
            Assert.That(trail.IntervalMultiplierOverride, Is.EqualTo(0.38f).Within(0.0001));
            Assert.That(trail.Thickness, Is.EqualTo(24f).Within(0.0001));
            Assert.That(trail.Blending.Source, Is.EqualTo(BlendingParameters.Additive.Source));
        }

        [Test]
        public void TestDotPaletteRoundTrip()
        {
            var def = new CosmeticDefinition
            {
                Id = "trail-aurora",
                Name = "Aurora",
                Type = CosmeticType.Trail,
                Family = CosmeticTrailFamily.Dot,
                Settings = new JObject
                {
                    ["ColourMode"] = "Palette",
                    ["Palette"] = new JArray("#50F0A0", "#46D2DC", "#9664E6"),
                    ["HueCycleSpeed"] = 0.4f,
                },
            };

            var trail = (ToriiCosmeticTrail)CosmeticTrailFactory.Create(roundTrip(def));
            Assert.That(trail.ColourMode, Is.EqualTo(ToriiCosmeticTrail.TrailColourMode.Palette));
            Assert.That(trail.Palette, Is.Not.Null);
            Assert.That(trail.Palette.Length, Is.EqualTo(3));
            assertColour(trail.Palette[0], new Color4(80, 240, 160, 255));
            assertColour(trail.Palette[2], new Color4(150, 100, 230, 255));
            Assert.That(trail.HueCycleSpeed, Is.EqualTo(0.4f).Within(0.0001));
        }

        [Test]
        public void TestParticleShapeRoundTrip()
        {
            var def = new CosmeticDefinition
            {
                Id = "trail-starlight",
                Name = "Starlight",
                Type = CosmeticType.Trail,
                Family = CosmeticTrailFamily.Particle,
                Settings = new JObject
                {
                    ["ParticleShape"] = "star",
                    ["Drift"] = new JArray(0, -8),
                    ["DriftJitter"] = 10f,
                    ["SpinDegrees"] = 140f,
                    ["SpawnInterval"] = 22f,
                    ["ParticleLifetime"] = 800,
                    ["StartScale"] = 1.1f,
                    ["EndScale"] = 0.4f,
                },
            };

            var trail = (CosmeticParticleTrail)CosmeticTrailFactory.Create(roundTrip(def));
            Assert.That(trail.ParticleFactory, Is.Not.Null, "la shape 'star' se resolvio a un factory");
            Assert.That(trail.Drift.X, Is.EqualTo(0f).Within(0.0001));
            Assert.That(trail.Drift.Y, Is.EqualTo(-8f).Within(0.0001));
            Assert.That(trail.SpinDegrees, Is.EqualTo(140f).Within(0.0001));
            Assert.That(trail.SpawnInterval, Is.EqualTo(22f).Within(0.0001));
            Assert.That(trail.ParticleLifetime, Is.EqualTo(800).Within(0.01));
            Assert.That(trail.StartScale, Is.EqualTo(1.1f).Within(0.0001));
            Assert.That(trail.EndScale, Is.EqualTo(0.4f).Within(0.0001));
        }

        [Test]
        public void TestRibbonRoundTrip()
        {
            var def = new CosmeticDefinition
            {
                Id = "trail-comet",
                Name = "Comet",
                Type = CosmeticType.Trail,
                Family = CosmeticTrailFamily.Ribbon,
                Settings = new JObject
                {
                    ["ColourMode"] = "Solid",
                    ["PrimaryColour"] = "#FFFFFF",
                    ["GlowColour"] = "#78B4FF",
                    ["Glow"] = true,
                    ["HeadDot"] = true,
                    ["Width"] = 12f,
                    ["RibbonLifetime"] = 450,
                },
            };

            var trail = (CosmeticRibbonTrail)CosmeticTrailFactory.Create(roundTrip(def));
            Assert.That(trail.ColourMode, Is.EqualTo(CosmeticRibbonTrail.RibbonColourMode.Solid));
            assertColour(trail.PrimaryColour, new Color4(255, 255, 255, 255));
            assertColour(trail.GlowColour, new Color4(120, 180, 255, 255));
            Assert.That(trail.Glow, Is.True);
            Assert.That(trail.HeadDot, Is.True);
            Assert.That(trail.Width, Is.EqualTo(12f).Within(0.0001));
            Assert.That(trail.RibbonLifetime, Is.EqualTo(450).Within(0.01));
        }

        [Test]
        public void TestUnknownParticleShapeFallsBackSafely()
        {
            // data de la comunidad podria traer una shape que no existe -> no debe romper.
            var def = new CosmeticDefinition
            {
                Type = CosmeticType.Trail,
                Family = CosmeticTrailFamily.Particle,
                Settings = new JObject { ["ParticleShape"] = "definitely-not-a-real-shape" },
            };

            var trail = (CosmeticParticleTrail)CosmeticTrailFactory.Create(roundTrip(def));
            Assert.That(trail.ParticleFactory, Is.Not.Null, "shape desconocida cae a un default en vez de null");
        }

        [Test]
        public void TestBadSettingValueIsSkippedNotThrown()
        {
            // un valor con tipo equivocado se saltea, no tira (data defensiva).
            var def = new CosmeticDefinition
            {
                Type = CosmeticType.Trail,
                Family = CosmeticTrailFamily.Dot,
                Settings = new JObject
                {
                    ["Thickness"] = "no-soy-un-numero",
                    ["ColourMode"] = "Rainbow",
                },
            };

            ToriiCosmeticTrail trail = null;
            Assert.DoesNotThrow(() => trail = (ToriiCosmeticTrail)CosmeticTrailFactory.Create(roundTrip(def)));
            // el valor bueno igual se aplico:
            Assert.That(trail.ColourMode, Is.EqualTo(ToriiCosmeticTrail.TrailColourMode.Rainbow));
        }

        [Test]
        public void TestAllParticleShapesResolve()
        {
            foreach (string shape in CosmeticParticleShapes.Names.ToArray())
                Assert.That(CosmeticParticleShapes.Get(shape), Is.Not.Null, $"shape '{shape}' resuelve");
        }

        private static void assertColour(Color4 actual, Color4 expected)
        {
            Assert.That(actual.R, Is.EqualTo(expected.R).Within(0.004), "R");
            Assert.That(actual.G, Is.EqualTo(expected.G).Within(0.004), "G");
            Assert.That(actual.B, Is.EqualTo(expected.B).Within(0.004), "B");
            Assert.That(actual.A, Is.EqualTo(expected.A).Within(0.004), "A");
        }
    }
}
