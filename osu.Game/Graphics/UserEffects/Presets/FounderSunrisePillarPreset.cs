// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserEffects.Presets
{
    /// <summary>
    /// Founder variant — "Sunrise Pillar". Dawn-at-the-shrine palette:
    /// warm gold + dawn-orange + pale-pink, blended so the aura reads
    /// as gilded sunlight passing through a torii at sunrise. The
    /// signature element is the VERTICAL GOLD RAY particle — slow
    /// rising thin columns of light that emulate the "tyndall beam"
    /// effect of dawn light cutting through morning mist.
    ///
    /// Identity signature: gradient sakura (pale-pink outer + gold
    /// inner — like a petal lit from behind by sunrise), plus the
    /// vertical gold rays that no other variant has, plus a
    /// gold-rimmed seal with a pink-to-gold gradient torii inside.
    /// </summary>
    public class FounderSunrisePillarPreset : AuraPreset
    {
        public const string ID = "founder-variant-sunrise-pillar";

        // Dawn palette — warm gold core with pink + orange wings.
        // Specifically picked to feel like the first 15 minutes of
        // sunrise rather than midday gold.
        private static readonly Color4 dawn_gold_bright = new Color4(255, 220, 140, 255);
        private static readonly Color4 dawn_gold_warm   = new Color4(255, 188, 100, 255);
        private static readonly Color4 dawn_orange      = new Color4(255, 158, 110, 255);
        private static readonly Color4 dawn_pink        = new Color4(255, 178, 188, 255);
        private static readonly Color4 dawn_cream       = new Color4(255, 240, 210, 255);

        // Halo glow — warm gold-pink middle ground so the username
        // glows like it's catching the first light.
        private static readonly Color4 halo_dawn = new Color4(255, 192, 158, 255);

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-founder" };

        // Variants are flagged with a very high priority so they never
        // win the prod group-fallback — only render when explicitly
        // equipped by AuraId (used by test personas).
        public override int DefaultPriority => 200;

        public override double SpawnIntervalMs => 240;
        public override double SpawnJitterMs => 140;
        public override int MaxAlive => 10;

        public override Color4? GlowColour => halo_dawn;

        // Inline ornaments — dawn-gradient seals flanking the username.
        // See AuraPreset.CreateLeadingOrnament docs for layout semantics.
        public override Drawable? CreateLeadingOrnament() => new SunriseSeal
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
        };

        public override Drawable? CreateTrailingOrnament() => new SunriseSeal
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
        };

        // Fallback for constrained wrappers (RelativeSizeAxes=X) where
        // the inline flow can't be used — see Imperial Gold variant's
        // CreateBackground for the full rationale.
        public override Drawable? CreateBackground() =>
            new Container
            {
                Children = new Drawable[]
                {
                    new SunriseSeal { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreRight, X = -2 },
                    new SunriseSeal { Anchor = Anchor.CentreRight, Origin = Anchor.CentreLeft, X = 2 },
                },
            };

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            double roll = random.NextDouble();
            // Mix:
            //   40% gradient sakura  — the pink/gold blossom signature
            //   25% vertical sun ray — unique to this variant, the dawn beam
            //   15% rising gold mote — warmth carrier
            //   10% orange spark     — punctuation, the "ember of sun"
            //   10% rare gold torii  — signature moment
            if (roll < 0.40)
                emitGradientSakura(parent, parentSize, random);
            else if (roll < 0.65)
                emitSunRay(parent, parentSize, random);
            else if (roll < 0.80)
                emitWarmMote(parent, parentSize, random);
            else if (roll < 0.90)
                emitOrangeSpark(parent, parentSize, random);
            else
                emitGoldTorii(parent, parentSize, random);
        }

        // 5-petal blossom with a gradient: pink/orange outer (cool
        // side of dawn) + gold inner (the sunlit centre). Reads as
        // "petal backlit by sunrise" — different mood from any of
        // the other variants' blossoms.
        private void emitGradientSakura(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.18 + random.NextDouble() * 1.36) * parentSize.X;
            float startY = -parentSize.Y * (0.18f + (float)random.NextDouble() * 0.28f);

            float endX = startX + (float)((random.NextDouble() - 0.5) * parentSize.X * 0.55f);
            float endY = parentSize.Y * (1.10f + (float)random.NextDouble() * 0.35f);

            float scale = ParticleScale(parentSize);
            float petalSize = (3.0f + (float)random.NextDouble() * 1.8f) * scale;

            // Outer petal: cool dawn side (pink or orange).
            Color4 outerColour = random.NextDouble() < 0.55 ? dawn_pink : dawn_orange;
            // Inner petal: warm sunlit side (gold).
            Color4 innerColour = random.NextDouble() < 0.55 ? dawn_gold_bright : dawn_gold_warm;

            var blossom = new SunriseBlossom(petalSize, outerColour, innerColour)
            {
                Origin = Anchor.Centre,
                Position = new Vector2(startX, startY),
                Alpha = 0,
                Rotation = (float)(random.NextDouble() * 360),
            };

            parent.Add(blossom);

            double lifetime = 2400 + random.NextDouble() * 800;
            float rotationDelta = (float)((random.NextDouble() - 0.5) * 200);

            blossom.FadeTo(0.95f, 380, Easing.OutQuad);
            blossom.MoveTo(new Vector2(endX, endY), lifetime, Easing.InOutSine);
            blossom.RotateTo(blossom.Rotation + rotationDelta, lifetime, Easing.InOutSine);
            blossom.Delay(lifetime - 460).FadeOut(460, Easing.InQuad).Expire();
        }

        // SIGNATURE. Vertical "sun ray" — a thin tall column of warm
        // gold that fades in from the bottom, holds briefly, fades
        // out from the top. Like a beam of dawn light cutting
        // through morning mist. Unique to this variant.
        private void emitSunRay(Container parent, Vector2 parentSize, Random random)
        {
            float scale = ParticleScale(parentSize);
            // Tall, thin. Width is fixed-ish; height spans most of the
            // bounding box height plus some.
            float width  = (1.3f + (float)random.NextDouble() * 0.6f) * scale;
            float height = parentSize.Y * (1.4f + (float)random.NextDouble() * 0.4f);

            float centerX = (float)(-0.05 + random.NextDouble() * 1.10) * parentSize.X;
            float centerY = parentSize.Y * 0.5f;

            // Two-tone ray: bright cream core + warm gold "soft edge"
            // simulated via a wider, dimmer ray behind the bright one.
            var halo = new Box
            {
                Origin = Anchor.Centre,
                Position = new Vector2(centerX, centerY),
                Width = width * 2.4f,
                Height = height,
                Colour = dawn_gold_warm,
                Alpha = 0.20f,
            };

            var core = new Box
            {
                Origin = Anchor.Centre,
                Position = new Vector2(centerX, centerY),
                Width = width,
                Height = height,
                Colour = dawn_cream,
                Alpha = 0,
            };

            halo.Alpha = 0;
            parent.Add(halo);
            parent.Add(core);

            // Beam visibly "passes through" — fade in, hold, fade out.
            double inTime = 380;
            double holdTime = 480;
            double outTime = 560;

            halo.FadeTo(0.35f, inTime, Easing.OutQuad);
            core.FadeTo(0.85f, inTime, Easing.OutQuad);
            halo.Delay(inTime + holdTime).FadeOut(outTime, Easing.InQuad).Expire();
            core.Delay(inTime + holdTime).FadeOut(outTime, Easing.InQuad).Expire();
        }

        // Rising warm-gold mote. Steady "heat present" beat.
        private void emitWarmMote(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.05 + random.NextDouble() * 1.10) * parentSize.X;
            float startY = parentSize.Y * (0.85f + (float)random.NextDouble() * 0.30f);

            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.20f);
            float driftY = -parentSize.Y * (1.25f + (float)random.NextDouble() * 0.45f);

            float scale = ParticleScale(parentSize);
            float size = (3.0f + (float)random.NextDouble() * 1.8f) * scale;

            Color4 coreColour = random.NextDouble() < 0.6 ? dawn_gold_bright : dawn_gold_warm;

            var halo = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size * 2.4f),
                Colour = coreColour,
                Alpha = 0.25f,
            };

            var core = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size),
                Colour = dawn_cream,
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

            double lifetime = 1500 + random.NextDouble() * 500;
            mote.FadeTo(1f, 240, Easing.OutQuad);
            mote.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutSine);
            mote.ScaleTo(0.55f, lifetime, Easing.OutQuad);
            mote.Delay(lifetime - 280).FadeOut(280, Easing.InQuad).Expire();
        }

        // Orange spark — short tapered line in dawn orange. The
        // warmth-spike beat, distinct from the cooler sun rays.
        private void emitOrangeSpark(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.05 + random.NextDouble() * 1.10) * parentSize.X;
            float startY = parentSize.Y * (0.7f + (float)random.NextDouble() * 0.25f);

            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.10f);
            float driftY = -parentSize.Y * (0.6f + (float)random.NextDouble() * 0.40f);

            float scale = ParticleScale(parentSize);
            float length = (4.5f + (float)random.NextDouble() * 3f) * scale;

            var head = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.BottomCentre,
                Width = 1.4f,
                Height = length * 0.35f,
                Colour = dawn_cream,
                Alpha = 1f,
            };

            var tail = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.TopCentre,
                Width = 1.0f,
                Height = length * 0.65f,
                Colour = dawn_orange,
                Alpha = 0.55f,
            };

            var spark = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { tail, head },
                Alpha = 0,
            };

            parent.Add(spark);

            double lifetime = 460 + random.NextDouble() * 260;
            spark.FadeTo(1f, 60, Easing.OutQuad);
            spark.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutCubic);
            spark.ScaleTo(0.55f, lifetime, Easing.OutQuad);
            spark.Delay(lifetime - 140).FadeOut(140, Easing.InQuad).Expire();
        }

        // Rare torii flash. Gradient idea: outer halo in dawn pink,
        // gate body in bright gold — the same dawn-backlit composition
        // as the gradient sakura, applied to the signature glyph.
        private void emitGoldTorii(Container parent, Vector2 parentSize, Random random)
        {
            float positionRoll = (float)random.NextDouble();
            float centerX, aboveY;
            if (positionRoll < 0.55f)
            {
                centerX = parentSize.X * (0.5f + (float)(random.NextDouble() - 0.5) * 0.14f);
                aboveY  = -parentSize.Y * (0.18f + (float)random.NextDouble() * 0.28f);
            }
            else if (positionRoll < 0.78f)
            {
                centerX = parentSize.X * (-0.18f + (float)random.NextDouble() * 0.10f);
                aboveY  = parentSize.Y * (0.30f + (float)random.NextDouble() * 0.30f);
            }
            else
            {
                centerX = parentSize.X * (1.08f + (float)random.NextDouble() * 0.10f);
                aboveY  = parentSize.Y * (0.30f + (float)random.NextDouble() * 0.30f);
            }

            float scale = ParticleScale(parentSize);
            float size = (15f + (float)random.NextDouble() * 6f) * scale;

            var halo = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.ToriiGate,
                Size = new Vector2(size * 1.55f),
                Colour = dawn_pink,
                Alpha = 0.26f,
            };

            var gate = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.ToriiGate,
                Size = new Vector2(size),
                Colour = dawn_gold_bright,
                Alpha = 0.95f,
            };

            var bundle = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(centerX, aboveY),
                Children = new Drawable[] { halo, gate },
                Alpha = 0,
                Scale = new Vector2(0.55f),
                Rotation = (float)((random.NextDouble() - 0.5) * 6),
            };

            parent.Add(bundle);

            bundle.FadeTo(0.95f, 300, Easing.OutQuad);
            bundle.ScaleTo(1f, 680, Easing.OutBack);
            bundle.Delay(740).FadeOut(780, Easing.InQuad).Expire();
        }

        /// <summary>Dawn-gradient seal — gold rim, pink-orange halo
        /// behind, gold torii inside.</summary>
        private partial class SunriseSeal : CompositeDrawable
        {
            public SunriseSeal()
            {
                AutoSizeAxes = Axes.Both;
                Alpha = 0;
                Blending = BlendingParameters.Additive;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                const float seal_size = 14f;

                InternalChildren = new Drawable[]
                {
                    // Dawn-pink soft outer glow — the "sky" behind.
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(seal_size * 1.55f),
                        Colour = dawn_pink,
                        Alpha = 0.22f,
                    },
                    // Warm orange mid-halo — the "horizon" tone.
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(seal_size * 1.18f),
                        Colour = dawn_orange,
                        Alpha = 0.32f,
                    },
                    // Bright gold rim.
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Regular.Circle,
                        Size = new Vector2(seal_size),
                        Colour = dawn_gold_bright,
                        Alpha = 1f,
                    },
                    // Gold torii at the centre — the "gate against the
                    // sunrise" image, miniaturised into a seal.
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Solid.ToriiGate,
                        Size = new Vector2(seal_size * 0.58f),
                        Colour = dawn_cream,
                        Alpha = 0.98f,
                    },
                };

                this.FadeTo(0.6f, 600, Easing.OutQuad);
                this.Delay(600).FadeTo(0.95f, 3600, Easing.InOutSine)
                               .Then().FadeTo(0.6f, 3600, Easing.InOutSine)
                               .Loop();
            }
        }

        /// <summary>5-petal blossom with a dawn gradient
        /// (cool outer, warm inner).</summary>
        private partial class SunriseBlossom : CompositeDrawable
        {
            public SunriseBlossom(float petalSize, Color4 outerColour, Color4 innerColour)
            {
                AutoSizeAxes = Axes.Both;

                const int petal_count = 5;
                float radius = petalSize * 0.55f;

                var children = new List<Drawable>(petal_count * 2 + 1);

                for (int i = 0; i < petal_count; i++)
                {
                    float angleDeg = i * (360f / petal_count);
                    double angleRad = angleDeg * Math.PI / 180.0;
                    float cx = (float)Math.Sin(angleRad) * radius;
                    float cy = -(float)Math.Cos(angleRad) * radius;

                    children.Add(new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Position = new Vector2(cx, cy),
                        Size = new Vector2(petalSize * 0.85f, petalSize * 1.30f),
                        Rotation = angleDeg,
                        Colour = outerColour,
                        Alpha = 0.92f,
                    });

                    children.Add(new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Position = new Vector2(cx, cy),
                        Size = new Vector2(petalSize * 0.45f, petalSize * 0.85f),
                        Rotation = angleDeg,
                        Colour = innerColour,
                        Alpha = 0.92f,
                    });
                }

                // Cream-bright pollen — the brightest point on the
                // blossom, like the sun catching the very centre of
                // the flower.
                children.Add(new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(petalSize * 0.55f),
                    Colour = dawn_cream,
                    Alpha = 1f,
                });

                InternalChildren = children.ToArray();
            }
        }
    }
}
