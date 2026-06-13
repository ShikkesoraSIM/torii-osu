// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserEffects.Presets
{
    /// <summary>
    /// Catch Consul aura: small fruits falling gently from above the
    /// username through to the baseline, evoking the catch-the-fruits
    /// mode at a glance. Each drop is a fixed FontAwesome fruit glyph —
    /// mixing apple / pear / lemon shapes so simultaneous drops aren't
    /// all identical.
    ///
    /// The single particle type uses three pieces of motion to read as
    /// "falling fruit" rather than "particle drifting down":
    /// <list type="bullet">
    /// <item>Vertical fall (the dominant motion) over the full height of
    ///       the username with a small horizontal drift, simulating
    ///       gravity + air resistance.</item>
    /// <item>Slow rotation over the fall duration so the fruit "spins"
    ///       on its way down, matching how catch fruits tumble.</item>
    /// <item>Final fade-out timed to coincide with reaching the baseline
    ///       so the fruit appears to "land" rather than vanish mid-air.</item>
    /// </list>
    /// </summary>
    public class CatchConsulAuraPreset : AuraPreset
    {
        public const string ID = "catch-consul";

        // Warm orange / yellow / red fruit palette. Pulled from typical
        // catch hyperdash colour cues so the aura visually pairs with
        // real Catch gameplay imagery.
        private static readonly Color4 fruit_orange = new Color4(255, 152, 80, 255);
        private static readonly Color4 fruit_yellow = new Color4(255, 210, 90, 255);
        private static readonly Color4 fruit_red    = new Color4(240, 100, 90, 255);
        private static readonly Color4 fruit_lime   = new Color4(180, 220, 110, 255);

        // Warm halo to tie the four fruit tones together visually. Closer
        // to orange since orange is the most common fruit colour in the
        // particle stream.
        private static readonly Color4 halo_warm    = new Color4(255, 180, 110, 255);

        // Two fruit-shaped FontAwesome glyphs in the Solid bundle —
        // AppleAlt is the apple-with-stem silhouette, Lemon is the lemon
        // shape. The original sketch wanted a third "plain apple"
        // glyph, but FontAwesome's "Apple" lives in the Brands bundle
        // (it's the Apple Inc. logo) — not appropriate as a fruit. Two
        // silhouettes × four colours from the palette below already
        // give us eight visually distinct fruit variants per spawn, so
        // a falling cluster never looks like duplicate stickers.
        private static readonly IconUsage[] fruit_glyphs =
        {
            FontAwesome.Solid.AppleAlt,
            FontAwesome.Solid.Lemon,
        };

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-advisor" };

        public override IReadOnlyList<string>? RequiredPlaymodes { get; } = new[] { "fruits" };

        public override int DefaultPriority => 55;

        // Slightly faster than Founder embers — fruit drops should feel
        // like a gentle rainfall, not a sparse occasional event. MaxAlive
        // capped low because each fruit is large + colourful and four
        // simultaneously is already a busy visual.
        public override double SpawnIntervalMs => 420;
        public override double SpawnJitterMs => 220;
        public override int MaxAlive => 4;

        public override Color4? GlowColour => halo_warm;

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            float scale = ParticleScale(parentSize);
            float size = (6f + (float)random.NextDouble() * 2.5f) * scale;

            // Spawn ABOVE the username and fall through it. Negative
            // startY puts the fruit in the empty space above the box.
            float startX = (float)(0.1 + random.NextDouble() * 0.8) * parentSize.X;
            float startY = -parentSize.Y * (0.15f + (float)random.NextDouble() * 0.2f);
            // Land at or just below the baseline of the name.
            float endY   = parentSize.Y * (0.85f + (float)random.NextDouble() * 0.25f);
            // Small horizontal drift so vertical drops don't read as
            // perfectly straight (boring) or scatter wildly (random).
            float endX   = startX + (float)((random.NextDouble() - 0.5) * parentSize.X * 0.18f);

            Color4 colour = random.NextDouble() switch
            {
                < 0.35 => fruit_orange,
                < 0.65 => fruit_yellow,
                < 0.85 => fruit_red,
                _      => fruit_lime,
            };

            IconUsage icon = fruit_glyphs[random.Next(fruit_glyphs.Length)];

            var fruit = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Position = new Vector2(startX, startY),
                Icon = icon,
                Size = new Vector2(size),
                Colour = colour,
                Alpha = 0,
                Rotation = (float)((random.NextDouble() - 0.5) * 60),
            };

            parent.Add(fruit);

            double lifetime = 1400 + random.NextDouble() * 400;

            fruit.FadeTo(0.92f, 220, Easing.OutQuad);
            fruit.MoveTo(new Vector2(endX, endY), lifetime, Easing.InQuad);
            // Continuous slow spin — match how real catch fruits tumble.
            // Total rotation ~120° over the fall feels natural; faster
            // would read as a coin flip, slower as a static decal.
            fruit.RotateTo(fruit.Rotation + (random.Next(2) == 0 ? 120 : -120), lifetime, Easing.InOutSine);
            // Fade timed so the fruit visually "lands" then dissipates,
            // rather than vanishing in midair which reads as broken.
            fruit.Delay(lifetime - 320).FadeOut(320, Easing.InQuad).Expire();
        }
    }
}
