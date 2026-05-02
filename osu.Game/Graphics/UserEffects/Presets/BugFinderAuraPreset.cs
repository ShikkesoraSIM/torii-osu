// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserEffects.Presets
{
    /// <summary>
    /// Bug-finder aura: a couple of tiny mint-coloured bug glyphs slowly
    /// crawl across the bottom edge of the username, occasionally bobbing
    /// like they're inspecting something. Designed as the lowkey,
    /// recognition-tier free aura for community members who reported real
    /// bugs — the visual goal is "you'll spot it if you look, but it
    /// never competes with the brighter elite auras (admin embers,
    /// supporter hearts) when the same user owns those too".
    /// </summary>
    /// <remarks>
    /// Tuning rationale (kept lightweight on purpose because many users
    /// will own this — see <see cref="MaxAlive"/> + slow spawn rate):
    /// <list type="bullet">
    ///   <item>Bugs spawn near the bottom-left and crawl rightward
    ///         (with vertical jitter) instead of drifting in 2D — that
    ///         "pacing along the baseline" reads as a deliberate
    ///         observer rather than a free-floating particle effect.</item>
    ///   <item>Glow uses the same mint family as the particles so the
    ///         halo + bugs read as one cohesive idea, not two
    ///         unrelated effects layered on top of each other.</item>
    ///   <item>Higher <see cref="DefaultPriority"/> than admin / dev /
    ///         mod / supporter so the more elite groups always win the
    ///         tiebreak — bug-finder is the "starter" aura that gracefully
    ///         hides behind any other ownership the user has.</item>
    /// </list>
    /// </remarks>
    public class BugFinderAuraPreset : AuraPreset
    {
        public const string ID = "bug-finder-bugs";

        // Mint / debugger-console palette. Three nearby tones so a cluster
        // doesn't look uniform. All chosen with high luminance so the
        // glyphs read against both light and dark backgrounds (chat lines
        // can be either depending on theme).
        private static readonly Color4[] bug_palette =
        {
            new Color4(140, 224, 197, 255), // mint base
            new Color4(176, 235, 210, 255), // pale mint
            new Color4(110, 200, 175, 255), // deeper teal-mint
        };

        public override string AuraId => ID;

        // Server-side group identifier the bug-finder badge maps to.
        // Granted manually by admins (or eventually automated via a
        // "your bug report was accepted" flow). Same identifier needs
        // to exist in the server's torii_groups catalog for the picker
        // to surface this aura — without it the resolver still works
        // via APIUser.EquippedAura, the preset just won't appear in the
        // settings list.
        public override IReadOnlyList<string> OwningGroupIdentifiers { get; } = new[] { "torii-bug-finder" };

        // High default priority = lowest precedence in the group fallback
        // tiebreak. If a user is BOTH a bug-finder AND a supporter / admin /
        // dev / mod, the elite preset wins automatically when no explicit
        // EquippedAura has been picked. Keeps the "lowkey" promise: this
        // aura never overrides something flashier the user already owns.
        public override int DefaultPriority => 80;

        // Sparse + slow on purpose. Bug-finder is intended to be common,
        // so we err on the side of "barely noticeable" — many simultaneous
        // bug-finder usernames in a chat row should NOT add up to a
        // distracting swarm.
        public override double SpawnIntervalMs => 620;
        public override double SpawnJitterMs => 320;
        public override int MaxAlive => 4;

        // Mint glow hugging the username letters. Pulled from the bug
        // palette base so glow + bugs read as one cohesive effect.
        public override Color4? GlowColour => bug_palette[0];

        public override void EmitParticle(Container parent, Vector2 parentSize, Random random)
        {
            // Spawn slightly LEFT of the username box and walk rightward.
            // Y is biased toward the bottom edge (60..100% of height) so
            // bugs read as "crawling along the baseline" rather than
            // free-floating particles — the chosen visual metaphor is
            // an observer pacing along the line, looking up occasionally.
            float startX = (float)(-0.10 + random.NextDouble() * 0.10) * parentSize.X;
            float startY = (float)(0.60 + random.NextDouble() * 0.40) * parentSize.Y;

            // Crawl the full username width plus a small overshoot. The
            // overshoot is what makes the bug "exit stage right" rather
            // than abruptly fading mid-name, which feels broken.
            float endX = startX + parentSize.X * (1.05f + (float)random.NextDouble() * 0.20f);

            // Tiny vertical wander as the bug crawls — keeps it feeling
            // alive without it actually leaving the baseline.
            float endY = startY + (float)((random.NextDouble() - 0.5) * parentSize.Y * 0.18f);

            float size = (5.0f + (float)random.NextDouble() * 2.0f) * ParticleScale(parentSize);
            Color4 colour = bug_palette[random.Next(bug_palette.Length)];

            // Soft halo behind the bug. Lower opacity than the goof aura's
            // halo (0.12 vs 0.16) because there are two glyphs side by
            // side at most and we don't want a doubled glow to read as
            // a smear of mint behind the username.
            var halo = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Bug,
                Size = new Vector2(size * 1.5f),
                Colour = colour,
                Alpha = 0.12f,
            };

            var bug = new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Icon = FontAwesome.Solid.Bug,
                Size = new Vector2(size),
                Colour = colour,
                Alpha = 0.78f,
                // Slight initial tilt — bugs aren't perfectly upright.
                Rotation = (float)((random.NextDouble() - 0.5) * 30),
            };

            var particle = new Container
            {
                AutoSizeAxes = Axes.Both,
                Position = new Vector2(startX, startY),
                Children = new Drawable[] { halo, bug },
                Alpha = 0,
            };

            parent.Add(particle);

            // Crawl pace: ≈ 2.4s left-to-right plus a touch of randomness.
            // Slow enough to read as "looking around" rather than running.
            double lifetime = 2200 + random.NextDouble() * 700;

            particle.FadeTo(1f, 280, Easing.OutQuad);
            particle.MoveTo(new Vector2(endX, endY), lifetime, Easing.InOutSine);

            // Subtle wobble while crawling — emulates the bug's gait
            // without becoming a noticeable bounce. ±2px Y over 600ms.
            bug.Loop(t => t
                .MoveToOffset(new Vector2(0, -2f), 600, Easing.InOutSine)
                .Then()
                .MoveToOffset(new Vector2(0, 2f), 600, Easing.InOutSine));

            // Tiny scale pulse so the glyph feels chibi-alive rather than
            // a static decal sliding along.
            bug.ScaleTo(1.06f, 700, Easing.InOutSine).Then().ScaleTo(1f, 700, Easing.InOutSine).Loop();

            particle.Delay(lifetime - 380).FadeOut(380, Easing.OutQuad).Expire();
        }
    }
}
