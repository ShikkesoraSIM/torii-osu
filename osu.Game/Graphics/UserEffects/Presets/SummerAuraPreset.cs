// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserEffects.Presets
{
    /// <summary>
    /// Summer 2026 seasonal aura: warm sun motes shimmering up, the odd cool
    /// water droplet, and a rare little sun flare. A bright, holiday mood that
    /// stays readable behind a username.
    ///
    /// EARNED, not bought — granted server-side via the "torii-summer-2026"
    /// group to people who took part in the summer event. Like every aura it's
    /// never purchasable with points; the cosmetics store is cursor trails and
    /// other non-aura items. High DefaultPriority so it only renders when a
    /// user explicitly equips it (it doesn't auto-win the group fallback).
    /// </summary>
    public class SummerAuraPreset : AuraPreset
    {
        public const string ID = "summer-2026";

        private static readonly Color4 sun_gold = new Color4(255, 210, 90, 255);
        private static readonly Color4 sun_amber = new Color4(255, 170, 70, 255);
        private static readonly Color4 sky_cyan = new Color4(130, 215, 255, 255);
        private static readonly Color4 aqua = new Color4(90, 195, 240, 255);
        private static readonly Color4 halo_warm = new Color4(255, 200, 120, 255);
        private static readonly Color4 sand_gold = new Color4(255, 216, 132, 255);
        private static readonly Color4 sand_tan = new Color4(240, 198, 150, 255);

        // Classic beach-ball stripes.
        private static readonly Color4[] ball_stripes =
        {
            new Color4(255, 95, 95, 255),   // red
            new Color4(255, 220, 95, 255),  // yellow
            new Color4(245, 245, 245, 255), // white
            new Color4(95, 195, 240, 255),  // cyan
            new Color4(140, 220, 130, 255), // green
        };

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-summer-2026" };

        // Seasonal cosmetic — only render when explicitly equipped.
        public override int DefaultPriority => 150;

        public override double SpawnIntervalMs => 230;
        public override double SpawnJitterMs => 130;
        public override int MaxAlive => 10;

        // Warm golden atmosphere between particles.
        public override Color4? GlowColour => halo_warm;

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            double roll = random.NextDouble();

            if (roll < 0.44)
                emitSand(parent, parentSize, random);
            else if (roll < 0.68)
                emitBubble(parent, parentSize, random);
            else if (roll < 0.86)
                emitSun(parent, parentSize, random, big: random.NextDouble() < 0.25);
            else
                emitBeachBall(parent, parentSize, random);
        }

        // Warm golden sand grain that shimmers upward like heat haze. The
        // ambient base of the aura — small, plentiful, never busy.
        private void emitSand(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)random.NextDouble() * parentSize.X;
            float startY = parentSize.Y * (0.5f + (float)random.NextDouble() * 0.5f);

            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.25f);
            float driftY = -parentSize.Y * (0.8f + (float)random.NextDouble() * 0.5f);

            float scale = ParticleScale(parentSize);
            float size = (2f + (float)random.NextDouble() * 2f) * scale;
            Color4 colour = random.NextDouble() < 0.5 ? sand_gold : sand_tan;

            var halo = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size * 2.4f),
                Colour = colour,
                Alpha = 0.2f,
                Blending = BlendingParameters.Additive,
            };
            var core = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size),
                Colour = colour,
                Blending = BlendingParameters.Additive,
            };

            // Origin Centre, anchor top-left → Position spans the full width.
            var mote = new Container
            {
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { halo, core },
                Alpha = 0,
            };

            parent.Add(mote);

            double lifetime = 1400 + random.NextDouble() * 600;
            float peak = 0.6f + (float)random.NextDouble() * 0.3f;
            mote.FadeTo(peak, 220, Easing.OutQuad)
                .Then().FadeTo(peak * 0.55f, 300, Easing.InOutSine)
                .Then().FadeTo(peak, 300, Easing.InOutSine)
                .Then().FadeOut((float)lifetime * 0.3f, Easing.InQuad);
            mote.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutSine);
            mote.Delay(lifetime).Expire();
        }

        // Cool water bubble rising with a highlight glint — the "sea" half of
        // the sun-over-water mood.
        private void emitBubble(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)random.NextDouble() * parentSize.X;
            float startY = parentSize.Y * (0.7f + (float)random.NextDouble() * 0.45f);

            float sway = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.3f);
            float rise = -parentSize.Y * (0.5f + (float)random.NextDouble() * 0.6f);

            float scale = ParticleScale(parentSize);
            float size = (3f + (float)random.NextDouble() * 3f) * scale;
            Color4 colour = random.NextDouble() < 0.5 ? sky_cyan : aqua;

            var ring = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size),
                Colour = colour,
                Alpha = 0.5f,
                Blending = BlendingParameters.Additive,
            };
            var glint = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size * 0.4f),
                Position = new Vector2(-size * 0.18f, -size * 0.18f),
                Colour = Color4.White,
                Alpha = 0.85f,
                Blending = BlendingParameters.Additive,
            };

            var bubble = new Container
            {
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { ring, glint },
                Alpha = 0,
            };

            parent.Add(bubble);

            double lifetime = 1500 + random.NextDouble() * 600;
            bubble.FadeTo(0.85f, 240, Easing.OutQuad);
            bubble.MoveTo(new Vector2(startX + sway, startY + rise), lifetime, Easing.OutSine);
            bubble.Delay(lifetime - 300).FadeOut(300, Easing.InQuad).Expire();
        }

        // A striped beach ball that bobs up and slowly spins. The signature
        // "fun" element — rare so it stays a highlight, not clutter.
        private void emitBeachBall(Container parent, Vector2 parentSize, Random random)
        {
            float scale = ParticleScale(parentSize);
            float s = (10f + (float)random.NextDouble() * 6f) * scale;

            float startX = (float)(0.12 + random.NextDouble() * 0.76) * parentSize.X;
            float startY = parentSize.Y * (0.6f + (float)random.NextDouble() * 0.45f);
            float endX = startX + (float)((random.NextDouble() - 0.5) * parentSize.X * 0.3f);
            float endY = -parentSize.Y * (0.5f + (float)random.NextDouble() * 0.6f);

            float stripeW = s / 5f;
            var stripes = new List<Drawable>();
            for (int i = 0; i < 5; i++)
            {
                stripes.Add(new Box
                {
                    Position = new Vector2(i * stripeW, 0),
                    // Slight overlap (+0.6) so no seams show between stripes.
                    Size = new Vector2(stripeW + 0.6f, s),
                    Colour = ball_stripes[i % ball_stripes.Length],
                });
            }

            var ball = new CircularContainer
            {
                Origin = Anchor.Centre,
                Size = new Vector2(s),
                Masking = true,
                Position = new Vector2(startX, startY),
                Alpha = 0,
                Scale = new Vector2(0.6f),
                Rotation = (float)(random.NextDouble() * 40 - 20),
                Children = stripes.ToArray(),
            };

            parent.Add(ball);

            double lifetime = 1500 + random.NextDouble() * 700;
            ball.FadeTo(0.95f, 240, Easing.OutQuad);
            ball.ScaleTo(1f, 440, Easing.OutBack);
            ball.MoveTo(new Vector2(endX, endY), lifetime, Easing.OutSine);
            ball.RotateTo(ball.Rotation + (random.NextDouble() < 0.5 ? 55 : -55), lifetime, Easing.InOutSine);
            ball.Delay(lifetime - 320).FadeOut(320, Easing.InQuad).Expire();
        }

        // A little sun glyph that fades in, spins, and pops out. The rare "big"
        // variant gets a halo and a scale-up flare for an occasional highlight.
        private void emitSun(Container parent, Vector2 parentSize, Random random, bool big)
        {
            float centreX = parentSize.X * (0.5f + (float)(random.NextDouble() - 0.5) * (big ? 0.3f : 0.9f));
            float centreY = big
                ? -parentSize.Y * (0.20f + (float)random.NextDouble() * 0.25f)
                : parentSize.Y * (0.2f + (float)random.NextDouble() * 0.6f);

            float scale = ParticleScale(parentSize);
            float size = (big ? 16f + (float)random.NextDouble() * 5f : 7f + (float)random.NextDouble() * 3f) * scale;

            var children = new List<Drawable>();

            if (big)
            {
                children.Add(new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = FontAwesome.Solid.Sun,
                    Size = new Vector2(size * 1.5f),
                    Colour = sun_amber,
                    Alpha = 0.22f,
                });
            }

            children.Add(new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Sun,
                Size = new Vector2(size),
                Colour = sun_gold,
                Alpha = 0.95f,
            });

            var sun = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(centreX, centreY),
                Children = children.ToArray(),
                Alpha = 0,
                Scale = new Vector2(big ? 0.5f : 0.8f),
                Rotation = (float)(random.NextDouble() * 40 - 20),
            };

            parent.Add(sun);

            double lifetime = big ? 1300 : 1000;
            sun.FadeTo(big ? 0.95f : 0.85f, 260, Easing.OutQuad);
            sun.ScaleTo(1f, big ? 640 : 420, Easing.OutBack);
            sun.RotateTo(sun.Rotation + (big ? 25 : 60), lifetime, Easing.InOutSine);
            sun.Delay(lifetime - 340).FadeOut(340, Easing.InQuad).Expire();
        }
    }
}
