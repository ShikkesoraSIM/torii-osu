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
    /// Stardust: a deliberately LOW-KEY aura that anyone can buy with points
    /// (the only points-purchasable aura — every other aura is earned by a
    /// group/role). Tiny soft silver dust motes drift slowly upward and
    /// twinkle, with a faint cool-white glow on the letters. Tuned to read as
    /// "a little sparkle" rather than the loud, saturated elite auras, so a
    /// bought aura never looks more prestigious than an earned one.
    /// </summary>
    public class StardustAuraPreset : AuraPreset
    {
        public const string ID = "stardust";

        // Cool silver-white, two close tones so motes vary subtly.
        private static readonly Color4 dust_silver = new Color4(224, 230, 245, 255);
        private static readonly Color4 dust_pale = new Color4(200, 212, 240, 255);
        private static readonly Color4 glow_cool = new Color4(196, 210, 240, 255);

        public override string AuraId => ID;

        // No owning group: this aura is NOT granted by any role. It is only
        // ever obtained by purchase (client ownership) + explicit equip, so it
        // never appears in the group-fallback resolver or the entitled list.
        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = Array.Empty<string>();

        // Never auto-wins a group fallback (it has no groups anyway). High
        // value keeps it last if it ever ends up in a priority comparison.
        public override int DefaultPriority => 250;

        // Sparse + slow: lowkey ambience, not a particle storm.
        public override double SpawnIntervalMs => 360;
        public override double SpawnJitterMs => 240;
        public override int MaxAlive => 8;

        // Faint cool glow hugging the letters. Much softer than the elite auras.
        public override Color4? GlowColour => glow_cool;

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            // Spawn anywhere across the name, biased to the lower half so motes
            // have room to drift up through the text.
            float startX = (float)(random.NextDouble()) * parentSize.X;
            float startY = (float)(0.35 + random.NextDouble() * 0.6) * parentSize.Y;

            // Mostly-vertical slow rise with a touch of sideways wander.
            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.18f);
            float driftY = -(float)(0.35 + random.NextDouble() * 0.45) * parentSize.Y;

            float size = (1.6f + (float)random.NextDouble() * 1.8f) * ParticleScale(parentSize);
            Color4 colour = random.NextDouble() < 0.5 ? dust_silver : dust_pale;

            var mote = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size),
                Colour = colour,
                Position = new Vector2(startX, startY),
                Alpha = 0,
                // Additive so overlapping motes read as soft light, not paint.
                Blending = BlendingParameters.Additive,
            };

            parent.Add(mote);

            double lifetime = 1500 + random.NextDouble() * 700;

            // Gentle rise.
            mote.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutSine);

            // Twinkle: fade up to a soft peak, shimmer, then fade out as it rises.
            float peak = 0.4f + (float)random.NextDouble() * 0.25f;
            mote.FadeTo(peak, 320, Easing.OutQuad)
                .Then().FadeTo(peak * 0.5f, 360, Easing.InOutSine)
                .Then().FadeTo(peak, 360, Easing.InOutSine)
                .Then().FadeOut((float)lifetime * 0.4f, Easing.InQuad);

            mote.ScaleTo(0.7f).ScaleTo(1f, 420, Easing.OutQuad);

            mote.Delay(lifetime).Expire();
        }
    }
}
