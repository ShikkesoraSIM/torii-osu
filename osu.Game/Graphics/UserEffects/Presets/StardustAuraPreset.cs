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

        // Icy "stardust" palette: a bright white core with cool glints that
        // vary between icy cyan, soft white and a touch of violet, so the
        // sparkles read as galaxy glitter rather than flat grey dots.
        private static readonly Color4[] glint_palette =
        {
            new Color4(160, 220, 255, 255), // icy cyan
            new Color4(205, 225, 255, 255), // cool white
            new Color4(200, 175, 255, 255), // soft violet
            new Color4(255, 240, 210, 255), // faint warm star
        };

        private static readonly Color4 glow_cool = new Color4(165, 205, 255, 255);

        public override string AuraId => ID;

        // No owning group: this aura is NOT granted by any role. It is only
        // ever obtained by purchase (client ownership) + explicit equip, so it
        // never appears in the group-fallback resolver or the entitled list.
        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = Array.Empty<string>();

        // Never auto-wins a group fallback (it has no groups anyway). High
        // value keeps it last if it ever ends up in a priority comparison.
        public override int DefaultPriority => 250;

        // Lowkey ambience — present but never busy.
        public override double SpawnIntervalMs => 320;
        public override double SpawnJitterMs => 200;
        public override int MaxAlive => 7;

        // Cool glow hugging the letters.
        public override Color4? GlowColour => glow_cool;

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            // Spawn anywhere across the name, biased to the lower half so the
            // sparkles have room to drift up through the text.
            float startX = (float)random.NextDouble() * parentSize.X;
            float startY = (float)(0.3 + random.NextDouble() * 0.65) * parentSize.Y;

            // Mostly-vertical slow rise with a touch of sideways wander.
            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.22f);
            float driftY = -(float)(0.4 + random.NextDouble() * 0.5) * parentSize.Y;

            float size = (4f + (float)random.NextDouble() * 3f) * ParticleScale(parentSize);
            Color4 glint = glint_palette[random.Next(glint_palette.Length)];

            // 4-point sparkle: bright white core + two crossed glints. Additive
            // so overlaps read as light. Origin Centre (anchor stays top-left)
            // so Position is measured from the name's top-left and sparkles span
            // the FULL width, not just the right half.
            float arm = size * 0.12f;

            var sparkle = new Container
            {
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Alpha = 0,
                Scale = new Vector2(0f),
                Rotation = (float)(random.NextDouble() * 30 - 15),
                Children = new Drawable[]
                {
                    new Box
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(arm, size),
                        Colour = glint,
                        Blending = BlendingParameters.Additive,
                    },
                    new Box
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(size, arm),
                        Colour = glint,
                        Blending = BlendingParameters.Additive,
                    },
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(size * 0.42f),
                        Colour = Color4.White,
                        Blending = BlendingParameters.Additive,
                    },
                },
            };

            parent.Add(sparkle);

            double lifetime = 1400 + random.NextDouble() * 700;
            float peak = 0.85f + (float)random.NextDouble() * 0.15f;

            // Gentle rise.
            sparkle.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutSine);

            // Twinkle in, shimmer once, twinkle out — both scale + alpha so it
            // reads as a star catching light rather than a fading dot.
            sparkle.FadeTo(peak, 240, Easing.OutQuad)
                   .Then().FadeTo(peak * 0.55f, 300, Easing.InOutSine)
                   .Then().FadeTo(peak, 300, Easing.InOutSine)
                   .Then().FadeOut((float)lifetime * 0.32f, Easing.InQuad);

            sparkle.ScaleTo(1f, 260, Easing.OutBack)
                   .Then().ScaleTo(0.78f, 300, Easing.InOutSine)
                   .Then().ScaleTo(1f, 300, Easing.InOutSine)
                   .Then().ScaleTo(0.2f, (float)lifetime * 0.32f, Easing.InQuad);

            sparkle.Delay(lifetime).Expire();
        }
    }
}
