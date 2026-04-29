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
    /// QAT (Quality Assurance Team / Beatmap Nominators) aura: small music
    /// notes drifting around the name with the occasional checkmark popping
    /// in. Reads as "this person evaluates beatmaps" — the music note is the
    /// cleanest, least-bullshit symbol for that role.
    /// </summary>
    public class QatAuraPreset : AuraPreset
    {
        public const string ID = "qat-notes";

        // Cool green/teal palette — distinct from goof's pastel green.
        // QAT's green should read as "approval / verified" rather than cute.
        private static readonly Color4 note_teal  = new Color4(80, 220, 200, 255);
        private static readonly Color4 note_green = new Color4(120, 230, 150, 255);
        private static readonly Color4 halo_teal  = new Color4(90, 220, 190, 255);

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-qat" };

        public override int DefaultPriority => 30;

        public override double SpawnIntervalMs => 460;
        public override double SpawnJitterMs => 200;
        public override int MaxAlive => 6;

        // Soft teal glow hugging the username letters — replaces the old
        // rectangular PulsingHalo. Reads as "approval / verified" rather
        // than goof's playful pastel green.
        public override Color4? GlowColour => halo_teal;

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            // 85% music note, 15% checkmark — checkmarks are the rare "approved"
            // accent that ties the aura to the QAT role specifically.
            if (random.NextDouble() < 0.85)
                emitNote(parent, parentSize, random);
            else
                emitCheck(parent, parentSize, random);
        }

        private void emitNote(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(0.05 + random.NextDouble() * 0.9) * parentSize.X;
            float startY = (float)(0.1 + random.NextDouble() * 0.8) * parentSize.Y;

            // Notes drift mostly upward and to one side — like sheet music
            // floating off the page.
            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.3f);
            float driftY = -parentSize.Y * (0.4f + (float)random.NextDouble() * 0.4f);

            float size = (5.5f + (float)random.NextDouble() * 2.5f) * ParticleScale(parentSize);
            Color4 colour = random.NextDouble() < 0.5 ? note_teal : note_green;

            var halo = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Music,
                Size = new Vector2(size * 1.4f),
                Colour = colour,
                Alpha = 0.18f,
            };

            var note = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Music,
                Size = new Vector2(size),
                Colour = colour,
                Alpha = 0.85f,
                Rotation = (float)((random.NextDouble() - 0.5) * 30),
            };

            var particle = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { halo, note },
                Alpha = 0,
            };

            parent.Add(particle);

            double lifetime = 1300 + random.NextDouble() * 500;
            particle.FadeTo(1f, 200, Easing.OutQuad);
            particle.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutSine);
            note.RotateTo(note.Rotation + (float)((random.NextDouble() - 0.5) * 40), lifetime, Easing.InOutSine);
            particle.Delay(lifetime - 320).FadeOut(320, Easing.OutQuad).Expire();
        }

        private void emitCheck(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(0.15 + random.NextDouble() * 0.7) * parentSize.X;
            float startY = (float)(0.2 + random.NextDouble() * 0.6) * parentSize.Y;

            float size = (5f + (float)random.NextDouble() * 2f) * ParticleScale(parentSize);

            var check = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Check,
                Size = new Vector2(size),
                Colour = note_green,
                Alpha = 0,
                Position = new Vector2(startX, startY),
            };

            parent.Add(check);

            // Pop in, scale up briefly, fade out — quick approval flash.
            check.FadeTo(0.95f, 100, Easing.OutQuad);
            check.ScaleTo(1.4f, 320, Easing.OutBack);
            check.Delay(180).FadeOut(380, Easing.InCubic).Expire();
        }
    }
}
