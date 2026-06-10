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
    /// Founder variant — "Crest of Honor". The most restrained of the
    /// five variants: heraldic gold crests carry the identity, and
    /// particles are deliberately sparse so the eye always returns to
    /// the crests as the visual story. Reads as "this person was
    /// awarded distinction" rather than "this person has a fancy
    /// particle effect".
    ///
    /// Identity signature: the LARGEST + most detailed flanking
    /// crests in the variant set (triple ring + laurel halo + torii
    /// inset + slow gold-rim shimmer), with whisper-light particle
    /// density. The aura's silence around the crests is the point.
    /// </summary>
    public class FounderCrestOfHonorPreset : AuraPreset
    {
        public const string ID = "founder-variant-crest-of-honor";

        // Rich gold + amber palette — fewer tones than the other
        // variants because the crests need a tight palette to read
        // as "one polished object" rather than a mosaic.
        private static readonly Color4 gold_polished = new Color4(255, 210, 110, 255);
        private static readonly Color4 gold_warm     = new Color4(225, 174, 76, 255);
        private static readonly Color4 gold_deep     = new Color4(180, 130, 50, 255);
        private static readonly Color4 cream         = new Color4(255, 240, 215, 255);

        // Soft warm halo so the username has presence even when no
        // particles are alive — this aura is mostly "silent".
        private static readonly Color4 halo_gold     = new Color4(220, 170, 100, 255);

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-founder" };

        // Variants are flagged with a very high priority so they never
        // win the prod group-fallback — only render when explicitly
        // equipped by AuraId (used by test personas).
        public override int DefaultPriority => 200;

        // SLOW cadence. This variant is supposed to feel "still and
        // dignified" — particles are accents, not the show.
        public override double SpawnIntervalMs => 480;
        public override double SpawnJitterMs => 280;
        public override int MaxAlive => 5;

        public override Color4? GlowColour => halo_gold;

        // The CENTERPIECE — large laurel-and-gate crests on each
        // flank. Rendered as inline ornaments so the row layout
        // visually groups them with the username; their generous
        // size means the wrapper's bounding box grows accordingly.
        public override Drawable? CreateLeadingOrnament() => new HeraldicCrest
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
        };

        public override Drawable? CreateTrailingOrnament() => new HeraldicCrest
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
                    new HeraldicCrest { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreRight, X = -3 },
                    new HeraldicCrest { Anchor = Anchor.CentreRight, Origin = Anchor.CentreLeft, X = 3 },
                },
            };

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            double roll = random.NextDouble();
            // Mix tuned for sparseness:
            //   55% drifting gold leaf  — slow, mostly fades into the bg
            //   25% tiny gold spark     — punctuation
            //   15% rare gold sakura    — the "moment" beat
            //    5% rare gold torii     — sparse signature
            if (roll < 0.55)
                emitGoldLeaf(parent, parentSize, random);
            else if (roll < 0.80)
                emitTinySpark(parent, parentSize, random);
            else if (roll < 0.95)
                emitGoldSakura(parent, parentSize, random);
            else
                emitGoldTorii(parent, parentSize, random);
        }

        // Slow gold leaf drifting around the name. The dominant
        // particle here, but at a slow rate + with a long lifetime
        // so it reads as "occasional".
        private void emitGoldLeaf(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.18 + random.NextDouble() * 1.36) * parentSize.X;
            float startY = -parentSize.Y * (0.12f + (float)random.NextDouble() * 0.22f);

            float endX = startX + (float)((random.NextDouble() - 0.5) * parentSize.X * 0.65f);
            float endY = parentSize.Y * (1.10f + (float)random.NextDouble() * 0.35f);

            float scale = ParticleScale(parentSize);
            float size = (4.5f + (float)random.NextDouble() * 1.8f) * scale;

            var leaf = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Position = new Vector2(startX, startY),
                Icon = FontAwesome.Solid.Leaf,
                Size = new Vector2(size),
                Colour = random.NextDouble() < 0.55 ? gold_polished : gold_warm,
                Alpha = 0,
                Rotation = (float)((random.NextDouble() - 0.5) * 80),
            };

            parent.Add(leaf);

            double lifetime = 2800 + random.NextDouble() * 1000;
            float rotationDelta = (float)((random.NextDouble() - 0.5) * 240);

            leaf.FadeTo(0.85f, 380, Easing.OutQuad);
            leaf.MoveTo(new Vector2(endX, endY), lifetime, Easing.InOutSine);
            leaf.RotateTo(leaf.Rotation + rotationDelta, lifetime, Easing.InOutSine);
            leaf.Delay(lifetime - 480).FadeOut(480, Easing.InQuad).Expire();
        }

        // Tiny gold spark — a single small round dot, brief. Like a
        // momentary glint catching the eye. Sparser than the other
        // variants' sparks.
        private void emitTinySpark(Container parent, Vector2 parentSize, Random random)
        {
            float centerX = (float)(-0.05 + random.NextDouble() * 1.10) * parentSize.X;
            float centerY = (float)(0.05 + random.NextDouble() * 0.90) * parentSize.Y;

            float scale = ParticleScale(parentSize);
            float size = (2.0f + (float)random.NextDouble() * 1.2f) * scale;

            var halo = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size * 2.4f),
                Colour = gold_warm,
                Alpha = 0.28f,
            };

            var core = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size),
                Colour = cream,
                Alpha = 1f,
            };

            var spark = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(centerX, centerY),
                Children = new Drawable[] { halo, core },
                Alpha = 0,
                Scale = new Vector2(0.6f),
            };

            parent.Add(spark);

            spark.FadeTo(1f, 110, Easing.OutQuad);
            spark.ScaleTo(1f, 260, Easing.OutBack);
            spark.Delay(280).FadeOut(380, Easing.InQuad).Expire();
        }

        // Rare gold sakura — a 5-petal blossom in gold tones. Even
        // rarer than the leaves; meant to feel like an honour-roll
        // "highlight moment" when one drifts past.
        private void emitGoldSakura(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.15 + random.NextDouble() * 1.30) * parentSize.X;
            float startY = -parentSize.Y * (0.18f + (float)random.NextDouble() * 0.28f);

            float endX = startX + (float)((random.NextDouble() - 0.5) * parentSize.X * 0.50f);
            float endY = parentSize.Y * (1.10f + (float)random.NextDouble() * 0.35f);

            float scale = ParticleScale(parentSize);
            float petalSize = (3.2f + (float)random.NextDouble() * 1.6f) * scale;

            var blossom = new HeraldicBlossom(petalSize, gold_warm, gold_polished)
            {
                Origin = Anchor.Centre,
                Position = new Vector2(startX, startY),
                Alpha = 0,
                Rotation = (float)(random.NextDouble() * 360),
            };

            parent.Add(blossom);

            double lifetime = 2600 + random.NextDouble() * 800;
            float rotationDelta = (float)((random.NextDouble() - 0.5) * 200);

            blossom.FadeTo(0.92f, 380, Easing.OutQuad);
            blossom.MoveTo(new Vector2(endX, endY), lifetime, Easing.InOutSine);
            blossom.RotateTo(blossom.Rotation + rotationDelta, lifetime, Easing.InOutSine);
            blossom.Delay(lifetime - 460).FadeOut(460, Easing.InQuad).Expire();
        }

        // Very rare gold torii. The signature "moment" — fires maybe
        // once every 10 seconds at this variant's spawn rate.
        private void emitGoldTorii(Container parent, Vector2 parentSize, Random random)
        {
            float positionRoll = (float)random.NextDouble();
            float centerX, aboveY;
            if (positionRoll < 0.65f)
            {
                centerX = parentSize.X * (0.5f + (float)(random.NextDouble() - 0.5) * 0.12f);
                aboveY  = -parentSize.Y * (0.20f + (float)random.NextDouble() * 0.28f);
            }
            else if (positionRoll < 0.82f)
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
            float size = (16f + (float)random.NextDouble() * 5f) * scale;

            var halo = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.ToriiGate,
                Size = new Vector2(size * 1.5f),
                Colour = gold_warm,
                Alpha = 0.24f,
            };

            var gate = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.ToriiGate,
                Size = new Vector2(size),
                Colour = gold_polished,
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
            bundle.Delay(760).FadeOut(820, Easing.InQuad).Expire();
        }

        /// <summary>The biggest, most detailed crest in the variant
        /// set. Triple ring + laurel halo + torii inset + breathing
        /// alpha. The visual centre of the aura.</summary>
        private partial class HeraldicCrest : CompositeDrawable
        {
            public HeraldicCrest()
            {
                AutoSizeAxes = Axes.Both;
                Alpha = 0;
                Blending = BlendingParameters.Additive;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                // Biggest crest size of any variant — this is the
                // identity of the preset.
                const float crest_size = 19f;

                InternalChildren = new Drawable[]
                {
                    // Wide warm-gold ambient glow — the "laurel halo"
                    // around the crest, signalling honour.
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(crest_size * 1.7f),
                        Colour = gold_warm,
                        Alpha = 0.22f,
                    },
                    // Solid deep-gold backplate so the inner glyphs
                    // read as inlaid on a gold disc, not floating.
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(crest_size * 0.95f),
                        Colour = gold_deep,
                        Alpha = 0.85f,
                    },
                    // First (outer) bright ring.
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Regular.Circle,
                        Size = new Vector2(crest_size),
                        Colour = gold_polished,
                        Alpha = 1f,
                    },
                    // Second (mid) ring — narrower, slightly inset.
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Regular.Circle,
                        Size = new Vector2(crest_size * 0.82f),
                        Colour = cream,
                        Alpha = 0.8f,
                    },
                    // Third (inner) ring — the tightest rim, hugging
                    // the central glyph. The triple-rim arrangement
                    // is what makes this crest read as "more elaborate
                    // than the seals on other variants".
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Regular.Circle,
                        Size = new Vector2(crest_size * 0.66f),
                        Colour = gold_polished,
                        Alpha = 0.9f,
                    },
                    // Centre torii inset, big enough to be the focal
                    // point.
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Solid.ToriiGate,
                        Size = new Vector2(crest_size * 0.50f),
                        Colour = cream,
                        Alpha = 1f,
                    },
                };

                this.FadeTo(0.70f, 600, Easing.OutQuad);
                this.Delay(600).FadeTo(0.98f, 5000, Easing.InOutSine)
                               .Then().FadeTo(0.70f, 5000, Easing.InOutSine)
                               .Loop();
            }
        }

        /// <summary>5-petal blossom in gold for the rare sakura beat.</summary>
        private partial class HeraldicBlossom : CompositeDrawable
        {
            public HeraldicBlossom(float petalSize, Color4 outerColour, Color4 innerColour)
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
                        Alpha = 0.92f,
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
