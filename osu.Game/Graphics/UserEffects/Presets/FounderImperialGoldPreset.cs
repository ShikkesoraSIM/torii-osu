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
    /// Founder variant — "Imperial Gold". Pure 24k-gold opulence with
    /// zero vermillion. The whole aura runs on a warm-yellow palette
    /// (champagne, polished gold, amber) so it can never be confused
    /// with the Admin aura (which is built on cherry-red embers).
    ///
    /// Identity signature: persistent twin gold seals flanking the
    /// name, plus a steady stream of gold sakura blossoms rising past
    /// it. Rare "imperial torii" flash — a gold-on-gold torii glyph
    /// briefly blooming above the username — gives it the moment of
    /// brand recognition without leaning on the red palette.
    /// </summary>
    public class FounderImperialGoldPreset : AuraPreset
    {
        public const string ID = "founder-variant-imperial-gold";

        // Gold family. Spaced across lightness so a particle cluster
        // reads as gilded metal (varied highlights) rather than a flat
        // yellow blob. Champagne anchors the cool end, polished gold
        // the mid, amber the warm end.
        private static readonly Color4 gold_champagne = new Color4(255, 234, 184, 255);
        private static readonly Color4 gold_polished  = new Color4(255, 206, 102, 255);
        private static readonly Color4 gold_amber     = new Color4(245, 174, 60, 255);
        private static readonly Color4 gold_deep      = new Color4(210, 142, 44, 255);
        private static readonly Color4 halo_gold      = new Color4(255, 196, 110, 255);

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-founder" };

        // Variants intentionally have a very high DefaultPriority so
        // they never win the group-fallback resolution in production —
        // real Founder users without an explicit equipped aura get the
        // baseline FounderAuraPreset. Variants only render when a user
        // explicitly equips them by AuraId (used by test personas).
        public override int DefaultPriority => 200;

        public override double SpawnIntervalMs => 210;
        public override double SpawnJitterMs => 120;
        public override int MaxAlive => 13;

        public override Color4? GlowColour => halo_gold;

        // Persistent twin imperial seals — pure gold ring + smaller
        // gold torii inside, no red anywhere. Rendered as INLINE
        // ornaments (leading + trailing) so they sit immediately
        // before / after the username and participate in the
        // wrapper's bounding box: the row layout sees "seal + name +
        // seal" as a single unit and the seals never bleed into
        // adjacent columns.
        // Anchor + Origin = CentreLeft on both ornaments so the
        // FillFlowContainer's horizontal layout leaves their Y at the
        // row's vertical centre (the username text typically sits
        // taller than the seal — without this the seal would top-align
        // with the text and read as floating above the baseline).
        public override Drawable? CreateLeadingOrnament() => new ImperialSeal
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
        };

        public override Drawable? CreateTrailingOrnament() => new ImperialSeal
        {
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
        };

        // Fallback rendering path for wrappers that can't use the
        // inline-flow approach (RelativeSizeAxes=X on the wrapper, i.e.
        // TruncatingSpriteText surfaces like the song-select leaderboard
        // and gameplay HUD). In those contexts UserAuraContainer
        // skips CreateLeadingOrnament/CreateTrailingOrnament and falls
        // back to CreateBackground — the seals still appear flanking
        // the name (now bounded by the MaxWidth-clamped emitter so
        // they don't bleed past the visible truncated text), they just
        // don't participate in the wrapper's bounding box for layout
        // purposes.
        public override Drawable? CreateBackground() =>
            new Container
            {
                Children = new Drawable[]
                {
                    new ImperialSeal { Anchor = Anchor.CentreLeft, Origin = Anchor.CentreRight, X = -2 },
                    new ImperialSeal { Anchor = Anchor.CentreRight, Origin = Anchor.CentreLeft, X = 2 },
                },
            };

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            double roll = random.NextDouble();
            // Particle mix (tuned for stronger torii presence — the
            // imperial moment is what makes this variant land as
            // "founder gold" instead of generic warm particles):
            //   45% gold sakura     — the signature airy beat
            //   23% rising gold mote — steady warm pulse
            //   17% tapered shimmer  — fast crackle texture
            //   15% imperial torii   — bumped from 10%, more presence
            if (roll < 0.45)
                emitGoldSakura(parent, parentSize, random);
            else if (roll < 0.68)
                emitGoldMote(parent, parentSize, random);
            else if (roll < 0.85)
                emitShimmer(parent, parentSize, random);
            else
                emitImperialTorii(parent, parentSize, random);
        }

        // SIGNATURE. A 5-petal gold sakura, drifting laterally + down
        // around the username. The blossom uses two gold tones (outer
        // polished, inner champagne) for visible depth.
        private void emitGoldSakura(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.20 + random.NextDouble() * 1.40) * parentSize.X;
            float startY = -parentSize.Y * (0.20f + (float)random.NextDouble() * 0.30f);

            float endX = startX + (float)((random.NextDouble() - 0.5) * parentSize.X * 0.55f);
            float endY = parentSize.Y * (1.15f + (float)random.NextDouble() * 0.30f);

            float scale = ParticleScale(parentSize);
            float petalSize = (3.2f + (float)random.NextDouble() * 1.8f) * scale;

            Color4 outerColour = random.NextDouble() < 0.55 ? gold_polished : gold_amber;
            Color4 innerColour = gold_champagne;

            var blossom = new GoldBlossom(petalSize, outerColour, innerColour)
            {
                Origin = Anchor.Centre,
                Position = new Vector2(startX, startY),
                Alpha = 0,
                Rotation = (float)(random.NextDouble() * 360),
            };

            parent.Add(blossom);

            double lifetime = 2400 + random.NextDouble() * 800;
            float rotationDelta = (float)((random.NextDouble() - 0.5) * 200);

            blossom.FadeTo(0.95f, 360, Easing.OutQuad);
            blossom.MoveTo(new Vector2(endX, endY), lifetime, Easing.InOutSine);
            blossom.RotateTo(blossom.Rotation + rotationDelta, lifetime, Easing.InOutSine);
            blossom.Delay(lifetime - 460).FadeOut(460, Easing.InQuad).Expire();
        }

        // Rising gold mote — round, soft halo around a brighter core.
        // The warm "always-on energy" particle.
        private void emitGoldMote(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.05 + random.NextDouble() * 1.10) * parentSize.X;
            float startY = parentSize.Y * (0.85f + (float)random.NextDouble() * 0.30f);

            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.22f);
            float driftY = -parentSize.Y * (1.25f + (float)random.NextDouble() * 0.45f);

            float scale = ParticleScale(parentSize);
            float size = (3.0f + (float)random.NextDouble() * 1.8f) * scale;

            Color4 coreColour = random.NextDouble() < 0.6 ? gold_polished : gold_amber;

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

            double lifetime = 1400 + random.NextDouble() * 500;
            mote.FadeTo(1f, 240, Easing.OutQuad);
            mote.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutSine);
            mote.ScaleTo(0.55f, lifetime, Easing.OutQuad);
            mote.Delay(lifetime - 280).FadeOut(280, Easing.InQuad).Expire();
        }

        // Tapered shimmer — bright champagne flash with a deeper gold
        // tail. Adds metallic "specular" sparkle to the aura.
        private void emitShimmer(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.05 + random.NextDouble() * 1.10) * parentSize.X;
            float startY = parentSize.Y * (0.65f + (float)random.NextDouble() * 0.30f);

            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.12f);
            float driftY = -parentSize.Y * (0.7f + (float)random.NextDouble() * 0.45f);

            float scale = ParticleScale(parentSize);
            float length = (5f + (float)random.NextDouble() * 3.5f) * scale;

            var head = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.BottomCentre,
                Width = 1.6f,
                Height = length * 0.35f,
                Colour = gold_champagne,
                Alpha = 1f,
            };

            var tail = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.TopCentre,
                Width = 1.1f,
                Height = length * 0.65f,
                Colour = gold_amber,
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

            double lifetime = 480 + random.NextDouble() * 280;
            shimmer.FadeTo(1f, 60, Easing.OutQuad);
            shimmer.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutCubic);
            shimmer.ScaleTo(0.55f, lifetime, Easing.OutQuad);
            shimmer.Delay(lifetime - 140).FadeOut(140, Easing.InQuad).Expire();
        }

        // Rare imperial torii — gold-on-gold (no red). Larger than the
        // ember torii of the original Founder so when it does appear
        // it lands as a "moment of crowning".
        private void emitImperialTorii(Container parent, Vector2 parentSize, Random random)
        {
            float positionRoll = (float)random.NextDouble();
            float centerX, aboveY;
            if (positionRoll < 0.55f)
            {
                centerX = parentSize.X * (0.5f + (float)(random.NextDouble() - 0.5) * 0.12f);
                aboveY  = -parentSize.Y * (0.18f + (float)random.NextDouble() * 0.28f);
            }
            else if (positionRoll < 0.78f)
            {
                centerX = parentSize.X * (-0.20f + (float)random.NextDouble() * 0.10f);
                aboveY  = parentSize.Y * (0.30f + (float)random.NextDouble() * 0.30f);
            }
            else
            {
                centerX = parentSize.X * (1.10f + (float)random.NextDouble() * 0.10f);
                aboveY  = parentSize.Y * (0.30f + (float)random.NextDouble() * 0.30f);
            }

            float scale = ParticleScale(parentSize);
            // Bumped from 16-22px → 18-26px. The torii is the
            // identity beat; when it does fire, it should land as a
            // real moment, not a small decoration.
            float size = (18f + (float)random.NextDouble() * 8f) * scale;

            // Soft champagne halo behind the torii so the glyph reads
            // as illuminated even on lighter backgrounds.
            var halo = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.ToriiGate,
                Size = new Vector2(size * 1.55f),
                Colour = gold_champagne,
                Alpha = 0.26f,
            };

            var gate = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.ToriiGate,
                Size = new Vector2(size),
                Colour = gold_polished,
                Alpha = 1f,
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
            bundle.Delay(720).FadeOut(780, Easing.InQuad).Expire();
        }

        /// <summary>
        /// Persistent gold-on-gold flanking seal. Anatomy ported from
        /// the V2 (Sakura Garden) GardenSeal because the previous
        /// torii-inside-a-double-rim treatment never read cleanly at
        /// 14px — the glyph was always fighting the ring for the same
        /// pixels. V2's "soft halo + thin Regular.Circle outline +
        /// small Spa glyph at 0.55x" balances better at this size, so
        /// we lift the structure and replace V2's pink palette with
        /// V1's gold palette. The rare torii moment is still served
        /// by the ToriiGate particle flash in EmitParticle.
        /// </summary>
        private partial class ImperialSeal : CompositeDrawable
        {
            public ImperialSeal()
            {
                AutoSizeAxes = Axes.Both;
                Alpha = 0;
                // Additive blending so the seal stays as bright as it
                // looked when it lived inside the emitter's additive
                // layer. After we moved seals out of the emitter
                // (CreateBackground path) and into being direct
                // children of the wrapper or inline-flow siblings,
                // the default alpha-blending made them visibly dimmer
                // than the previous treatment.
                Blending = BlendingParameters.Additive;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                // Matches V2's GardenSeal sizing exactly (13px ring +
                // 1.35x halo + 0.55x inner glyph). The user explicitly
                // asked for the V2 badge design ported to V1 — don't
                // diverge from those proportions without re-checking
                // both variants together.
                const float seal_size = 13f;

                InternalChildren = new Drawable[]
                {
                    // Soft warm-gold halo so the seal reads as
                    // illuminated, mirroring V2's pink halo but in the
                    // V1 palette. Alpha matches V2 (0.20) — additive
                    // blending pushes it brighter on screen anyway.
                    new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(seal_size * 1.35f),
                        Colour = halo_gold,
                        Alpha = 0.22f,
                    },
                    // Champagne rim — the lighter gold reads as the
                    // metal edge of the seal, same role V2's
                    // gold_champagne rim plays for the pink badge.
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Regular.Circle,
                        Size = new Vector2(seal_size),
                        Colour = gold_champagne,
                        Alpha = 1f,
                    },
                    // Inner Spa glyph in amber — V2's design uses the
                    // Spa icon (a 4-leaf rounded cluster) instead of
                    // a torii because the torii silhouette never reads
                    // clearly below ~20px. Amber gives the warm-metal
                    // contrast against the lighter champagne rim that
                    // sakura_mid gave against gold_champagne in V2.
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Solid.Spa,
                        Size = new Vector2(seal_size * 0.55f),
                        Colour = gold_amber,
                        Alpha = 0.95f,
                    },
                };

                // Identical breath cadence to V2 so the two variants
                // pulse in sync when shown side-by-side in the test
                // comparison view.
                this.FadeTo(0.55f, 600, Easing.OutQuad);
                this.Delay(600).FadeTo(0.92f, 3400, Easing.InOutSine)
                               .Then().FadeTo(0.55f, 3400, Easing.InOutSine)
                               .Loop();
            }
        }

        /// <summary>5-petal blossom in gold tones.</summary>
        private partial class GoldBlossom : CompositeDrawable
        {
            public GoldBlossom(float petalSize, Color4 outerColour, Color4 innerColour)
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

                // Polished gold pollen centre.
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
