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

            if (roll < 0.50)
                emitSparkle(parent, parentSize, random);
            else if (roll < 0.78)
                emitDroplet(parent, parentSize, random);
            else if (roll < 0.92)
                emitSun(parent, parentSize, random, big: false);
            else
                emitSun(parent, parentSize, random, big: true);
        }

        // Warm gold mote that shimmers upward like heat haze.
        private void emitSparkle(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(0.02 + random.NextDouble() * 0.96) * parentSize.X;
            float startY = parentSize.Y * (0.55f + (float)random.NextDouble() * 0.45f);

            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.25f);
            float driftY = -parentSize.Y * (0.9f + (float)random.NextDouble() * 0.5f);

            float scale = ParticleScale(parentSize);
            float size = (2.4f + (float)random.NextDouble() * 1.8f) * scale;

            Color4 colour = random.NextDouble() < 0.6 ? sun_gold : sun_amber;

            var halo = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size * 2.6f),
                Colour = colour,
                Alpha = 0.18f,
            };

            var core = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size),
                Colour = colour,
                Alpha = 0.95f,
            };

            var mote = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { halo, core },
                Alpha = 0,
            };

            parent.Add(mote);

            double lifetime = 1500 + random.NextDouble() * 600;
            mote.FadeTo(1f, 220, Easing.OutQuad);
            mote.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutSine);
            // Twinkle.
            core.ScaleTo(1.3f, 420, Easing.OutQuad).Then().ScaleTo(0.85f, 420, Easing.InQuad).Loop();
            mote.Delay(lifetime - 320).FadeOut(320, Easing.InQuad).Expire();
        }

        // Cool water droplet — the one falling element, balancing the rising
        // gold so the aura reads as "sun over water".
        private void emitDroplet(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.05 + random.NextDouble() * 1.10) * parentSize.X;
            float startY = -parentSize.Y * (0.15f + (float)random.NextDouble() * 0.25f);

            float endX = startX + (float)((random.NextDouble() - 0.5) * parentSize.X * 0.20f);
            float endY = parentSize.Y * (1.05f + (float)random.NextDouble() * 0.35f);

            float scale = ParticleScale(parentSize);
            float w = (1.8f + (float)random.NextDouble() * 0.9f) * scale;
            float h = w * (1.5f + (float)random.NextDouble() * 0.4f);

            Color4 colour = random.NextDouble() < 0.6 ? sky_cyan : aqua;

            var droplet = new Circle
            {
                Origin = Anchor.Centre,
                Position = new Vector2(startX, startY),
                Size = new Vector2(w, h),
                Colour = colour,
                Alpha = 0,
            };

            parent.Add(droplet);

            double lifetime = 1300 + random.NextDouble() * 500;
            droplet.FadeTo(0.9f, 200, Easing.OutQuad);
            droplet.MoveTo(new Vector2(endX, endY), lifetime, Easing.InQuad);
            droplet.Delay(lifetime - 260).FadeOut(260, Easing.InQuad).Expire();
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
