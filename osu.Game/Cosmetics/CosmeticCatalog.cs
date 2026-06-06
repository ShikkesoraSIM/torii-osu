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

        private readonly Func<Drawable> factory;

        public CosmeticTrailDefinition(string id, string name, CosmeticTier tier, int price, Func<Drawable> factory)
        {
            Id = id;
            Name = name;
            Tier = tier;
            Price = price;
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
                t.ColourMode = ToriiCosmeticTrail.TrailColourMode.Rainbow;
                t.HueBase = 0.32f;
                t.HueSpread = 0.42f;
                t.HueCycleSpeed = 0.12f;
                t.FadeDurationOverride = 950;
                t.IntervalMultiplierOverride = 0.34f;
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
                t.Drift = new Vector2(0, -20); t.DriftJitter = 8; t.SpawnInterval = 14;
                t.ParticleLifetime = 600; t.StartScale = 1.2f; t.EndScale = 0.3f; t.MaxAlive = 200;
            }),
            particle("trail-stardust", "Stardust", CosmeticTier.Premium, 2800, CosmeticParticles.RainbowSparkle, t =>
            {
                t.Drift = new Vector2(0, -6); t.DriftJitter = 12; t.SpinDegrees = 60; t.SpawnInterval = 18;
                t.ParticleLifetime = 800; t.StartScale = 1f; t.EndScale = 0.3f;
            }),
        };

        private static CosmeticTrailDefinition smooth(string id, string name, CosmeticTier tier, int price, Action<ToriiCosmeticTrail> configure)
            => new CosmeticTrailDefinition(id, name, tier, price, () =>
            {
                var t = new ToriiCosmeticTrail();
                configure(t);
                return t;
            });

        private static CosmeticTrailDefinition particle(string id, string name, CosmeticTier tier, int price, Func<int, Drawable> factory, Action<CosmeticParticleTrail> configure)
            => new CosmeticTrailDefinition(id, name, tier, price, () =>
            {
                var t = new CosmeticParticleTrail { ParticleFactory = factory };
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
