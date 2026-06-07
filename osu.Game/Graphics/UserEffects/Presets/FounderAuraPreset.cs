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
    /// Founder aura: a torii-shrine themed effect reserved for the
    /// early-adopter cohort (server-side id &lt;= 100). Designed to feel
    /// dignified and ceremonial without being lifeless — the visual
    /// metaphor is "warm wind moving past a shrine gate at dusk".
    ///
    /// Four layered particle types, all spawning AROUND the username
    /// (not just inside its bounding box) so the aura reads as the
    /// person's surroundings rather than a decal pasted on top of the
    /// letters:
    ///
    /// <list type="bullet">
    /// <item>Sakura blossom (~45%, the signature look). A custom
    ///       five-petal flower drifting laterally + slowly downward
    ///       with rotation, fading at the end of its arc. Petals use a
    ///       layered pink palette so a blossom doesn't read as a flat
    ///       sticker.</item>
    /// <item>Vermillion ember (~30%). Round mote rising past the name —
    ///       the constant "live fire" beat keeping the aura warm.</item>
    /// <item>Warm gold spark (~20%). Short tapered line, fast attack,
    ///       gives the aura a sense of crackling life.</item>
    /// <item>Torii gate flash (~5%). Faint vermillion gate silhouette
    ///       blooming above the username — the namesake identity hook.</item>
    /// </list>
    /// </summary>
    public class FounderAuraPreset : AuraPreset
    {
        public const string ID = "founder-torii";

        // Vermillion family for embers + glow + gate flash. Matches the
        // ToriiClientBadge palette so all Torii-branded UI shares one
        // red.
        private static readonly Color4 vermillion_bright = new Color4(255, 80, 60, 255);
        private static readonly Color4 vermillion_deep   = new Color4(204, 60, 50, 255);

        // Warm gold for the spark accents.
        private static readonly Color4 gold_warm = new Color4(255, 200, 130, 255);
        private static readonly Color4 gold_pale = new Color4(255, 224, 180, 255);

        // Sakura palette: layered pinks so a blossom's outer petals read
        // a touch deeper than its inner highlight, giving each flower
        // visible depth even at small sizes. The deep_pink anchors the
        // colour so blossoms don't get washed out by background.
        private static readonly Color4 sakura_pale = new Color4(255, 215, 230, 255);
        private static readonly Color4 sakura_mid  = new Color4(255, 175, 205, 255);
        private static readonly Color4 sakura_deep = new Color4(232, 130, 175, 255);

        // Softer vermillion for the halo so the glow reads as ambient
        // shrine-light, not a saturated red wash that swallows the
        // letters underneath.
        private static readonly Color4 halo_vermillion = new Color4(220, 110, 90, 255);

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-founder" };

        public override int DefaultPriority => 70;

        // Slightly higher density than the previous revision — the
        // user wanted the aura to have more body. MaxAlive is still
        // capped sanely (10) so a hall full of founders doesn't tank
        // framerate.
        public override double SpawnIntervalMs => 240;
        public override double SpawnJitterMs => 150;
        public override int MaxAlive => 10;

        public override Color4? GlowColour => halo_vermillion;

        // Persistent twin gold seals flanking the username. This is the
        // visual marker that makes the Founder aura feel "earned" rather
        // than just a louder version of another preset: regular users
        // never get a permanent decoration around their name, only
        // transient particles. The seals declare "this person was here
        // first" the moment the eye lands on the name, before any
        // particle even spawns.
        //
        // Each seal is a thin gold ring with a small vermillion torii
        // glyph inside, pulsing gently in alpha so the seals breathe
        // with the aura instead of being a static stamp. Anchored just
        // OUTSIDE the username's left and right edges so they read as
        // bookend ornaments rather than text overlays.
        public override Drawable? CreateBackground() =>
            new Container
            {
                // The emitter sets RelativeSizeAxes = Axes.Both on this
                // container before parenting it, so the children below
                // resolve their anchors against the username bounding
                // box.
                Children = new Drawable[]
                {
                    new FounderSeal
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreRight,
                        // Push the seal just outside the username's left
                        // edge so it never overlaps the first letter.
                        X = -6,
                    },
                    new FounderSeal
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreLeft,
                        X = 6,
                    },
                },
            };

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            double roll = random.NextDouble();
            // Re-tuned mix:
            //   40% sakura  (was 45)
            //   25% ember   (was 30)
            //   20% spark   (was 20)
            //   15% torii gate flash  (was 5 — bumped 3x per user request)
            if (roll < 0.40)
                emitSakuraBlossom(parent, parentSize, random);
            else if (roll < 0.65)
                emitEmber(parent, parentSize, random);
            else if (roll < 0.85)
                emitGoldSpark(parent, parentSize, random);
            else
                emitGateFlash(parent, parentSize, random);
        }

        // SIGNATURE particle. A 5-petal sakura blossom drifting around
        // the name with a slow rotation. Spawns slightly ABOVE the
        // username (negative Y) and drifts laterally + downward past
        // the baseline — reads as "petals carried on a soft breeze",
        // matching the shrine atmosphere the aura is going for.
        private void emitSakuraBlossom(Container parent, Vector2 parentSize, Random random)
        {
            // Start ABOVE the username so blossoms enter from over the
            // text frame. Wider horizontal range (-0.15..1.15) so they
            // appear AROUND the name, not only inside it.
            float startX = (float)(-0.15 + random.NextDouble() * 1.30) * parentSize.X;
            float startY = -parentSize.Y * (0.20f + (float)random.NextDouble() * 0.30f);

            // Lateral wander + downward drift past the baseline.
            float endX = startX + (float)((random.NextDouble() - 0.5) * parentSize.X * 0.50f);
            float endY = parentSize.Y * (1.10f + (float)random.NextDouble() * 0.30f);

            float scale = ParticleScale(parentSize);
            float petalSize = (3.0f + (float)random.NextDouble() * 1.5f) * scale;

            // Two-tone per blossom so the outer petals can be deeper
            // than the inner highlight. Mostly mid/pale so a cluster
            // of blossoms looks airy; occasional deep for depth.
            Color4 outerColour = random.NextDouble() switch
            {
                < 0.55 => sakura_pale,
                < 0.90 => sakura_mid,
                _      => sakura_deep,
            };
            Color4 innerColour = sakura_pale;

            var blossom = new SakuraBlossom(petalSize, outerColour, innerColour)
            {
                Origin = Anchor.Centre,
                Position = new Vector2(startX, startY),
                Alpha = 0,
                Rotation = (float)(random.NextDouble() * 360),
            };

            parent.Add(blossom);

            double lifetime = 2200 + random.NextDouble() * 700;
            // Slow rotation gives the petals a sense of tumble in the
            // breeze. Total spin ~80-120° over the lifetime — enough to
            // read as alive, not enough to look like a propeller.
            float rotationDelta = (float)((random.NextDouble() - 0.5) * 200);

            blossom.FadeTo(0.88f, 360, Easing.OutQuad);
            blossom.MoveTo(new Vector2(endX, endY), lifetime, Easing.InOutSine);
            blossom.RotateTo(blossom.Rotation + rotationDelta, lifetime, Easing.InOutSine);
            blossom.Delay(lifetime - 420).FadeOut(420, Easing.InQuad).Expire();
        }

        // Vermillion ember rising. Spawns BELOW the name and drifts up
        // past it — opposite vertical direction to the blossoms, so
        // together they create a sense of layered atmosphere (heat
        // rising, petals descending).
        private void emitEmber(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.05 + random.NextDouble() * 1.10) * parentSize.X;
            float startY = parentSize.Y * (0.85f + (float)random.NextDouble() * 0.30f);

            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.20f);
            float driftY = -parentSize.Y * (1.20f + (float)random.NextDouble() * 0.40f);

            float scale = ParticleScale(parentSize);
            float size = (2.8f + (float)random.NextDouble() * 1.8f) * scale;

            Color4 coreColour = random.NextDouble() < 0.6 ? vermillion_bright : vermillion_deep;

            var halo = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size * 2.2f),
                Colour = coreColour,
                Alpha = 0.22f,
            };

            var core = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size),
                Colour = coreColour,
                Alpha = 0.9f,
            };

            var ember = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { halo, core },
                Alpha = 0,
            };

            parent.Add(ember);

            double lifetime = 1400 + random.NextDouble() * 500;
            ember.FadeTo(1f, 220, Easing.OutQuad);
            ember.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutSine);
            ember.ScaleTo(0.55f, lifetime, Easing.OutQuad);
            ember.Delay(lifetime - 260).FadeOut(260, Easing.InQuad).Expire();
        }

        // Short gold spark — fast, narrow, vertical. Adds "crackling
        // heat" texture between the slow blossoms and embers without
        // dominating either.
        private void emitGoldSpark(Container parent, Vector2 parentSize, Random random)
        {
            float startX = (float)(-0.05 + random.NextDouble() * 1.10) * parentSize.X;
            float startY = parentSize.Y * (0.7f + (float)random.NextDouble() * 0.25f);

            float driftX = (float)((random.NextDouble() - 0.5) * parentSize.X * 0.10f);
            float driftY = -parentSize.Y * (0.6f + (float)random.NextDouble() * 0.4f);

            float scale = ParticleScale(parentSize);
            float length = (4f + (float)random.NextDouble() * 3f) * scale;

            Color4 colour = random.NextDouble() < 0.5 ? gold_warm : gold_pale;

            var head = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.BottomCentre,
                Width = 1.4f,
                Height = length * 0.35f,
                Colour = colour,
                Alpha = 0.95f,
            };

            var tail = new Box
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.TopCentre,
                Width = 1.0f,
                Height = length * 0.65f,
                Colour = colour,
                Alpha = 0.45f,
            };

            var spark = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { tail, head },
                Alpha = 0,
            };

            parent.Add(spark);

            double lifetime = 480 + random.NextDouble() * 280;
            spark.FadeTo(1f, 60, Easing.OutQuad);
            spark.MoveTo(new Vector2(startX + driftX, startY + driftY), lifetime, Easing.OutCubic);
            spark.ScaleTo(0.55f, lifetime, Easing.OutQuad);
            spark.Delay(lifetime - 140).FadeOut(140, Easing.InQuad).Expire();
        }

        // RARE signature beat. Faint vermillion torii-gate silhouette
        // blooming above (or to the side of) the name. The "moment that
        // declares the brand" — without it, the aura would just be
        // "warm sakura particles".
        private void emitGateFlash(Container parent, Vector2 parentSize, Random random)
        {
            // Position can be centred above or flanking the name. 50/50
            // pick gives some variety; flank positions add to the
            // "around the name" feel the user requested.
            float positionRoll = (float)random.NextDouble();
            float centerX, aboveY;
            if (positionRoll < 0.50f)
            {
                // Centred above.
                centerX = parentSize.X * (0.5f + (float)(random.NextDouble() - 0.5) * 0.10f);
                aboveY  = -parentSize.Y * (0.15f + (float)random.NextDouble() * 0.25f);
            }
            else if (positionRoll < 0.75f)
            {
                // Left flank.
                centerX = parentSize.X * (-0.15f + (float)random.NextDouble() * 0.08f);
                aboveY  = parentSize.Y * (0.30f + (float)random.NextDouble() * 0.30f);
            }
            else
            {
                // Right flank.
                centerX = parentSize.X * (1.05f + (float)random.NextDouble() * 0.08f);
                aboveY  = parentSize.Y * (0.30f + (float)random.NextDouble() * 0.30f);
            }

            float scale = ParticleScale(parentSize);
            // Bumped from 11-16px → 14-20px so when this signature beat
            // does fire, it lands as a proper "moment" rather than a
            // small ornament. Pairs with the increased spawn rate
            // (5% → 15%) — the gate is now both more frequent AND a
            // touch larger when it appears.
            float size = (14f + (float)random.NextDouble() * 6f) * scale;

            var gate = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Position = new Vector2(centerX, aboveY),
                Icon = FontAwesome.Solid.ToriiGate,
                Size = new Vector2(size),
                Colour = vermillion_bright,
                Alpha = 0,
                Scale = new Vector2(0.55f),
                Rotation = (float)((random.NextDouble() - 0.5) * 6),
            };

            parent.Add(gate);

            gate.FadeTo(0.48f, 280, Easing.OutQuad);
            gate.ScaleTo(1f, 620, Easing.OutBack);
            gate.Delay(680).FadeOut(740, Easing.InQuad).Expire();
        }

        /// <summary>
        /// Five-petal sakura blossom rendered from primitives so we
        /// don't need a baked image asset and the colours / scale stay
        /// fully controllable per-spawn. Each petal is a small Circle
        /// rotated radially around the centre; the centre carries a
        /// tiny warm-gold pollen dot that gives the blossom a focal
        /// point even at chat-row sizes.
        /// </summary>
        private partial class SakuraBlossom : CompositeDrawable
        {
            public SakuraBlossom(float petalSize, Color4 outerColour, Color4 innerColour)
            {
                AutoSizeAxes = Axes.Both;

                // Five petals at 72° intervals around the centre.
                // Each petal is a Circle stretched along its radial
                // axis (Height > Width) so the shape reads as an oval
                // petal pointing outward, not a round dot.
                const int petal_count = 5;
                float petalRadius = petalSize * 0.85f;

                var children = new List<Drawable>(petal_count + 1);

                for (int i = 0; i < petal_count; i++)
                {
                    float angleDeg = i * (360f / petal_count);
                    double angleRad = angleDeg * Math.PI / 180.0;
                    float cx = (float)Math.Sin(angleRad) * petalRadius * 0.55f;
                    float cy = -(float)Math.Cos(angleRad) * petalRadius * 0.55f;

                    children.Add(new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Position = new Vector2(cx, cy),
                        // Width = thinner axis, Height = longer axis so
                        // the petal points away from the blossom centre.
                        Size = new Vector2(petalSize * 0.85f, petalSize * 1.30f),
                        Rotation = angleDeg,
                        Colour = outerColour,
                        Alpha = 0.92f,
                    });

                    // Inner highlight — same shape, smaller + paler.
                    // Adds depth so a blossom doesn't read as a flat
                    // pink star.
                    children.Add(new Circle
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Position = new Vector2(cx, cy),
                        Size = new Vector2(petalSize * 0.45f, petalSize * 0.85f),
                        Rotation = angleDeg,
                        Colour = innerColour,
                        Alpha = 0.85f,
                    });
                }

                // Pollen centre. Warm yellow so it reads as a "real
                // flower centre" rather than another pink dot.
                children.Add(new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(petalSize * 0.55f),
                    Colour = new Color4(255, 220, 140, 255),
                    Alpha = 0.95f,
                });

                InternalChildren = children.ToArray();
            }
        }

        /// <summary>
        /// Persistent flanking ornament drawn permanently beside the
        /// username (NOT a transient particle). Two of these — one on
        /// each side of the name — are added by
        /// <see cref="CreateBackground"/> and live for the lifetime of
        /// the aura, breathing gently in opacity so they read as alive
        /// rather than stamped.
        ///
        /// Visual: a thin gold ring with a small vermillion torii gate
        /// glyph centred inside, like a stylised wax seal beside a name
        /// on an old document. Sized in fixed pixels (~13px) so it
        /// stays modest at profile-header sizes and reads clearly at
        /// chat-row sizes without depending on parent dimensions.
        /// </summary>
        private partial class FounderSeal : CompositeDrawable
        {
            public FounderSeal()
            {
                AutoSizeAxes = Axes.Both;
                // Start slightly under so the FadeIn animation in load()
                // has somewhere to ease up from — avoids a single-frame
                // flash on first display.
                Alpha = 0;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                // Modest pixel size. At chat rows (~13px name height)
                // the seal sits roughly name-height. At profile headers
                // (~50px name height) it's about a quarter the height,
                // which is exactly the "tasteful flanking ornament"
                // proportion we want — small enough not to fight the
                // name, distinct enough to be visible.
                const float seal_size = 13f;

                InternalChildren = new Drawable[]
                {
                    // Outer gold ring — the "wax seal" border. Uses the
                    // FontAwesome.Regular outline circle so we don't
                    // need to overlay two filled discs to fake a ring.
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Regular.Circle,
                        Size = new Vector2(seal_size),
                        Colour = gold_warm,
                        Alpha = 0.9f,
                    },
                    // Inner torii glyph — the brand mark stamped on
                    // the seal. Vermillion so it pops against the warm
                    // gold ring even under the additive blending the
                    // emitter forces.
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Icon = FontAwesome.Solid.ToriiGate,
                        Size = new Vector2(seal_size * 0.62f),
                        Colour = vermillion_bright,
                        Alpha = 0.95f,
                    },
                };

                // Gentle alpha breath. Range 0.55..0.95 over 4 seconds
                // each direction → 8-second full cycle. Slow enough to
                // never call attention to itself but ALWAYS subtly
                // signalling "this name is decorated".
                this.FadeTo(0.55f, 600, Easing.OutQuad);
                this.Delay(600).FadeTo(0.95f, 4000, Easing.InOutSine)
                               .Then().FadeTo(0.55f, 4000, Easing.InOutSine)
                               .Loop();
            }
        }
    }
}
