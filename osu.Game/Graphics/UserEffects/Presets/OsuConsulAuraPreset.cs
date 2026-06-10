// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserEffects.Presets
{
    /// <summary>
    /// osu! Consul aura: animated osu! hitcircle hits around the
    /// username. Each particle is a layered hitcircle (outer ring +
    /// inner pink fill + approach circle converging in from a larger
    /// radius), recreating the canonical "circle being hit" silhouette
    /// every osu! player recognises at a glance.
    ///
    /// First revision used a single FontAwesome ring glyph for each
    /// hit, which read as a generic outlined circle without the
    /// approach-circle motion that makes osu!'s hitcircle iconic.
    /// This version layers three primitives per particle (outer ring +
    /// inner fill + shrinking approach ring) so the silhouette reads
    /// as "osu! hit" rather than "decorative pink circle". Particles
    /// also spawn AROUND the username (positions can land outside the
    /// bounding box), which is what makes the aura read as an
    /// atmosphere instead of a decal inside the name frame.
    /// </summary>
    public class OsuConsulAuraPreset : AuraPreset
    {
        public const string ID = "osu-consul";

        // osu!-pink core + brighter outer ring. Two complementary tones
        // so the layered hitcircle has visible inner/outer contrast.
        private static readonly Color4 osu_pink_core = new Color4(255, 105, 180, 255); // hot pink
        private static readonly Color4 osu_pink_ring = new Color4(255, 180, 215, 255); // pale pink

        // Halo glow is a desaturated osu! pink so the username sits in
        // a faint pink atmosphere even between hits — gives the aura
        // a "this is osu!" baseline mood without depending on a
        // particle being alive at every moment.
        private static readonly Color4 halo_pink     = new Color4(255, 140, 190, 255);

        public override string AuraId => ID;

        // All four advisor groups share "torii-advisor"; RequiredPlaymodes
        // is what distinguishes osu / taiko / catch / mania.
        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-advisor" };

        public override IReadOnlyList<string>? RequiredPlaymodes { get; } = new[] { "osu" };

        public override int DefaultPriority => 55;

        // ~430ms between hits — comfortable mid-tempo, reads as a
        // player tapping rather than a hitstream blast.
        public override double SpawnIntervalMs => 430;
        public override double SpawnJitterMs => 180;
        public override int MaxAlive => 5;

        public override Color4? GlowColour => halo_pink;

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            // Spawn positions span beyond the username bounding box so
            // the aura reads as the player's "playfield" around the
            // name, not a confined effect inside the letter frame.
            float centerX = (float)(-0.10 + random.NextDouble() * 1.20) * parentSize.X;
            float centerY = (float)(-0.10 + random.NextDouble() * 1.20) * parentSize.Y;

            float scale = ParticleScale(parentSize);
            float hitSize = (7f + (float)random.NextDouble() * 3f) * scale;
            float approachStartSize = hitSize * 3.0f;

            // Inner pink fill of the hitcircle. Slightly smaller than
            // the outer ring so the ring reads as a border around it.
            var inner = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(hitSize * 0.78f),
                Colour = osu_pink_core,
                Alpha = 0.85f,
            };

            // Outer ring of the hitcircle — the bright outline. Drawn
            // as a CircularContainer with a borderless inner cut-out:
            // Box backing tinted, then a smaller dark inner hole. Or
            // alternatively, a Circle behind a smaller dark Circle —
            // we use the latter for simplicity.
            var outerRing = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(hitSize),
                Colour = osu_pink_ring,
                Alpha = 0.9f,
            };

            // Approach circle — large ring that shrinks toward the
            // hitcircle's outer radius. We approximate "ring" using a
            // Circle that has high BorderThickness, but since plain
            // Circle is filled, we instead use a CircularContainer with
            // border. That requires CircularContainer construction; to
            // keep the preset standalone, we approximate the approach
            // ring with a Circle that fades down to alpha while
            // shrinking — visually close to a converging ring at the
            // tile sizes this aura renders at.
            var approach = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(approachStartSize),
                Colour = osu_pink_ring,
                Alpha = 0.0f,
            };

            var hit = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(centerX, centerY),
                Children = new Drawable[]
                {
                    approach,
                    outerRing,
                    inner,
                },
                Alpha = 0,
                Scale = new Vector2(0.85f),
            };

            parent.Add(hit);

            // Total lifetime ~720ms — long enough for the approach
            // animation to read as "circle being hit", short enough
            // that the aura stays light at chat density.
            const double approach_time = 460;
            const double burst_time = 240;

            // Hitcircle fades in fully during the approach.
            hit.FadeTo(1f, 140, Easing.OutQuad);

            // Approach ring: starts large + invisible, fades up to mid
            // alpha, then shrinks to the hitcircle size as it fades
            // back down — recreates the converging approach circle.
            approach.FadeTo(0.55f, 120, Easing.OutQuad)
                    .Then().Delay(approach_time - 240)
                    .FadeOut(120, Easing.OutQuad);
            approach.ResizeTo(new Vector2(hitSize * 1.02f), approach_time, Easing.OutCubic);

            // After the approach lands, hitcircle "bursts" — a quick
            // outward scale + fade, the visual feedback the player gets
            // when they hit a real osu! circle.
            hit.Delay(approach_time).ScaleTo(1.35f, burst_time, Easing.OutQuad);
            hit.Delay(approach_time).FadeOut(burst_time, Easing.OutQuad).Expire();
        }
    }
}
