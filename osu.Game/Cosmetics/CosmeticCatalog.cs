// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Cosmetics
{
    /// <summary>Rough rarity bucket for a store cosmetic. Drives pricing and
    /// how it's presented in the shop.</summary>
    public enum CosmeticTier
    {
        Basic,
        Special,
        Premium,
    }

    /// <summary>
    /// One purchasable cursor-trail cosmetic: stable id, display name, tier, a
    /// points price, and a factory that builds the configured trail drawable
    /// (either a smooth ribbon trail or a shaped particle trail).
    ///
    /// Prices are PLACEHOLDERS for design/preview. The shop is points-only
    /// (earned, never bought with money); the authoritative price + ownership
    /// will live server-side when the shop endpoint ships.
    /// </summary>
    public sealed class CosmeticTrailDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public CosmeticTier Tier { get; }
        public int Price { get; }

        /// <summary>Render family, so the shop shows only the sliders that make
        /// sense (e.g. no density slider for a continuous ribbon).</summary>
        public CosmeticTrailFamily Family { get; }

        private readonly Func<Drawable> factory;

        public CosmeticTrailDefinition(string id, string name, CosmeticTier tier, int price, CosmeticTrailFamily family, Func<Drawable> factory)
        {
            Id = id;
            Name = name;
            Tier = tier;
            Price = price;
            Family = family;
            this.factory = factory;
        }

        /// <summary>Build a fresh trail drawable. It implements
        /// <see cref="ICosmeticTrail"/> so callers can drive/tune it.</summary>
        public Drawable Create() => factory();
    }

    /// <summary>
    /// The client-side catalog of cursor-trail cosmetics. Two families:
    ///   - smooth ribbon trails (solid / gradient / rainbow), and
    ///   - shaped particle trails (each with its own form AND motion).
    /// Adding a trail is one entry here; the preview scene and (later) the shop
    /// both read it.
    /// </summary>
    public static class CosmeticCatalog
    {
        private static readonly Color4 crimson = new Color4(225, 55, 70, 255);
        private static readonly Color4 ocean = new Color4(55, 150, 240, 255);
        private static readonly Color4 mint = new Color4(95, 225, 170, 255);
        private static readonly Color4 gold = new Color4(245, 200, 75, 255);
        private static readonly Color4 violet = new Color4(170, 110, 235, 255);
        private static readonly Color4 pearl = new Color4(245, 245, 255, 255);

        public static readonly IReadOnlyList<CosmeticTrailDefinition> Trails = new[]
        {
            // ── Smooth ribbons: solid colours (normal blend keeps colour true).
            solid("trail-pearl", "Pearl", 150, pearl),
            solid("trail-crimson", "Crimson", 300, crimson),
            solid("trail-ocean", "Ocean", 300, ocean),
            solid("trail-mint", "Mint", 300, mint),
            solid("trail-gold", "Gold", 300, gold),
            solid("trail-violet", "Amethyst", 300, violet),

            // ── Smooth ribbons: two-colour gradients, additive glow.
            gradient("trail-sunset", "Sunset", 900, new Color4(255, 170, 60, 255), new Color4(235, 60, 150, 255)),
            gradient("trail-ember", "Ember", 900, new Color4(255, 225, 80, 255), new Color4(225, 40, 30, 255)),
            gradient("trail-frost", "Frost", 900, new Color4(235, 250, 255, 255), new Color4(70, 180, 255, 255)),

            // ── Smooth ribbons: premium animated spectra.
            smooth("trail-aurora", "Aurora", CosmeticTier.Premium, 2500, t =>
            {
                // Same soft "engined" dot style as Rainbow (Engined), but an
                // aurora palette (green -> teal -> violet) instead of the wheel.
                t.ColourMode = ToriiCosmeticTrail.TrailColourMode.Palette;
                t.Palette = new[] { new Color4(80, 240, 160, 255), new Color4(70, 210, 220, 255), new Color4(150, 100, 230, 255) };
                t.FadeDurationOverride = 1100;
                t.IntervalMultiplierOverride = 0.3f;
                t.Thickness = 26f;
                t.Blending = BlendingParameters.Additive;
            }),
            smooth("trail-rainbow-engined", "Rainbow (Engined)", CosmeticTier.Premium, 4000, t =>
            {
                t.ColourMode = ToriiCosmeticTrail.TrailColourMode.Rainbow;
                t.HueBase = 0f;
                t.HueSpread = 1f;
                t.HueCycleSpeed = 0.40f;
                t.FadeDurationOverride = 1300;
                t.IntervalMultiplierOverride = 0.28f;
                t.Thickness = 28f;
                t.Blending = BlendingParameters.Additive;
            }),

            // ── Shaped particle trails: each its own form + motion.
            particle("trail-bubbles", "Bubbles", CosmeticTier.Basic, 400, CosmeticParticles.Bubble, t =>
            {
                t.Drift = new Vector2(0, -24); t.DriftJitter = 12; t.SpawnInterval = 26;
                t.ParticleLifetime = 950; t.StartScale = 0.5f; t.EndScale = 1.3f; t.MaxAlive = 90;
            }),
            particle("trail-starlight", "Starlight", CosmeticTier.Special, 1000, CosmeticParticles.Star, t =>
            {
                t.Drift = new Vector2(0, -8); t.DriftJitter = 10; t.SpinDegrees = 140; t.SpawnInterval = 22;
                t.ParticleLifetime = 800; t.StartScale = 1.1f; t.EndScale = 0.4f;
            }),
            particle("trail-lovestruck", "Lovestruck", CosmeticTier.Special, 1000, CosmeticParticles.Heart, t =>
            {
                t.Drift = new Vector2(0, -26); t.DriftJitter = 14; t.SpawnInterval = 24;
                t.ParticleLifetime = 850; t.StartScale = 1f; t.EndScale = 0.5f;
                t.Blending = BlendingParameters.Inherit;
            }),
            particle("trail-sakura", "Sakura", CosmeticTier.Special, 1200, CosmeticParticles.Petal, t =>
            {
                t.Drift = new Vector2(6, 22); t.DriftJitter = 18; t.SpinDegrees = 220; t.SpawnInterval = 22;
                t.ParticleLifetime = 1000; t.StartScale = 1f; t.EndScale = 0.85f;
                t.Blending = BlendingParameters.Inherit;
            }),
            particle("trail-frostfall", "Frostfall", CosmeticTier.Special, 1000, CosmeticParticles.Snowflake, t =>
            {
                t.Drift = new Vector2(0, 20); t.DriftJitter = 12; t.SpinDegrees = 160; t.SpawnInterval = 24;
                t.ParticleLifetime = 1100; t.StartScale = 1f; t.EndScale = 0.75f;
                t.Blending = BlendingParameters.Inherit;
            }),
            particle("trail-melody", "Melody", CosmeticTier.Special, 1100, CosmeticParticles.Note, t =>
            {
                t.Drift = new Vector2(0, -24); t.DriftJitter = 16; t.SpawnInterval = 26;
                t.ParticleLifetime = 900; t.StartScale = 1f; t.EndScale = 0.7f;
                t.Blending = BlendingParameters.Inherit;
            }),
            particle("trail-inferno", "Inferno", CosmeticTier.Premium, 2200, CosmeticParticles.Flame, t =>
            {
                // Calmer than a full bonfire so it doesn't pull focus mid-play.
                t.Drift = new Vector2(0, -20); t.DriftJitter = 8; t.SpawnInterval = 18;
                t.ParticleLifetime = 600; t.StartScale = 1f; t.EndScale = 0.3f; t.MaxAlive = 140;
            }),
            particle("trail-stardust", "Stardust", CosmeticTier.Premium, 2800, CosmeticParticles.RainbowSparkle, t =>
            {
                t.Drift = new Vector2(0, -6); t.DriftJitter = 12; t.SpinDegrees = 60; t.SpawnInterval = 12;
                t.ParticleLifetime = 850; t.StartScale = 1.5f; t.EndScale = 0.3f;
            }),

            // ── Connected RIBBON trails (a stitched mesh, not dots): finite,
            //    long, tapering, waving, flowing.
            ribbon("trail-comet", "Comet", CosmeticTier.Premium, 2600, t =>
            {
                // The simple one: clean white band, blue halo, bright head dot.
                t.ColourMode = CosmeticRibbonTrail.RibbonColourMode.Solid;
                t.PrimaryColour = new Color4(255, 255, 255, 255);
                t.GlowColour = new Color4(120, 180, 255, 255);
                t.Glow = true; t.HeadDot = true;
                t.Width = 12f; t.RibbonLifetime = 450;
            }),
            ribbon("trail-serpent", "Emerald Serpent", CosmeticTier.Special, 1300, t =>
            {
                // Finite, long, clean snake.
                t.ColourMode = CosmeticRibbonTrail.RibbonColourMode.Solid;
                t.PrimaryColour = new Color4(70, 230, 150, 255);
                t.Glow = false; t.Width = 11f; t.RibbonLifetime = 950;
            }),
            ribbon("trail-rainbow-ribbon", "Rainbow Ribbon", CosmeticTier.Premium, 3200, t =>
            {
                // The simple one: whole band cycles smoothly through the spectrum.
                t.ColourMode = CosmeticRibbonTrail.RibbonColourMode.Rainbow;
                t.Glow = true; t.HueCycleSpeed = 0.5f;
                t.Width = 12f; t.RibbonLifetime = 750;
            }),
            ribbon("trail-neon-flux", "Neon Flux", CosmeticTier.Premium, 3000, t =>
            {
                // Cyan core with a magenta halo (two-tone neon).
                t.ColourMode = CosmeticRibbonTrail.RibbonColourMode.Solid;
                t.PrimaryColour = new Color4(70, 240, 255, 255);
                t.GlowColour = new Color4(255, 80, 230, 255);
                t.Glow = true; t.Width = 9f; t.RibbonLifetime = 650;
            }),

            // ── "Fancy" segmented variants of the above: per-length colour +
            //    taper + tail fade (kept alongside the simple ones).
            ribbon("trail-comet-prime", "Comet Prime", CosmeticTier.Premium, 3200, t =>
            {
                t.Segmented = true;
                t.ColourMode = CosmeticRibbonTrail.RibbonColourMode.Gradient;
                t.PrimaryColour = new Color4(255, 255, 255, 255);
                t.SecondaryColour = new Color4(110, 170, 255, 255);
                t.HeadWidth = 18f; t.TailWidth = 2f; t.FadeTail = true;
                t.Glow = true; t.GlowColour = new Color4(120, 180, 255, 255); t.HeadDot = true;
                t.RibbonLifetime = 470;
            }),
            ribbon("trail-spectrum", "Spectrum", CosmeticTier.Premium, 3600, t =>
            {
                t.Segmented = true;
                t.ColourMode = CosmeticRibbonTrail.RibbonColourMode.Rainbow;
                t.HueSpread = 1f; t.HueCycleSpeed = 0.35f;
                t.HeadWidth = 12f; t.TailWidth = 6f; t.FadeTail = true;
                t.Glow = true; t.RibbonLifetime = 820;
            }),
            ribbon("trail-neon-surge", "Neon Surge", CosmeticTier.Premium, 3400, t =>
            {
                t.Segmented = true;
                t.ColourMode = CosmeticRibbonTrail.RibbonColourMode.Gradient;
                t.PrimaryColour = new Color4(70, 240, 255, 255);
                t.SecondaryColour = new Color4(255, 80, 230, 255);
                t.HeadWidth = 13f; t.TailWidth = 4f; t.FadeTail = true;
                t.Glow = true; t.GlowColour = new Color4(255, 90, 235, 255);
                t.RibbonLifetime = 700;
            }),

            // ── Creative / unique ribbons.
            ribbon("trail-nebula", "Nebula", CosmeticTier.Premium, 3400, t =>
            {
                // Deep-space band: purple -> magenta -> blue.
                t.Segmented = true;
                t.ColourMode = CosmeticRibbonTrail.RibbonColourMode.Palette;
                t.Palette = new[] { new Color4(150, 90, 240, 255), new Color4(230, 90, 200, 255), new Color4(80, 120, 255, 255) };
                t.HeadWidth = 16f; t.TailWidth = 10f; t.FadeTail = true;
                t.Glow = true; t.GlowColour = new Color4(160, 90, 240, 255);
                t.RibbonLifetime = 950;
            }),
            ribbon("trail-glitch", "Glitch", CosmeticTier.Premium, 2600, t =>
            {
                // Chromatic aberration: R/G/B copies that separate as you move.
                t.RgbSplit = true; t.RgbSplitOffset = 3.5f;
                t.Width = 8f; t.Glow = false; t.RibbonLifetime = 420;
            }),
            ribbon("trail-wisp", "Wisp", CosmeticTier.Special, 1400, t =>
            {
                // Ethereal: a big soft orb head trailing a thin faint wisp.
                t.ColourMode = CosmeticRibbonTrail.RibbonColourMode.Solid;
                t.PrimaryColour = new Color4(180, 240, 255, 255);
                t.GlowColour = new Color4(120, 200, 255, 255);
                t.Glow = true; t.HeadDot = true; t.HeadDotScale = 3.2f;
                t.Width = 5f; t.RibbonLifetime = 430;
            }),
            ribbon("trail-heartbeat", "Heartbeat", CosmeticTier.Special, 1300, t =>
            {
                // Width pulses like a heartbeat.
                t.ColourMode = CosmeticRibbonTrail.RibbonColourMode.Solid;
                t.PrimaryColour = new Color4(255, 80, 120, 255);
                t.GlowColour = new Color4(255, 60, 100, 255);
                t.Glow = true; t.Width = 9f;
                t.PulseAmount = 0.4f; t.PulseSpeed = 1.8f;
                t.RibbonLifetime = 600;
            }),

            // ── More shaped particle trails (varied forms + motion).
            particle("trail-confetti", "Confetti", CosmeticTier.Special, 1000, CosmeticParticles.Confetti, t =>
            {
                t.Drift = new Vector2(0, 26); t.DriftJitter = 22; t.SpinDegrees = 320; t.SpawnInterval = 20;
                t.ParticleLifetime = 1100; t.StartScale = 1f; t.EndScale = 0.9f;
                t.Blending = BlendingParameters.Inherit;
            }),
            particle("trail-smoke", "Smoke", CosmeticTier.Basic, 500, CosmeticParticles.Smoke, t =>
            {
                t.Drift = new Vector2(0, -18); t.DriftJitter = 14; t.SpawnInterval = 18;
                t.ParticleLifetime = 900; t.StartScale = 0.5f; t.EndScale = 2.2f; // billow out
            }),
            particle("trail-prism", "Prism", CosmeticTier.Special, 1200, CosmeticParticles.Geometric, t =>
            {
                t.Drift = new Vector2(0, -10); t.DriftJitter = 12; t.SpinDegrees = 220; t.SpawnInterval = 22;
                t.ParticleLifetime = 850; t.StartScale = 1f; t.EndScale = 0.4f;
            }),
            particle("trail-galaxy", "Galaxy", CosmeticTier.Premium, 2600, CosmeticParticles.GalaxyDust, t =>
            {
                t.Drift = new Vector2(0, -5); t.DriftJitter = 14; t.SpawnInterval = 12;
                t.ParticleLifetime = 1000; t.StartScale = 1f; t.EndScale = 0.5f; t.MaxAlive = 220;
            }),
            particle("trail-arcade", "Arcade", CosmeticTier.Special, 900, CosmeticParticles.Pixel, t =>
            {
                t.Drift = new Vector2(0, -8); t.DriftJitter = 16; t.SpawnInterval = 16;
                t.ParticleLifetime = 700; t.StartScale = 1f; t.EndScale = 1f;
                t.Blending = BlendingParameters.Inherit;
            }),
            particle("trail-storm", "Storm", CosmeticTier.Premium, 2400, CosmeticParticles.Bolt, t =>
            {
                t.Drift = new Vector2(0, -6); t.DriftJitter = 18; t.SpawnInterval = 24;
                t.ParticleLifetime = 500; t.StartScale = 1.2f; t.EndScale = 0.5f;
            }),
        };

        private static CosmeticTrailDefinition smooth(string id, string name, CosmeticTier tier, int price, Action<ToriiCosmeticTrail> configure)
            => new CosmeticTrailDefinition(id, name, tier, price, CosmeticTrailFamily.Dot, () =>
            {
                var t = new ToriiCosmeticTrail();
                configure(t);
                return t;
            });

        private static CosmeticTrailDefinition particle(string id, string name, CosmeticTier tier, int price, Func<int, Drawable> factory, Action<CosmeticParticleTrail> configure)
            => new CosmeticTrailDefinition(id, name, tier, price, CosmeticTrailFamily.Particle, () =>
            {
                var t = new CosmeticParticleTrail { ParticleFactory = factory };
                configure(t);
                return t;
            });

        private static CosmeticTrailDefinition ribbon(string id, string name, CosmeticTier tier, int price, Action<CosmeticRibbonTrail> configure)
            => new CosmeticTrailDefinition(id, name, tier, price, CosmeticTrailFamily.Ribbon, () =>
            {
                var t = new CosmeticRibbonTrail();
                configure(t);
                return t;
            });

        private static CosmeticTrailDefinition solid(string id, string name, int price, Color4 colour)
            => smooth(id, name, CosmeticTier.Basic, price, t =>
            {
                t.ColourMode = ToriiCosmeticTrail.TrailColourMode.Solid;
                t.PrimaryColour = colour;
                t.FadeDurationOverride = 480;
                t.IntervalMultiplierOverride = 0.4f;
                t.Thickness = 24f;
                t.Blending = BlendingParameters.Inherit; // normal blend = true colour
            });

        private static CosmeticTrailDefinition gradient(string id, string name, int price, Color4 head, Color4 tail)
            => smooth(id, name, CosmeticTier.Special, price, t =>
            {
                t.ColourMode = ToriiCosmeticTrail.TrailColourMode.Gradient;
                t.PrimaryColour = head;
                t.SecondaryColour = tail;
                t.FadeDurationOverride = 620;
                t.IntervalMultiplierOverride = 0.38f;
                t.Thickness = 24f;
                t.Blending = BlendingParameters.Additive;
            });
    }
}
