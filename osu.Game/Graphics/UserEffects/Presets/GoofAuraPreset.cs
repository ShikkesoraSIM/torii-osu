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
    /// Goof aura: small chibi leaves slowly hover-drifting around the name with
    /// a soft pastel-green halo. Reads as cute + playful — matches the goofball
    /// energy without being visually loud. Each leaf rotates slowly as it
    /// drifts and pulses subtly in size to feel alive rather than mechanical.
    /// </summary>
    public class GoofAuraPreset : AuraPreset
    {
        public const string ID = "goof-leaves";

        // Pastel-green palette tuned around the GOOF badge colour (#9CE5A0).
        // Three nearby tones so a cluster of leaves doesn't look uniform.
        private static readonly Color4[] leaf_palette =
        {
            new Color4(156, 229, 160, 255), // base GOOF green
            new Color4(190, 240, 175, 255), // lime
            new Color4(120, 210, 145, 255), // deeper sage
        };

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-goof" };

        public override int DefaultPriority => 50;

        // Slower & sparser than admin embers — the visual goal is "a couple of
        // leaves drifting around" not "swarm".
        public override double SpawnIntervalMs => 520;
        public override double SpawnJitterMs => 280;
        public override int MaxAlive => 5;

        // Soft pastel-green halo hugging the username letters. Pulled from
        // the leaf palette base tone so the glow + leaves read as the same
        // GOOF green family rather than a halo and particles in slightly
        // different greens.
        public override Color4? GlowColour => leaf_palette[0];

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            // Spawn region intentionally extends slightly OUTSIDE the username
            // bounding box so leaves appear to hover around the label rather
            // than stuck on top of the letters. The previous tuning kept
            // spawn fully inside (5%..95% × 10%..90%) which made the leaves
            // bunch on the text and read as "leaves stuck in the name", not
            // "leaves drifting around it". Going to ±15% past each edge gives
            // the goof aura its gentle "halo of leaves" feel without becoming
            // chaotic. Particle count stays MaxAlive=5 so the wider envelope
            // doesn't suddenly look crowded.
            float startX = (float)(-0.15 + random.NextDouble() * 1.30) * parentSize.X;
            float startY = (float)(-0.20 + random.NextDouble() * 1.40) * parentSize.Y;

            // Drift roughly doubled vs the prior tuning — leaves now actually
            // travel a noticeable distance rather than barely wandering. Kept
            // symmetric in X so leaves on either side of the name drift toward
            // OR away from it with equal probability; in Y biased upward
            // slightly (centre at -0.05*H) so the overall feel is "lifting"
            // rather than equally falling.
            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.5f);
            float driftY = (float)((random.NextDouble() - 0.55) * parentSize.Y * 0.7f);

            float size = (5.5f + (float)random.NextDouble() * 3f) * ParticleScale(parentSize);
            Color4 colour = leaf_palette[random.Next(leaf_palette.Length)];

            // The leaf glyph itself plus a fainter, larger halo behind it to
            // sell the glow without needing a real blur shader. Halo scaled
            // down (1.4x vs 1.8x) so the glow doesn't bleed too far past edges.
            var halo = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Leaf,
                Size = new Vector2(size * 1.4f),
                Colour = colour,
                Alpha = 0.16f,
            };

            var leaf = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Leaf,
                Size = new Vector2(size),
                Colour = colour,
                Alpha = 0.85f,
                Rotation = (float)((random.NextDouble() - 0.5) * 60), // varied initial tilt
            };

            // Wrap in a centre-anchored container so we can rotate/scale around
            // the leaf's own midpoint without dragging the position offset.
            var particle = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { halo, leaf },
                Alpha = 0,
            };

            parent.Add(particle);

            double lifetime = 1800 + random.NextDouble() * 900;
            float endRotation = leaf.Rotation + (float)((random.NextDouble() - 0.5) * 90);

            // Fade in slow & soft, drift to destination, gently rotate, then fade out.
            // The hover bob is a nested loop on the inner leaf so it bobs INSIDE
            // the drifting envelope — gives a real "floating" feel rather than
            // just translation.
            particle.FadeTo(1f, 320, Easing.OutQuad);
            particle.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.InOutSine);
            leaf.RotateTo(endRotation, lifetime, Easing.InOutSine);

            // Gentle hover bob (1.5-2.5px Y oscillation) layered on top of the
            // drift. Reduced from earlier 3-4px so the bob doesn't add to the
            // visual spread that pushed leaves outside the name area.
            float bob = 1.5f + (float)random.NextDouble();
            leaf.Loop(t => t
                .MoveToOffset(new Vector2(0, -bob), 700, Easing.InOutSine)
                .Then()
                .MoveToOffset(new Vector2(0, bob), 700, Easing.InOutSine));

            // Subtle scale pulse so the leaf feels chibi-alive rather than static.
            leaf.ScaleTo(1.08f, 600, Easing.InOutSine).Then().ScaleTo(1f, 600, Easing.InOutSine).Loop();

            particle.Delay(lifetime - 400).FadeOut(400, Easing.OutQuad).Expire();
        }
    }
}
