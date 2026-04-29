// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserEffects.Presets
{
    /// <summary>
    /// Admin aura: a low pulsing red halo behind the name plus tight columns of
    /// rising "sparks" — tapered vertical lines with a glowing head — and the
    /// occasional star-burst flash for visual variety. Reads as authority +
    /// barely-contained heat, without the lazy "circles popping in" feel the
    /// first revision had. Tightly bounded so particles stay visually anchored
    /// to the username and don't drift far outside its box.
    /// </summary>
    public class AdminAuraPreset : AuraPreset
    {
        public const string ID = "admin-embers";

        // Hot palette tuned around bright cherry / amber so multiple stacking
        // particles read as fire rather than monochrome dots.
        private static readonly Color4 spark_bright = new Color4(255, 200, 140, 255);
        private static readonly Color4 spark_red    = new Color4(255, 80, 60, 255);
        private static readonly Color4 spark_amber  = new Color4(255, 140, 80, 255);

        // Glow tone is intentionally MUCH softer than the spark colours —
        // earlier (255,60,50) was the same straight cherry red as the spark
        // particles, which read as a harsh saturated wash behind the name
        // (especially in chat where the username sits on a dark grey row at
        // 13pt; the glow drowned out everything around it). The web profile
        // paints the admin name in a coral-leaning lighter red and that's
        // the look the user is asking us to match. (255,140,110) lands in
        // that coral family while still being recognisably "admin red"
        // when it pulses behind the letters.
        private static readonly Color4 halo_red     = new Color4(255, 140, 110, 255);

        public override string AuraId => ID;

        // The "torii-admin" identifier matches APIUserGroup.Identifier on the
        // client. Note that the server-side catalog uses the bare key "admin"
        // — they're different vocabularies on purpose; the client only ever
        // sees the prefixed identifier on incoming user payloads.
        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-admin" };

        // Highest priority — admin should win the default-aura fallback when
        // a user has multiple roles and no explicit pick.
        public override int DefaultPriority => 0;

        // Faster cadence than goof (admin should feel "energetic") but capped
        // so a chat full of admins doesn't tank framerate.
        public override double SpawnIntervalMs => 180;
        public override double SpawnJitterMs => 100;
        public override int MaxAlive => 9;

        // Soft red glow hugging the username letters. Re-introduced after
        // the rectangular PulsingHalo was scrapped — TextShapeGlow follows
        // letter outlines via blur instead of a Box bounding box, so the
        // "visually square" complaint that killed the previous halo no
        // longer applies. Lower-saturation halo_red so the glow reads as
        // ambient warmth rather than competing with the bright spark heads.
        public override Color4? GlowColour => halo_red;

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            // 70% spark (rising tapered line), 25% sparkle (bright star burst),
            // 5% slow ember (round, drifts higher) — distribution tuned so the
            // dominant motion is upward sparks with occasional brighter accents.
            double roll = random.NextDouble();
            if (roll < 0.70)
                emitSpark(parent, parentSize, random);
            else if (roll < 0.95)
                emitSparkle(parent, parentSize, random);
            else
                emitEmber(parent, parentSize, random);
        }

        // Thin tapered line rising fast and short. The dominant particle type —
        // gives the aura its "shower of sparks" character.
        private void emitSpark(Container parent, Vector2 parentSize, Random random)
        {
            // Spawn from the bottom 35% of the name, columns spread across the
            // full width. Tighter than the original (which used 60-100% Y).
            float startX = (float)(0.05 + random.NextDouble() * 0.9) * parentSize.X;
            float startY = parentSize.Y * (0.65f + (float)random.NextDouble() * 0.3f);

            // Mostly straight up with a tiny lateral wobble. Drift height is
            // ~70% of the name height — keeps sparks visually anchored.
            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.12f);
            float driftY = -parentSize.Y * (0.55f + (float)random.NextDouble() * 0.35f);

            // Scale spark size with parent height so chat rows (~13px) get
            // proportionate tiny sparks instead of full-size 5–9px lines that
            // were visually dwarfing the username.
            float scale = ParticleScale(parentSize);
            float length = (5f + (float)random.NextDouble() * 4f) * scale;

            Color4 colour = random.NextDouble() < 0.5
                ? spark_red
                : (random.NextDouble() < 0.5 ? spark_amber : spark_bright);

            // Bright "head" + slightly longer dim "tail" stacked for a tapered
            // look. Both additive so they bloom into each other and the halo.
            var head = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.BottomCentre,
                Width = 1.5f,
                Height = length * 0.35f,
                Colour = colour,
                Alpha = 0.95f,
            };

            var tail = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.TopCentre,
                Width = 1.0f,
                Height = length * 0.65f,
                Colour = colour,
                Alpha = 0.5f,
            };

            var particle = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { tail, head },
                Alpha = 0,
            };

            parent.Add(particle);

            double lifetime = 600 + random.NextDouble() * 400;
            particle.FadeTo(1f, 80, Easing.OutQuad);
            particle.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutCubic);
            particle.ScaleTo(0.6f, lifetime, Easing.OutQuad);
            particle.Delay(lifetime - 180).FadeOut(180, Easing.InQuad).Expire();
        }

        // Star-shaped flash that pops in, scales up, fades. Sparser than the
        // sparks; gives the aura its "static charge" beat.
        private void emitSparkle(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(0.1 + random.NextDouble() * 0.8) * parentSize.X;
            float startY = (float)(0.2 + random.NextDouble() * 0.6) * parentSize.Y;

            float size = (4.5f + (float)random.NextDouble() * 2.5f) * ParticleScale(parentSize);

            // Sparkle is added directly to the emitter (no wrapper Container),
            // so its Position is interpreted relative to its Anchor. We want
            // (startX, startY) to be in parent.TopLeft coords (which is what
            // the formula above produces — startX ∈ [0..W], startY ∈ [0..H]).
            // Anchor defaults to TopLeft when omitted; explicitly setting it
            // to Centre here was a long-standing bug that biased every spark
            // / sparkle / dev bit to the right of the username, because
            // Centre + Position(W,H) lands at (1.5W, 1.5H). Origin stays
            // Centre so the rotate/scale animations pivot around the star's
            // own midpoint.
            var sparkle = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Star,
                Size = new Vector2(size),
                Colour = spark_bright,
                Alpha = 0,
                Position = new Vector2(startX, startY),
            };

            parent.Add(sparkle);

            // Pop in fast, briefly scale up + rotate, fade out. Total ~500ms.
            sparkle.RotateTo((float)((random.NextDouble() - 0.5) * 90));
            sparkle.FadeTo(0.95f, 90, Easing.OutQuad);
            sparkle.ScaleTo(1.6f, 250, Easing.OutQuad);
            sparkle.Delay(120).FadeOut(380, Easing.InCubic);
            sparkle.RotateTo(sparkle.Rotation + 60, 500, Easing.OutSine).Expire();
        }

        // Slow round ember that drifts higher and lingers — adds a softer
        // counter-beat to the fast spark columns.
        private void emitEmber(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(0.15 + random.NextDouble() * 0.7) * parentSize.X;
            float startY = parentSize.Y * (0.7f + (float)random.NextDouble() * 0.3f);

            float driftY = -parentSize.Y * (1.0f + (float)random.NextDouble() * 0.3f);
            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.18f);

            float size = (4f + (float)random.NextDouble() * 2.5f) * ParticleScale(parentSize);

            var halo = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size * 1.8f),
                Colour = spark_red,
                Alpha = 0.3f,
            };

            var core = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size),
                Colour = spark_amber,
                Alpha = 0.8f,
            };

            var ember = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { halo, core },
                Alpha = 0,
            };

            parent.Add(ember);

            double lifetime = 1100 + random.NextDouble() * 400;
            ember.FadeTo(1f, 140, Easing.OutQuad);
            ember.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutSine);
            ember.ScaleTo(0.5f, lifetime, Easing.OutQuad);
            ember.Delay(lifetime - 220).FadeOut(220, Easing.InQuad).Expire();
        }
    }
}
