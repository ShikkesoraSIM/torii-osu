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
    /// Feature Architect aura, May 2026 Cohort: a "good idea" effect for
    /// community members whose feature requests landed between 4 May and
    /// 4 June 2026.
    ///
    /// First revision tried a "blueprint construction lines materialising
    /// around the username" approach, which read as random stray lines
    /// at chat-row sizes (the geometric particles needed more density +
    /// a bigger canvas than a 5-character username to read as
    /// architecture). Scrapped in favour of an instantly-legible
    /// metaphor: a small gold lightbulb pulses on above the username
    /// every few seconds, with cyan idea-sparkles dancing around it.
    /// Everyone reads "lightbulb = idea" without explanation, which is
    /// the right shorthand for "this person proposes features".
    ///
    /// Two particle types:
    /// <list type="bullet">
    /// <item>Gold sparkle (~80%, the steady beat). A FontAwesome star
    ///       popping briefly around the username — distributed across
    ///       the bounding box, never outside, so the aura always reads
    ///       as "decorating this name" rather than "stray pixels".</item>
    /// <item>Centred lightbulb flash (~20%, the signature beat). A
    ///       cyan-tinted lightbulb glyph blooming on above the username,
    ///       brightening + scaling up + fading off. The aura's identity
    ///       hook — the moment that says "ideas".</item>
    /// </list>
    /// </summary>
    public class FeatureArchitectAuraPreset : AuraPreset
    {
        public const string ID = "feature-architect-2026-06";

        // Warm gold for the sparkle steady beat — same family as the
        // Founder gold so the warm accents across the catalogue feel
        // intentional rather than each preset inventing its own
        // colour palette.
        private static readonly Color4 gold_spark = new Color4(255, 215, 130, 255);

        // Cool cyan for the lightbulb signature beat. Stays cool to
        // contrast the warm sparkles — together they read as "spark of
        // insight in a technical mind" rather than monochrome flash.
        private static readonly Color4 cyan_bulb  = new Color4(140, 220, 250, 255);

        // Halo glow leans cyan so the username reads as "designed /
        // engineered" rather than "burning". Picked desaturated enough
        // that the halo doesn't compete with the warm sparkles for
        // attention.
        private static readonly Color4 halo_cyan  = new Color4(150, 210, 230, 255);

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-feature-architect-2026-06" };

        // Sits between Supporter (40) and Founder (70) so an active
        // Supporter who is also an FA defaults to the supporter aura
        // (donating energy outranks one-off recognition); a Founder who
        // is also an FA defaults to the FA aura (cohort recognition is
        // more specific than the broad early-adopter founder badge).
        public override int DefaultPriority => 60;

        // Steady but not overwhelming. Gold sparkles need a moderate
        // cadence to read as "a presence" — too slow feels dead, too
        // fast turns into a glitter explosion that competes with the
        // username text.
        public override double SpawnIntervalMs => 360;
        public override double SpawnJitterMs => 220;
        public override int MaxAlive => 6;

        public override Color4? GlowColour => halo_cyan;

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            if (random.NextDouble() < 0.80)
                emitGoldSparkle(parent, parentSize, random);
            else
                emitLightbulb(parent, parentSize, random);
        }

        // Dominant particle. Small gold star that pops in, scales up,
        // rotates a tick, fades. Positioned strictly INSIDE the
        // bounding box (10..90% of each axis) so on a tight 5-character
        // username it doesn't escape the text frame and read as a
        // glitch — the issue with the previous blueprint-lines approach.
        private void emitGoldSparkle(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(0.10 + random.NextDouble() * 0.80) * parentSize.X;
            float startY = (float)(0.10 + random.NextDouble() * 0.80) * parentSize.Y;

            float scale = ParticleScale(parentSize);
            float size = (3.5f + (float)random.NextDouble() * 2.5f) * scale;

            var sparkle = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Position = new Vector2(startX, startY),
                Icon = FontAwesome.Solid.Star,
                Size = new Vector2(size),
                Colour = gold_spark,
                Alpha = 0,
                // Mild initial tilt + counter-rotation gives the sparkle
                // a sense of "twinkle" without becoming a noticeable
                // spin.
                Rotation = (float)((random.NextDouble() - 0.5) * 50),
            };

            parent.Add(sparkle);

            sparkle.FadeTo(0.9f, 110, Easing.OutQuad);
            sparkle.ScaleTo(1.45f, 280, Easing.OutQuad);
            sparkle.Delay(150).FadeOut(380, Easing.InCubic);
            sparkle.RotateTo(sparkle.Rotation + 35, 530, Easing.OutSine).Expire();
        }

        // Signature beat. Cyan lightbulb glyph above the username with
        // a faint halo. Materialise -> brief hold -> dissolve. Anchored
        // dead-centre horizontally (give or take a tiny jitter) and
        // ABOVE the text baseline so the "idea" sits over the name like
        // a thought bubble. Sparser than the sparkles because it's the
        // visual identity hook and would lose impact if spammed.
        private void emitLightbulb(Container parent, Vector2 parentSize, Random random)
        {
            float centerX = parentSize.X * (0.5f + (float)(random.NextDouble() - 0.5) * 0.10f);
            float aboveY  = parentSize.Y * (0.18f + (float)random.NextDouble() * 0.12f);

            float scale = ParticleScale(parentSize);
            float size = (8f + (float)random.NextDouble() * 3f) * scale;

            // Halo behind the bulb — same cyan, low alpha. Makes the
            // bulb pop without harsh edges, especially on darker chat
            // backgrounds.
            var halo = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Lightbulb,
                Size = new Vector2(size * 1.6f),
                Colour = cyan_bulb,
                Alpha = 0.22f,
            };

            var bulb = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Lightbulb,
                Size = new Vector2(size),
                Colour = cyan_bulb,
                Alpha = 0.95f,
            };

            var bundle = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(centerX, aboveY),
                Children = new Drawable[] { halo, bulb },
                Alpha = 0,
                Scale = new Vector2(0.6f),
            };

            parent.Add(bundle);

            // Bloom on — hold — dissolve. ~1.4s total. Peak alpha 1.0
            // because the lightbulb is the moment, not a background
            // texture; muting it would defeat the purpose.
            bundle.FadeTo(1f, 220, Easing.OutQuad);
            bundle.ScaleTo(1f, 320, Easing.OutBack);
            bundle.Delay(560).FadeOut(620, Easing.InQuad).Expire();
        }
    }
}
