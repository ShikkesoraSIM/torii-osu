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
    /// Taiko Consul aura: Don and Kat hits ringing around the username
    /// with the occasional taiko-drum glyph flash. The Don/Kat ripple
    /// pattern is the visual core (anyone who plays Taiko reads
    /// "red filled circle = Don, blue ring = Kat" instantly), and the
    /// rare drum flash is the identity hook that turns the aura from
    /// "two-coloured circles" into "this is specifically Taiko".
    ///
    /// Three particle types:
    /// <list type="bullet">
    /// <item>Don ripple (~50%). Red filled circle that bursts open at
    ///       its spawn point and emits a single expanding red ring —
    ///       mimicking the impact ripple a Don note produces on hit.</item>
    /// <item>Kat ripple (~45%). Same envelope but blue, and the core
    ///       is a ring instead of a filled disc — same rim/centre
    ///       distinction Taiko uses to differentiate the two notes.</item>
    /// <item>Drum flash (~5%). Vermillion-ish drum glyph appearing near
    ///       the side of the username, blooming + dissolving. Sparse
    ///       so it stays a signature beat rather than a wallpaper.</item>
    /// </list>
    ///
    /// All particles spawn AROUND the username (positions can land
    /// outside the bounding box) so the aura reads as an atmosphere
    /// surrounding the player rather than something painted on the
    /// letters.
    /// </summary>
    public class TaikoConsulAuraPreset : AuraPreset
    {
        public const string ID = "taiko-consul";

        // Canonical Taiko don red + kat blue, pulled from the gameplay
        // skin palette so the colour association is immediate.
        private static readonly Color4 don_red       = new Color4(244, 95, 88, 255);
        private static readonly Color4 don_red_glow  = new Color4(255, 130, 120, 255);
        private static readonly Color4 kat_blue      = new Color4(90, 168, 248, 255);
        private static readonly Color4 kat_blue_glow = new Color4(150, 200, 255, 255);

        // Halo glow blends warm toward Don red since Don is the
        // structural backbone of most Taiko patterns. Slight nudge,
        // not a wash.
        private static readonly Color4 halo_warm = new Color4(220, 140, 150, 255);

        // Don/Kat alternation — strict pattern with occasional swap so
        // the visible cadence reads as a real drumstroke, not a
        // metronome. Threadsafe-by-context: EmitParticle is called on
        // the update thread for a single aura container.
        private bool nextIsDon = true;

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-advisor" };

        public override IReadOnlyList<string>? RequiredPlaymodes { get; } = new[] { "taiko" };

        public override int DefaultPriority => 55;

        // ~300ms between hits — comfortable mid-tempo drum feel. Fast
        // enough to read as "rhythm", slow enough to not turn into a
        // particle storm.
        public override double SpawnIntervalMs => 300;
        public override double SpawnJitterMs => 110;
        public override int MaxAlive => 8;

        public override Color4? GlowColour => halo_warm;

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            // 5% chance for the drum-flash signature beat. The
            // remaining 95% alternates Don/Kat (with a 20% swap chance
            // to avoid metronomic regularity).
            if (random.NextDouble() < 0.05)
            {
                emitDrumFlash(parent, parentSize, random);
                return;
            }

            bool emitDon = nextIsDon;
            if (random.NextDouble() < 0.20)
                emitDon = !emitDon;
            nextIsDon = !emitDon;

            if (emitDon)
                emitDonRipple(parent, parentSize, random);
            else
                emitKatRipple(parent, parentSize, random);
        }

        // Don: red filled disc + expanding red ring (the "ripple" that
        // makes the hit read as a drum-centre impact rather than a
        // static dot). Positions span beyond the username bounding box
        // so hits land around the name, not only inside it.
        private void emitDonRipple(Container parent, Vector2 parentSize, Random random)
        {
            float scale = ParticleScale(parentSize);
            float discSize = (6.5f + (float)random.NextDouble() * 2.5f) * scale;
            float ringMaxSize = discSize * 3.8f;

            float centerX = (float)(-0.10 + random.NextDouble() * 1.20) * parentSize.X;
            float centerY = parentSize.Y * (0.55f + (float)random.NextDouble() * 0.40f);

            // Outer ripple ring — expands outward and fades.
            var ripple = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(discSize * 1.2f),
                Colour = don_red_glow,
                Alpha = 0.0f,
            };

            // Soft glow halo behind the disc — gives the hit weight.
            var halo = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(discSize * 1.8f),
                Colour = don_red,
                Alpha = 0.32f,
            };

            // Filled centre disc — the Don hit itself.
            var core = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(discSize),
                Colour = don_red,
                Alpha = 1f,
            };

            var hit = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(centerX, centerY),
                Children = new Drawable[] { ripple, halo, core },
                Alpha = 0,
                Scale = new Vector2(0.7f),
            };

            parent.Add(hit);

            const double attack = 90;
            const double sustain = 130;
            const double release = 360;

            // Snap attack, brief sustain, slow release.
            hit.FadeTo(1f, attack, Easing.OutQuad);
            hit.ScaleTo(1.1f, attack, Easing.OutBack)
               .Then().ScaleTo(0.95f, release, Easing.InQuad);

            // Ripple ring fires immediately on the hit, expands +
            // fades. The "thump" you visually feel.
            ripple.FadeTo(0.6f, attack, Easing.OutQuad);
            ripple.ResizeTo(new Vector2(ringMaxSize), attack + sustain + release, Easing.OutCubic);
            ripple.Delay(attack + sustain).FadeOut(release, Easing.OutQuad);

            hit.Delay(attack + sustain).FadeOut(release, Easing.InQuad).Expire();
        }

        // Kat: blue ring + expanding blue ring. Same envelope as Don
        // but the core is a ring (rim-hit metaphor), and the colour
        // sits cool. Spawn Y biased UP relative to Don's down so the
        // visible pattern alternates positionally too — adds rhythmic
        // texture beyond just colour.
        private void emitKatRipple(Container parent, Vector2 parentSize, Random random)
        {
            float scale = ParticleScale(parentSize);
            float ringSize = (8f + (float)random.NextDouble() * 2.5f) * scale;
            float rippleMaxSize = ringSize * 3.5f;

            float centerX = (float)(-0.10 + random.NextDouble() * 1.20) * parentSize.X;
            float centerY = parentSize.Y * (0.05f + (float)random.NextDouble() * 0.40f);

            // Expanding ripple ring.
            var ripple = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Regular.Circle,
                Size = new Vector2(ringSize * 1.1f),
                Colour = kat_blue_glow,
                Alpha = 0.0f,
            };

            // Glow disc behind the ring — fills the ring's interior
            // with a soft blue so the ring doesn't look hollow against
            // dark backgrounds.
            var halo = new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(ringSize * 1.5f),
                Colour = kat_blue,
                Alpha = 0.25f,
            };

            // Core: ring (outline) — the rim-hit silhouette.
            var ring = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Regular.Circle,
                Size = new Vector2(ringSize),
                Colour = kat_blue,
                Alpha = 1f,
            };

            var hit = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(centerX, centerY),
                Children = new Drawable[] { ripple, halo, ring },
                Alpha = 0,
                Scale = new Vector2(0.7f),
            };

            parent.Add(hit);

            const double attack = 90;
            const double sustain = 130;
            const double release = 360;

            hit.FadeTo(1f, attack, Easing.OutQuad);
            hit.ScaleTo(1.1f, attack, Easing.OutBack)
               .Then().ScaleTo(0.95f, release, Easing.InQuad);

            ripple.FadeTo(0.55f, attack, Easing.OutQuad);
            ripple.ResizeTo(new Vector2(rippleMaxSize), attack + sustain + release, Easing.OutCubic);
            ripple.Delay(attack + sustain).FadeOut(release, Easing.OutQuad);

            hit.Delay(attack + sustain).FadeOut(release, Easing.InQuad).Expire();
        }

        // Rare drum glyph flash. Anchored to the left or right of the
        // username so it reads as "the taiko drum sitting next to the
        // player" — sparse so it remains a signature beat. Uses
        // FontAwesome.Solid.Drum (which renders as a side-on taiko-
        // shaped drum) tinted in the same warm halo tone as the
        // ambient glow, so the drum belongs to the aura rather than
        // looking like a foreign icon dropped in.
        private void emitDrumFlash(Container parent, Vector2 parentSize, Random random)
        {
            // Coin-flip: left flank or right flank of the username.
            bool leftSide = random.Next(2) == 0;
            float centerX = leftSide
                ? parentSize.X * -0.18f + (float)(random.NextDouble() * parentSize.X * 0.05f)
                : parentSize.X * 1.18f + (float)(random.NextDouble() * parentSize.X * 0.05f);
            float centerY = parentSize.Y * (0.35f + (float)random.NextDouble() * 0.30f);

            float scale = ParticleScale(parentSize);
            float size = (12f + (float)random.NextDouble() * 4f) * scale;

            // Soft warm halo behind the drum so it pops on dark
            // backgrounds without harsh edges.
            var halo = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Drum,
                Size = new Vector2(size * 1.5f),
                Colour = don_red_glow,
                Alpha = 0.20f,
            };

            var drum = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Drum,
                Size = new Vector2(size),
                Colour = don_red,
                Alpha = 0.92f,
            };

            var flash = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(centerX, centerY),
                Children = new Drawable[] { halo, drum },
                Alpha = 0,
                Scale = new Vector2(0.55f),
                // Slight tilt so the drum reads as "set down at an
                // angle" rather than a stamped decal.
                Rotation = leftSide ? -8f : 8f,
            };

            parent.Add(flash);

            flash.FadeTo(1f, 220, Easing.OutQuad);
            flash.ScaleTo(1f, 360, Easing.OutBack);
            flash.Delay(540).FadeOut(620, Easing.InQuad).Expire();
        }
    }
}
