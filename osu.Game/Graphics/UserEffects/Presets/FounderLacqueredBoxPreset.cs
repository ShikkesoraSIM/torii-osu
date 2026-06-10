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
    /// Founder variant — "Lacquered Box". Inspired by traditional
    /// Japanese gold-on-black lacquerware (urushi + gilt accent). The
    /// aura runs on deep gold + bright gold against an onyx-tinted
    /// halo, so the gold particles look like inlaid metal flake
    /// rather than free sparks. Mature, refined, "treasured artifact"
    /// vibe.
    ///
    /// Identity signature: LARGER, more elaborate flanking crests
    /// (double-ringed coin seals with a torii inset) are the visual
    /// anchor. Particles are sparser than the other variants because
    /// the persistent crests carry the aura's weight — quality over
    /// quantity.
    /// </summary>
    public class FounderLacqueredBoxPreset : AuraPreset
    {
        public const string ID = "founder-variant-lacquered-box";

        // Gold palette — slightly cooler than Imperial's, leaning
        // toward "old gold leaf" rather than "bright new metal".
        private static readonly Color4 gold_bright = new Color4(255, 215, 130, 255);
        private static readonly Color4 gold_warm   = new Color4(220, 170, 80, 255);
        private static readonly Color4 gold_deep   = new Color4(170, 120, 50, 255);

        // Onyx accent — used inside the seals as the "lacquer base"
        // colour. Brings the gold into stark relief.
        private static readonly Color4 onyx_deep   = new Color4(28, 22, 18, 255);

        // Cream highlight — the "polish" on the gold edge.
        private static readonly Color4 cream       = new Color4(252, 240, 210, 255);

        // Halo glow leans deeper than the other variants — supports
        // the "lacquerware" mood without bleaching the username.
        private static readonly Color4 halo_gold   = new Color4(200, 160, 90, 255);

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-founder" };

        // Variants are flagged with a very high priority so they never
        // win the prod group-fallback — only render when explicitly
        // equipped by AuraId (used by test personas).
        public override int DefaultPriority => 200;

        // Sparser cadence — the persistent crests are the show, the
        // particles are decorative. Fewer simultaneous particles
        // means each one gets visual room.
        public override double SpawnIntervalMs => 340;
        public override double SpawnJitterMs => 200;
        public override int MaxAlive => 7;

        public override Color4? GlowColour => halo_gold;

        // Persistent flanking CRESTS (not just seals). Larger than
        // the other variants', with concentric rings + an inner
        // torii inlay. These ARE the aura's identity — the particles
        // are secondary. Rendered as inline ornaments so the wrapper
        // bounding box includes them.
        public override Drawable? CreateLeadingOrnament() => new LacquerCrest
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
        };

        public override Drawable? CreateTrailingOrnament() => new LacquerCrest
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
                    new LacquerCrest { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreRight, X = -3 },
                    new LacquerCrest { Anchor = Anchor.CentreRight, Origin = Anchor.CentreLeft, X = 3 },
                },
            };

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            double roll = random.NextDouble();
            // Particle mix:
            //   40% gold leaf drift   — the dominant slow particle
            //   25% gold sakura       — punctuation
            //   20% koi ripple        — gold expanding ring (lacquer signature)
            //   10% bright shimmer    — fast metallic specular
            //    5% gold torii flash  — sparse signature
            if (roll < 0.40)
                emitGoldLeaf(parent, parentSize, random);
            else if (roll < 0.65)
                emitGoldSakura(parent, parentSize, random);
            else if (roll < 0.85)
                emitKoiRipple(parent, parentSize, random);
            else if (roll < 0.95)
                emitShimmer(parent, parentSize, random);
            else
                emitGoldTorii(parent, parentSize, random);
        }

        // Slow drifting gold leaf — small FontAwesome leaf glyph in
        // warm gold, drifting laterally and downward like a leaf
        // falling through still air. The "lacquer flake" beat.
        private void emitGoldLeaf(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.18 + random.NextDouble() * 1.36) * parentSize.X;
            float startY = -parentSize.Y * (0.15f + (float)random.NextDouble() * 0.25f);

            // Pronounced lateral drift — leaves don't fall straight.
            float endX = startX + (float)((random.NextDouble() - 0.5) * parentSize.X * 0.80f);
            float endY = parentSize.Y * (1.10f + (float)random.NextDouble() * 0.35f);

            float scale = ParticleScale(parentSize);
            float size = (5f + (float)random.NextDouble() * 2f) * scale;

            Color4 colour = random.NextDouble() < 0.55 ? gold_bright : gold_warm;

            var halo = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Leaf,
                Size = new Vector2(size * 1.5f),
                Colour = colour,
                Alpha = 0.18f,
            };

            var leaf = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Leaf,
                Size = new Vector2(size),
                Colour = colour,
                Alpha = 0.88f,
            };

            var bundle = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { halo, leaf },
                Alpha = 0,
                Rotation = (float)((random.NextDouble() - 0.5) * 80),
            };

            parent.Add(bundle);

            double lifetime = 2600 + random.NextDouble() * 900;
            float rotationDelta = (float)((random.NextDouble() - 0.5) * 280);

            bundle.FadeTo(0.92f, 360, Easing.OutQuad);
            bundle.MoveTo(new Vector2(endX, endY), lifetime, Easing.InOutSine);
            bundle.RotateTo(bundle.Rotation + rotationDelta, lifetime, Easing.InOutSine);
            bundle.Delay(lifetime - 480).FadeOut(480, Easing.InQuad).Expire();
        }

        // 5-petal gold sakura, like Imperial Gold but with the
        // deeper warm gold palette of this variant.
        private void emitGoldSakura(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.15 + random.NextDouble() * 1.30) * parentSize.X;
            float startY = -parentSize.Y * (0.18f + (float)random.NextDouble() * 0.28f);

            float endX = startX + (float)((random.NextDouble() - 0.5) * parentSize.X * 0.55f);
            float endY = parentSize.Y * (1.10f + (float)random.NextDouble() * 0.30f);

            float scale = ParticleScale(parentSize);
            float petalSize = (3.0f + (float)random.NextDouble() * 1.6f) * scale;

            var blossom = new LacquerBlossom(petalSize, gold_warm, gold_bright)
            {
                Origin = Anchor.Centre,
                Position = new Vector2(startX, startY),
                Alpha = 0,
                Rotation = (float)(random.NextDouble() * 360),
            };

            parent.Add(blossom);

            double lifetime = 2400 + random.NextDouble() * 800;
            float rotationDelta = (float)((random.NextDouble() - 0.5) * 200);

            blossom.FadeTo(0.92f, 380, Easing.OutQuad);
            blossom.MoveTo(new Vector2(endX, endY), lifetime, Easing.InOutSine);
            blossom.RotateTo(blossom.Rotation + rotationDelta, lifetime, Easing.InOutSine);
            blossom.Delay(lifetime - 460).FadeOut(460, Easing.InQuad).Expire();
        }

        // Koi-ripple. A SpriteIcon ring (FA.Regular.Circle) that
        // materialises at a small size, expands outward to ~3x its
        // start, fading. Reads as "ripple on lacquered water" — the
        // visual that ties the gold-and-onyx mood to a real-world
        // reference (lacquer trays often pool reflective liquid).
        private void emitKoiRipple(Container parent, Vector2 parentSize, Random random)
        {
            float centerX = (float)(-0.10 + random.NextDouble() * 1.20) * parentSize.X;
            float centerY = (float)(-0.10 + random.NextDouble() * 1.20) * parentSize.Y;

            float scale = ParticleScale(parentSize);
            float startSize = (5f + (float)random.NextDouble() * 2f) * scale;
            float endSize = startSize * (3.2f + (float)random.NextDouble() * 0.6f);

            Color4 colour = random.NextDouble() < 0.6 ? gold_bright : cream;

            var ring = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Position = new Vector2(centerX, centerY),
                Icon = FontAwesome.Regular.Circle,
                Size = new Vector2(startSize),
                Colour = colour,
                Alpha = 0,
            };

            parent.Add(ring);

            double lifetime = 900 + random.NextDouble() * 280;

            ring.FadeTo(0.75f, 140, Easing.OutQuad);
            ring.ResizeTo(new Vector2(endSize), lifetime, Easing.OutCubic);
            ring.Delay(lifetime - 300).FadeOut(300, Easing.InQuad).Expire();
        }

        // Bright tapered shimmer — cream-bright head, gold-warm tail.
        // The "polished metal glint" beat.
        private void emitShimmer(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.05 + random.NextDouble() * 1.10) * parentSize.X;
            float startY = parentSize.Y * (0.6f + (float)random.NextDouble() * 0.35f);

            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.10f);
            float driftY = -parentSize.Y * (0.65f + (float)random.NextDouble() * 0.4f);

            float scale = ParticleScale(parentSize);
            float length = (4.5f + (float)random.NextDouble() * 3.5f) * scale;

            var head = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.BottomCentre,
                Width = 1.5f,
                Height = length * 0.35f,
                Colour = cream,
                Alpha = 1f,
            };

            var tail = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.TopCentre,
                Width = 1.0f,
                Height = length * 0.65f,
                Colour = gold_warm,
                Alpha = 0.55f,
            };

            var shimmer = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { tail, head },
                Alpha = 0,
            };

            parent.Add(shimmer);

            double lifetime = 460 + random.NextDouble() * 280;
            shimmer.FadeTo(1f, 60, Easing.OutQuad);
            shimmer.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutCubic);
            shimmer.ScaleTo(0.55f, lifetime, Easing.OutQuad);
            shimmer.Delay(lifetime - 140).FadeOut(140, Easing.InQuad).Expire();
        }

        // Rare gold torii. Slightly larger than the other variants
        // because in this variant the gate is supposed to be a true
        // "moment" — the lacquerware is at rest, then it briefly
        // signals its origin.
        private void emitGoldTorii(Container parent, Vector2 parentSize, Random random)
        {
            float positionRoll = (float)random.NextDouble();
            float centerX, aboveY;
            if (positionRoll < 0.60f)
            {
                centerX = parentSize.X * (0.5f + (float)(random.NextDouble() - 0.5) * 0.12f);
                aboveY  = -parentSize.Y * (0.20f + (float)random.NextDouble() * 0.28f);
            }
            else if (positionRoll < 0.80f)
            {
                centerX = parentSize.X * (-0.20f + (float)random.NextDouble() * 0.08f);
                aboveY  = parentSize.Y * (0.30f + (float)random.NextDouble() * 0.30f);
            }
            else
            {
                centerX = parentSize.X * (1.12f + (float)random.NextDouble() * 0.08f);
                aboveY  = parentSize.Y * (0.30f + (float)random.NextDouble() * 0.30f);
            }

            float scale = ParticleScale(parentSize);
            float size = (17f + (float)random.NextDouble() * 6f) * scale;

            var halo = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.ToriiGate,
                Size = new Vector2(size * 1.55f),
                Colour = gold_warm,
                Alpha = 0.22f,
            };

            var gate = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.ToriiGate,
                Size = new Vector2(size),
                Colour = gold_bright,
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

            bundle.FadeTo(0.95f, 320, Easing.OutQuad);
            bundle.ScaleTo(1f, 700, Easing.OutBack);
            bundle.Delay(760).FadeOut(800, Easing.InQuad).Expire();
        }

        /// <summary>Large persistent crest with a double-ring + onyx
        /// inlay + gold torii. The visual anchor of this variant.</summary>
        private partial class LacquerCrest : CompositeDrawable
        {
            public LacquerCrest()
            {
                AutoSizeAxes = Axes.Both;
                Alpha = 0;
                Blending = BlendingParameters.Additive;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                // Bigger than the other seals — this is the centrepiece
                // of the variant, not a decorative accent.
                const float crest_size = 17f;

                InternalChildren = new Drawable[]
                {
                    // Outer warm-gold halo glow.
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(crest_size * 1.5f),
                        Colour = gold_warm,
                        Alpha = 0.22f,
                    },
                    // Solid onyx backplate — the "lacquer" surface
                    // the gold elements are inlaid into. This is what
                    // distinguishes the variant: the dark base
                    // backing the gold reads as real lacquerware.
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(crest_size * 0.92f),
                        Colour = onyx_deep,
                        Alpha = 0.85f,
                    },
                    // Outer gold rim.
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Regular.Circle,
                        Size = new Vector2(crest_size),
                        Colour = gold_bright,
                        Alpha = 1f,
                    },
                    // Inner gold rim — the "double ring" detail that
                    // separates this crest from the simpler seals of
                    // the other variants.
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Regular.Circle,
                        Size = new Vector2(crest_size * 0.78f),
                        Colour = cream,
                        Alpha = 0.8f,
                    },
                    // Gold torii inlay at the centre.
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Solid.ToriiGate,
                        Size = new Vector2(crest_size * 0.55f),
                        Colour = gold_bright,
                        Alpha = 1f,
                    },
                };

                this.FadeTo(0.62f, 600, Easing.OutQuad);
                this.Delay(600).FadeTo(0.95f, 4400, Easing.InOutSine)
                               .Then().FadeTo(0.62f, 4400, Easing.InOutSine)
                               .Loop();
            }
        }

        /// <summary>5-petal blossom in gold tones with cream pollen.</summary>
        private partial class LacquerBlossom : CompositeDrawable
        {
            public LacquerBlossom(float petalSize, Color4 outerColour, Color4 innerColour)
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
                        Alpha = 0.95f,
                    });

                    children.Add(new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Position = new Vector2(cx, cy),
                        Size = new Vector2(petalSize * 0.45f, petalSize * 0.85f),
                        Rotation = angleDeg,
                        Colour = innerColour,
                        Alpha = 0.9f,
                    });
                }

                children.Add(new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(petalSize * 0.55f),
                    Colour = cream,
                    Alpha = 1f,
                });

                InternalChildren = children.ToArray();
            }
        }
    }
}
