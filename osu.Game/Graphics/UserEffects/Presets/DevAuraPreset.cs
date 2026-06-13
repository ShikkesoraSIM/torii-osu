// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserEffects.Presets
{
    /// <summary>
    /// Dev aura: rising "data bits" — small cyan squares with the occasional
    /// angle-bracket glyph — that look like syntax tokens floating up. Reads
    /// as code/build/tech without copying the admin's heat motif. Tighter and
    /// more orderly than admin: dev = precise, admin = chaotic.
    /// </summary>
    public class DevAuraPreset : AuraPreset
    {
        public const string ID = "dev-bits";

        // Cool palette: electric cyan + light blue. Three nearby tones so a
        // burst of bits doesn't read as monochrome.
        private static readonly Color4 bit_cyan   = new Color4(120, 220, 255, 255);
        private static readonly Color4 bit_pale   = new Color4(180, 240, 255, 255);
        private static readonly Color4 bit_deep   = new Color4(80,  170, 230, 255);

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-dev" };

        public override int DefaultPriority => 10;

        // Faster + more particles than the v1 cadence — dev should feel
        // "actively coding" not "occasionally bleeping". With four particle
        // types in the rotation now (bits, brackets, digits, operators),
        // bumping MaxAlive 8→12 gives the visual room for the variety to
        // actually register without each glyph type starving the others.
        public override double SpawnIntervalMs => 170;
        public override double SpawnJitterMs => 110;
        public override int MaxAlive => 12;

        // Soft cyan glow hugging the username letters — replaces the old
        // rectangular PulsingHalo. Dev's "calm focus" vibe vs admin's heat.
        public override Color4? GlowColour => bit_cyan;

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            // Distribution tuned for "developer-flavoured" mix: small data
            // bits dominate (rising columns of pixels), with periodic syntax
            // tokens and the rare numeric digit / operator glyph for
            // variety. Probabilities sum to 1.0.
            double roll = random.NextDouble();
            if (roll < 0.55)
                emitBit(parent, parentSize, random);
            else if (roll < 0.80)
                emitBracket(parent, parentSize, random);
            else if (roll < 0.93)
                emitDigit(parent, parentSize, random);
            else
                emitOperator(parent, parentSize, random);
        }

        // ---------------------------------------------------------------
        // Anchor-bug note for everything below:
        //
        // Particles are added DIRECTLY to the emitter (no wrapper Container).
        // Earlier revisions used `Anchor = Anchor.Centre` on these particles
        // along with `Position = (startX, startY)` where `startX ∈ [0..W]`.
        // That combo is wrong: with Anchor.Centre, Position is interpreted as
        // an offset FROM PARENT CENTRE, so startX = 0.5*W ends up rendering
        // at parentW/2 + 0.5*W = right edge, and startX = 0.95*W ends up at
        // 1.45*W (off-screen right). Result: the entire dev aura visibly
        // drifted to the right of the username.
        //
        // Fix: keep `Origin = Anchor.Centre` (so rotate/scale pivot stays the
        // particle's centre), but DROP the explicit `Anchor = Anchor.Centre`
        // — Anchor defaults to TopLeft, which is what the startX/startY
        // formulas were always written for.
        // ---------------------------------------------------------------

        private void emitBit(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(0.05 + random.NextDouble() * 0.9) * parentSize.X;
            float startY = parentSize.Y * (0.6f + (float)random.NextDouble() * 0.35f);

            // Mostly straight up. Smaller lateral drift than admin embers
            // because dev "data bits" should look orderly / column-aligned.
            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.08f);
            float driftY = -parentSize.Y * (0.6f + (float)random.NextDouble() * 0.3f);

            // Scale by parent height so chat-density usernames don't get the
            // same ~3.5px bits a 32pt profile header gets.
            float scale = ParticleScale(parentSize);
            float size = (2.2f + (float)random.NextDouble() * 1.5f) * scale;

            Color4 colour = random.NextDouble() < 0.5
                ? bit_cyan
                : (random.NextDouble() < 0.5 ? bit_pale : bit_deep);

            var bit = new Box
            {
                Origin = Anchor.Centre,
                Size = new Vector2(size),
                Colour = colour,
                Alpha = 0,
                Position = new Vector2(startX, startY),
            };

            parent.Add(bit);

            double lifetime = 800 + random.NextDouble() * 400;
            bit.FadeTo(0.95f, 100, Easing.OutQuad);
            bit.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutCubic);
            bit.RotateTo(45f, lifetime, Easing.OutSine); // diamond as it rises
            bit.Delay(lifetime - 200).FadeOut(200, Easing.InQuad).Expire();
        }

        private void emitBracket(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(0.1 + random.NextDouble() * 0.8) * parentSize.X;
            float startY = parentSize.Y * (0.55f + (float)random.NextDouble() * 0.35f);

            float driftY = -parentSize.Y * (0.7f + (float)random.NextDouble() * 0.2f);
            float size = (4.5f + (float)random.NextDouble() * 2f) * ParticleScale(parentSize);

            // Pick < or > based on a coin flip — gives the impression of
            // syntax tokens rising rather than identical glyphs.
            var bracket = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = random.NextDouble() < 0.5 ? FontAwesome.Solid.LessThan : FontAwesome.Solid.GreaterThan,
                Size = new Vector2(size),
                Colour = bit_pale,
                Alpha = 0,
                Position = new Vector2(startX, startY),
            };

            parent.Add(bracket);

            double lifetime = 900 + random.NextDouble() * 300;
            bracket.FadeTo(0.85f, 120, Easing.OutQuad);
            bracket.MoveTo(new Vector2(startX, startY + driftY), lifetime, Easing.OutSine);
            bracket.ScaleTo(0.6f, lifetime, Easing.OutQuad);
            bracket.Delay(lifetime - 220).FadeOut(220, Easing.InQuad).Expire();
        }

        // Tiny binary digit — picks 0 or 1, rises like a bit but with text
        // legibility. Sells the "machine code" half of the dev motif: actual
        // numbers floating up rather than abstract pixels.
        private void emitDigit(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(0.08 + random.NextDouble() * 0.84) * parentSize.X;
            float startY = parentSize.Y * (0.55f + (float)random.NextDouble() * 0.35f);

            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.06f);
            float driftY = -parentSize.Y * (0.65f + (float)random.NextDouble() * 0.3f);

            float size = (8f + (float)random.NextDouble() * 2.5f) * ParticleScale(parentSize);

            string glyph = random.NextDouble() < 0.5 ? "0" : "1";
            Color4 colour = random.NextDouble() < 0.5 ? bit_cyan : bit_pale;

            // OsuSpriteText (the project-mandated subclass — raw SpriteText is
            // banned by analyzer). The Bold weight + monospace-leaning condensed
            // family keeps the digit readable at the small size we render.
            var digit = new OsuSpriteText
            {
                Origin = Anchor.Centre,
                Text = glyph,
                Font = OsuFont.GetFont(size: size, weight: FontWeight.Bold),
                Colour = colour,
                Alpha = 0,
                Position = new Vector2(startX, startY),
            };

            parent.Add(digit);

            double lifetime = 950 + random.NextDouble() * 350;
            digit.FadeTo(0.9f, 120, Easing.OutQuad);
            digit.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutCubic);
            digit.ScaleTo(0.7f, lifetime, Easing.OutQuad);
            digit.Delay(lifetime - 240).FadeOut(240, Easing.InQuad).Expire();
        }

        // Operator glyph — slash, asterisk, equals, plus. Rotates briefly as
        // it rises, mimicking a code-token feel. Rarer than digits + brackets;
        // they're the "spice" of the variety mix.
        private void emitOperator(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(0.1 + random.NextDouble() * 0.8) * parentSize.X;
            float startY = parentSize.Y * (0.5f + (float)random.NextDouble() * 0.4f);

            float driftY = -parentSize.Y * (0.75f + (float)random.NextDouble() * 0.25f);
            float size = (4f + (float)random.NextDouble() * 2f) * ParticleScale(parentSize);

            // Pool of operator-y FontAwesome glyphs that read as "code" at small size.
            IconUsage[] operators =
            {
                FontAwesome.Solid.Slash,
                FontAwesome.Solid.Asterisk,
                FontAwesome.Solid.Equals,
                FontAwesome.Solid.Plus,
            };

            var op = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = operators[random.Next(operators.Length)],
                Size = new Vector2(size),
                Colour = bit_deep,
                Alpha = 0,
                Position = new Vector2(startX, startY),
                Rotation = (float)((random.NextDouble() - 0.5) * 30),
            };

            parent.Add(op);

            double lifetime = 1000 + random.NextDouble() * 350;
            float endRotation = op.Rotation + (float)((random.NextDouble() - 0.5) * 60);

            op.FadeTo(0.8f, 130, Easing.OutQuad);
            op.MoveTo(new Vector2(startX, startY + driftY), lifetime, Easing.OutSine);
            op.RotateTo(endRotation, lifetime, Easing.OutSine);
            op.ScaleTo(0.55f, lifetime, Easing.OutQuad);
            op.Delay(lifetime - 240).FadeOut(240, Easing.InQuad).Expire();
        }
    }
}
