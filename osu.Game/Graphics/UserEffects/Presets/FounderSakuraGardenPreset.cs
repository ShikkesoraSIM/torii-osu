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
    /// Founder variant — "Sakura Garden". The aura everyone thinks of
    /// when they hear "shrine in spring": dominant pink blossoms in a
    /// dense drift, with subtle 24k-gold accents only at the points
    /// that matter (the flanking seals, the rare torii flash, the
    /// blossom centres). Zero vermillion — keeps full visual distance
    /// from Admin.
    ///
    /// Identity signature: TWO densities of pink particle (full
    /// 5-petal blossoms + tiny single petals) producing a layered
    /// "hanami breeze" effect, plus persistent pale-gold seals that
    /// frame the name like wax-stamped origami.
    /// </summary>
    public class FounderSakuraGardenPreset : AuraPreset
    {
        public const string ID = "founder-variant-sakura-garden";

        // Pink family for blossoms — three saturations so a drift
        // reads as layered depth rather than a wall of the same pink.
        private static readonly Color4 sakura_white = new Color4(255, 232, 240, 255);
        private static readonly Color4 sakura_pale  = new Color4(255, 200, 222, 255);
        private static readonly Color4 sakura_mid   = new Color4(255, 158, 195, 255);
        private static readonly Color4 sakura_deep  = new Color4(232, 110, 170, 255);

        // Gold accents — pale champagne only. Used sparingly: seal
        // rims, blossom pollen, the torii flash. Never the dominant
        // colour, just the precious metal that frames the pink.
        private static readonly Color4 gold_champagne = new Color4(255, 230, 178, 255);
        private static readonly Color4 gold_amber     = new Color4(245, 190, 90, 255);

        // Halo glow is warm pink so the username sits in a soft pink
        // atmosphere between particle spawns — the "constant spring"
        // mood the aura is going for.
        private static readonly Color4 halo_pink      = new Color4(255, 175, 205, 255);

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-founder" };

        // Variants are flagged with a very high priority so they never
        // win the prod group-fallback — only render when explicitly
        // equipped by AuraId (used by test personas).
        public override int DefaultPriority => 200;

        // Dense cadence — sakura should feel like a continuous spring
        // shower, not a sparse drift. MaxAlive permits ~10 simultaneous
        // particles, which holds up at chat density without GPU spikes.
        public override double SpawnIntervalMs => 200;
        public override double SpawnJitterMs => 110;
        public override int MaxAlive => 11;

        public override Color4? GlowColour => halo_pink;

        // Persistent flanking seals — gold-rimmed with a SAKURA
        // silhouette inside (FA.Solid.Spa, the closest stylised
        // flower glyph) instead of a torii gate. Rendered as inline
        // ornaments so the layout treats them as part of the name.
        public override Drawable? CreateLeadingOrnament() => new GardenSeal
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
        };

        public override Drawable? CreateTrailingOrnament() => new GardenSeal
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
                    new GardenSeal { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreRight, X = -2 },
                    new GardenSeal { Anchor = Anchor.CentreRight, Origin = Anchor.CentreLeft, X = 2 },
                },
            };

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            double roll = random.NextDouble();
            // Mix tuned for "dense pink drift with hints of gold":
            //   55% full sakura blossom
            //   25% tiny single petal (filler density)
            //   10% gold pollen mote
            //   10% rare gold torii flash
            if (roll < 0.55)
                emitBlossom(parent, parentSize, random);
            else if (roll < 0.80)
                emitTinyPetal(parent, parentSize, random);
            else if (roll < 0.90)
                emitGoldPollen(parent, parentSize, random);
            else
                emitGoldTorii(parent, parentSize, random);
        }

        // Full 5-petal blossom. Slower lifetime than the original
        // Founder's sakura (3s vs 2.2s) so a blossom is on screen
        // long enough to read as drifting — not just dropping past.
        private void emitBlossom(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.20 + random.NextDouble() * 1.40) * parentSize.X;
            float startY = -parentSize.Y * (0.20f + (float)random.NextDouble() * 0.30f);

            // Wider horizontal drift than other variants — gives the
            // garden a sense of "wind crossing the courtyard".
            float endX = startX + (float)((random.NextDouble() - 0.5) * parentSize.X * 0.70f);
            float endY = parentSize.Y * (1.15f + (float)random.NextDouble() * 0.40f);

            float scale = ParticleScale(parentSize);
            float petalSize = (3.2f + (float)random.NextDouble() * 1.7f) * scale;

            Color4 outerColour = random.NextDouble() switch
            {
                < 0.40 => sakura_mid,
                < 0.75 => sakura_pale,
                _      => sakura_deep,
            };
            Color4 innerColour = random.NextDouble() < 0.6 ? sakura_white : sakura_pale;

            var blossom = new GardenBlossom(petalSize, outerColour, innerColour)
            {
                Origin = Anchor.Centre,
                Position = new Vector2(startX, startY),
                Alpha = 0,
                Rotation = (float)(random.NextDouble() * 360),
            };

            parent.Add(blossom);

            double lifetime = 3000 + random.NextDouble() * 900;
            float rotationDelta = (float)((random.NextDouble() - 0.5) * 240);

            blossom.FadeTo(0.92f, 380, Easing.OutQuad);
            blossom.MoveTo(new Vector2(endX, endY), lifetime, Easing.InOutSine);
            blossom.RotateTo(blossom.Rotation + rotationDelta, lifetime, Easing.InOutSine);
            blossom.Delay(lifetime - 500).FadeOut(500, Easing.InQuad).Expire();
        }

        // Tiny single petal — just one oval, no centre. Fills the
        // garden with "petal density" without each particle being a
        // full blossom (which would tank framerate or read as a wall
        // of flowers).
        private void emitTinyPetal(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.15 + random.NextDouble() * 1.30) * parentSize.X;
            float startY = -parentSize.Y * (0.15f + (float)random.NextDouble() * 0.20f);

            float endX = startX + (float)((random.NextDouble() - 0.5) * parentSize.X * 0.55f);
            float endY = parentSize.Y * (1.05f + (float)random.NextDouble() * 0.40f);

            float scale = ParticleScale(parentSize);
            float petalWidth  = (1.8f + (float)random.NextDouble() * 0.8f) * scale;
            float petalHeight = (3.0f + (float)random.NextDouble() * 1.4f) * scale;

            Color4 colour = random.NextDouble() < 0.55 ? sakura_pale : sakura_mid;

            var petal = new Circle
            {
                Origin = Anchor.Centre,
                Position = new Vector2(startX, startY),
                Size = new Vector2(petalWidth, petalHeight),
                Colour = colour,
                Alpha = 0,
                Rotation = (float)(random.NextDouble() * 360),
            };

            parent.Add(petal);

            double lifetime = 2400 + random.NextDouble() * 700;
            float rotationDelta = (float)((random.NextDouble() - 0.5) * 300);

            petal.FadeTo(0.85f, 280, Easing.OutQuad);
            petal.MoveTo(new Vector2(endX, endY), lifetime, Easing.InOutSine);
            petal.RotateTo(petal.Rotation + rotationDelta, lifetime, Easing.InOutSine);
            petal.Delay(lifetime - 360).FadeOut(360, Easing.InQuad).Expire();
        }

        // Tiny gold pollen mote — drifts up past the username. The
        // ONLY rising motion in the aura (everything pink falls), so
        // it visually balances the downward sakura drift and adds the
        // "warmth" the user wanted from the gold palette.
        private void emitGoldPollen(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.05 + random.NextDouble() * 1.10) * parentSize.X;
            float startY = parentSize.Y * (0.85f + (float)random.NextDouble() * 0.25f);

            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.18f);
            float driftY = -parentSize.Y * (1.20f + (float)random.NextDouble() * 0.40f);

            float scale = ParticleScale(parentSize);
            float size = (2.2f + (float)random.NextDouble() * 1.4f) * scale;

            var halo = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size * 2.5f),
                Colour = gold_amber,
                Alpha = 0.20f,
            };

            var core = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size),
                Colour = gold_champagne,
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

            double lifetime = 1600 + random.NextDouble() * 500;
            mote.FadeTo(1f, 240, Easing.OutQuad);
            mote.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutSine);
            mote.ScaleTo(0.55f, lifetime, Easing.OutQuad);
            mote.Delay(lifetime - 280).FadeOut(280, Easing.InQuad).Expire();
        }

        // Rare gold torii. Same idea as the original Founder's gate
        // flash but rendered in champagne gold instead of vermillion,
        // so it harmonises with the seal palette + the pollen motes.
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
            float size = (15f + (float)random.NextDouble() * 5f) * scale;

            var halo = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.ToriiGate,
                Size = new Vector2(size * 1.45f),
                Colour = sakura_pale,
                Alpha = 0.22f,
            };

            var gate = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.ToriiGate,
                Size = new Vector2(size),
                Colour = gold_champagne,
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

            bundle.FadeTo(0.92f, 320, Easing.OutQuad);
            bundle.ScaleTo(1f, 660, Easing.OutBack);
            bundle.Delay(720).FadeOut(740, Easing.InQuad).Expire();
        }

        /// <summary>Persistent gold-rimmed seal with a pale-pink
        /// stylised flower inside instead of a torii. Origami-stamp
        /// look — distinct from the other variants' coin-seal.</summary>
        private partial class GardenSeal : CompositeDrawable
        {
            public GardenSeal()
            {
                AutoSizeAxes = Axes.Both;
                Alpha = 0;
                Blending = BlendingParameters.Additive;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                const float seal_size = 13f;

                InternalChildren = new Drawable[]
                {
                    // Soft pink halo so the seal reads as warm.
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(seal_size * 1.35f),
                        Colour = sakura_pale,
                        Alpha = 0.20f,
                    },
                    // Outer gold rim.
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Regular.Circle,
                        Size = new Vector2(seal_size),
                        Colour = gold_champagne,
                        Alpha = 1f,
                    },
                    // Stylised flower glyph (Spa = a 4-leaf rounded
                    // leaf icon that reads as a cherry-blossom-ish
                    // bloom at small sizes).
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Solid.Spa,
                        Size = new Vector2(seal_size * 0.55f),
                        Colour = sakura_mid,
                        Alpha = 0.95f,
                    },
                };

                this.FadeTo(0.55f, 600, Easing.OutQuad);
                this.Delay(600).FadeTo(0.92f, 3400, Easing.InOutSine)
                               .Then().FadeTo(0.55f, 3400, Easing.InOutSine)
                               .Loop();
            }
        }

        /// <summary>5-petal blossom in pink with a gold pollen centre.</summary>
        private partial class GardenBlossom : CompositeDrawable
        {
            public GardenBlossom(float petalSize, Color4 outerColour, Color4 innerColour)
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
                        Alpha = 0.88f,
                    });
                }

                // Gold pollen centre — the one warm spot in an
                // otherwise all-pink blossom. Threads the gold accent
                // through the dominant pink theme.
                children.Add(new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(petalSize * 0.55f),
                    Colour = gold_amber,
                    Alpha = 1f,
                });

                InternalChildren = children.ToArray();
            }
        }
    }
}
