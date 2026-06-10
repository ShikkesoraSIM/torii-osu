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
    /// Mania Consul aura: chunky scrolling note bars in four lanes
    /// alongside the username, with bottom receptor flashes when each
    /// note lands. The Mania visual signature is "vertical scroll +
    /// flash on hit" — without the receptor flash the aura was just
    /// "vertical streaks" and didn't read as a rhythm game. This
    /// revision adds the flash, widens the bars so they're clearly
    /// notes (not threads), and pulls the four lanes OUTSIDE the
    /// username so the aura surrounds the name like a 4K playfield
    /// rather than overlapping the text.
    ///
    /// Three particle types:
    /// <list type="bullet">
    /// <item>Note bar (~85%). A short chunky vertical bar that
    ///       materialises above the name's vertical range, scrolls
    ///       down through it, and triggers a receptor flash when its
    ///       leading edge crosses the baseline.</item>
    /// <item>Receptor flash (~spawned by note completion). A bright
    ///       horizontal pulse at the bottom of the lane indicating
    ///       a "hit". Fired by the bar itself, not as an independent
    ///       random spawn — the flash always belongs to a specific
    ///       note's landing moment.</item>
    /// <item>Music-note glyph flash (~15%). A FontAwesome music note
    ///       blooming alongside one of the lanes. Sparse signature
    ///       beat that locks the aura's identity as "music / rhythm
    ///       game" beyond just bar particles.</item>
    /// </list>
    /// </summary>
    public class ManiaConsulAuraPreset : AuraPreset
    {
        public const string ID = "mania-consul";

        // 4K Mania palette: white outer lanes + blue inner lanes is
        // the canonical look. We add a vivid accent purple for the
        // music-note signature beat so it reads as distinct from the
        // bar stream.
        private static readonly Color4 note_white     = new Color4(245, 250, 255, 255);
        private static readonly Color4 note_blue      = new Color4(110, 170, 240, 255);
        private static readonly Color4 receptor_flash = new Color4(180, 220, 255, 255);
        private static readonly Color4 music_accent   = new Color4(170, 140, 240, 255);

        // Cool halo glow — Mania visuals are mostly monochrome with
        // accent on the notes themselves; halo stays subdued so it
        // doesn't compete with the brighter bar particles.
        private static readonly Color4 halo_cool      = new Color4(140, 175, 220, 255);

        // 4 lanes, positioned as a "playfield" alongside the username:
        // two lanes on the left flank, two on the right. We DON'T put
        // them across the centre of the name because the bars would
        // run through the text and reduce legibility. Lane X positions
        // are expressed as fractions of parentSize.X and recomputed
        // per spawn so they scale with the username width.
        //
        // Lane index → relative X (offsets from parent box). Negative
        // values land left of the username, > 1 values land right.
        private static readonly float[] lane_offsets = { -0.16f, -0.08f, 1.08f, 1.16f };

        public override string AuraId => ID;

        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-advisor" };

        public override IReadOnlyList<string>? RequiredPlaymodes { get; } = new[] { "mania" };

        public override int DefaultPriority => 55;

        // Mania charts are visually dense — the aura should feel
        // rhythmic. ~210ms between spawns gives roughly two notes per
        // lane per cycle, which reads as a busy 4K stream without
        // tanking framerate.
        public override double SpawnIntervalMs => 210;
        public override double SpawnJitterMs => 110;
        public override int MaxAlive => 12;

        public override Color4? GlowColour => halo_cool;

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            if (random.NextDouble() < 0.15)
            {
                emitMusicNote(parent, parentSize, random);
                return;
            }

            emitNoteBar(parent, parentSize, random);
        }

        // Dominant particle. Chunky vertical bar in one of the 4
        // lanes, scrolling down from above the username past the
        // baseline. Width is the visual change vs the previous
        // revision — bars were 1.5px which read as threads; now
        // they're proper note-shaped rectangles.
        private void emitNoteBar(Container parent, Vector2 parentSize, Random random)
        {
            int laneIndex = random.Next(lane_offsets.Length);
            float laneX = parentSize.X * lane_offsets[laneIndex];

            float scale = ParticleScale(parentSize);
            // Chunkier than v1. ~5px wide note at chat-row scale, scaling
            // with parent size. Reads as a clear note instead of a
            // pixel-thin streak.
            float barWidth = 4.5f * scale;
            float barHeight = parentSize.Y * 0.40f;

            // Outer lanes get white notes, inner lanes get blue —
            // matches the canonical 4K skin (W B B W).
            Color4 colour = (laneIndex == 0 || laneIndex == 3) ? note_white : note_blue;

            float startY = -parentSize.Y * 0.30f;
            float landY  =  parentSize.Y * 0.80f;
            float exitY  =  parentSize.Y * 1.30f;

            var bar = new Box
            {
                Origin = Anchor.TopCentre,
                Position = new Vector2(laneX, startY),
                Width = barWidth,
                Height = barHeight,
                Colour = colour,
                Alpha = 0,
                // Slight corner-rounding effect via clipping is too
                // expensive per particle; we accept the square ends as
                // a stylistic choice consistent with low-cost flat notes.
            };

            parent.Add(bar);

            const double scroll_time = 540;
            const double exit_time = 220;

            bar.FadeTo(0.92f, 80, Easing.OutQuad);
            // First leg: scroll from above the box to the receptor line.
            bar.MoveTo(new Vector2(laneX, landY), scroll_time, Easing.InQuad);
            // Second leg: continue past the receptor + fade. The note
            // "exiting" through the baseline reads as "passed through
            // the hit zone".
            bar.Delay(scroll_time).MoveTo(new Vector2(laneX, exitY), exit_time, Easing.InQuad);
            bar.Delay(scroll_time).FadeOut(exit_time, Easing.InQuad).Expire();

            // Trigger receptor flash at the moment the bar lands.
            emitReceptorFlash(parent, parentSize, laneX, landY, colour, scroll_time);
        }

        // Companion particle to a landing note. A short horizontal
        // bright pulse at the lane's receptor line — the visual
        // feedback that makes the falling-note pattern read as "this
        // is a rhythm game" instead of "vertical lines drifting".
        //
        // Scheduled with the parent's Delay() so the flash fires
        // exactly when the bar reaches its receptor — kept inside the
        // same parent container as the bar so it shares lifecycle and
        // cleans up automatically.
        private void emitReceptorFlash(Container parent, Vector2 parentSize, float laneX, float landY, Color4 baseColour, double triggerDelay)
        {
            float scale = ParticleScale(parentSize);
            float flashWidth = 14f * scale;
            float flashHeight = 3f * scale;

            var flash = new Box
            {
                Origin = Anchor.Centre,
                Position = new Vector2(laneX, landY),
                Width = flashWidth,
                Height = flashHeight,
                Colour = receptor_flash,
                Alpha = 0,
            };

            parent.Add(flash);

            flash.Delay(triggerDelay).FadeTo(0.95f, 60, Easing.OutQuad);
            flash.Delay(triggerDelay).ScaleTo(new Vector2(1.4f, 1f), 120, Easing.OutQuad);
            flash.Delay(triggerDelay + 70).FadeOut(220, Easing.OutQuad).Expire();
        }

        // Rare music-note signature beat. A FontAwesome music glyph
        // pops in slightly outside one of the lane stacks. Reinforces
        // the "rhythm game" identity beyond the geometric note bars.
        private void emitMusicNote(Container parent, Vector2 parentSize, Random random)
        {
            // Snap to the far edge of the leftmost or rightmost lane
            // (whichever side) so the music note sits "above" the
            // stack rather than over the text.
            bool leftSide = random.Next(2) == 0;
            float centerX = parentSize.X * (leftSide ? -0.22f : 1.22f) + (float)(random.NextDouble() * parentSize.X * 0.05f);
            float centerY = parentSize.Y * (0.20f + (float)random.NextDouble() * 0.30f);

            float scale = ParticleScale(parentSize);
            float size = (8f + (float)random.NextDouble() * 3f) * scale;

            var halo = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Music,
                Size = new Vector2(size * 1.6f),
                Colour = music_accent,
                Alpha = 0.22f,
            };

            var note = new SpriteIcon
            {
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Music,
                Size = new Vector2(size),
                Colour = music_accent,
                Alpha = 0.92f,
            };

            var bundle = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(centerX, centerY),
                Children = new Drawable[] { halo, note },
                Alpha = 0,
                Scale = new Vector2(0.6f),
                Rotation = (float)((random.NextDouble() - 0.5) * 16),
            };

            parent.Add(bundle);

            bundle.FadeTo(1f, 180, Easing.OutQuad);
            bundle.ScaleTo(1f, 340, Easing.OutBack);
            bundle.Delay(520).FadeOut(560, Easing.InQuad).Expire();
        }
    }
}
