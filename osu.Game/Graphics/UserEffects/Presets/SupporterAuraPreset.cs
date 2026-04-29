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
    /// Supporter aura: small pink hearts rising slowly with a gentle pulse.
    /// Mirrors the established osu!supporter heart symbol so it's instantly
    /// recognisable without explanation. Subdued cadence — a "thank you"
    /// feel rather than the loud attention-grabbing of admin/dev/qat.
    ///
    /// Granted ONLY while the user is currently supporting (server-side
    /// `torii-supporter` group is added iff `is_currently_supporting`).
    /// Lapsed donors lose access to this aura on their next API hit; the
    /// permanent recognition for them is the `torii-donator` badge, which
    /// has no aura attached.
    /// </summary>
    public class SupporterAuraPreset : AuraPreset
    {
        public const string ID = "supporter-aura";

        // Pinks tuned around the existing osu! supporter pink so the aura
        // reads as "this person supports the project" without needing a
        // label.
        private static readonly Color4[] PINK_PALETTE =
        {
            new Color4(255, 130, 200, 255), // pink
            new Color4(255, 165, 215, 255), // rose
            new Color4(230, 100, 175, 255), // deep pink
        };

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-supporter" };

        public override int DefaultPriority => 40;

        public override double SpawnIntervalMs => 540;
        public override double SpawnJitterMs => 240;
        public override int MaxAlive => 6;

        // Soft pink halo hugging the username letters. Same family as the
        // PINK_PALETTE base tone so the heart particles + glow read as a
        // single continuous "supporter pink" rather than two disconnected
        // colours.
        public override Color4? GlowColour => PINK_PALETTE[0];

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(0.08 + random.NextDouble() * 0.84) * parentSize.X;
            float startY = parentSize.Y * (0.55f + (float)random.NextDouble() * 0.4f);

            // Hearts rise mostly straight up with a tiny side-to-side wobble.
            // No drama — the supporter aura is a quiet thank-you, not a flex.
            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.18f);
            float driftY = -parentSize.Y * (0.7f + (float)random.NextDouble() * 0.3f);

            float size = (5f + (float)random.NextDouble() * 2.5f) * ParticleScale(parentSize);
            Color4 colour = PINK_PALETTE[random.Next(PINK_PALETTE.Length)];

            var halo = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Heart,
                Size = new Vector2(size * 1.5f),
                Colour = colour,
                Alpha = 0.18f,
            };

            var heart = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Heart,
                Size = new Vector2(size),
                Colour = colour,
                Alpha = 0.85f,
            };

            var particle = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { halo, heart },
                Alpha = 0,
            };

            parent.Add(particle);

            double lifetime = 1500 + random.NextDouble() * 500;
            particle.FadeTo(1f, 240, Easing.OutQuad);
            particle.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutSine);

            // Gentle "heartbeat" pulse on the inner heart only.
            heart.ScaleTo(1.15f, 700, Easing.OutQuad).Then().ScaleTo(1f, 700, Easing.InQuad).Loop();

            particle.Delay(lifetime - 340).FadeOut(340, Easing.OutQuad).Expire();
        }
    }
}
