// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;

namespace osu.Game.Graphics.UserEffects
{
    /// <summary>
    /// Drives particle emission for a single <see cref="AuraPreset"/>.
    ///
    /// Sits behind the username text (drawn first → renders underneath), fills
    /// the parent's bounding box, and asks the preset to emit a new particle
    /// every <c>SpawnIntervalMs ± SpawnJitterMs</c> while it has fewer than
    /// <c>MaxAlive</c> drawable particles.
    ///
    /// Optionally hosts a single "background" drawable from the preset (e.g. a
    /// pulsing halo) that's drawn underneath the particles and excluded from
    /// the alive-count cap so it never gets evicted to make room for sparks.
    ///
    /// Uses additive blending so the layered particles read as a single soft
    /// glow rather than a wall of opaque sprites — cheap on the GPU and lets
    /// each preset get away without per-particle blur.
    /// </summary>
    public partial class ParticleAuraEmitter : Container
    {
        private readonly AuraPreset preset;
        private readonly Random random = new Random();

        private double nextSpawnTime;
        private Drawable? background;

        public ParticleAuraEmitter(AuraPreset preset)
        {
            this.preset = preset;

            // Particles glow into each other and into the underlying name. Saves
            // every preset from needing its own BufferedContainer-with-blur.
            Blending = BlendingParameters.Additive;

            // Pixel-bounded — particles can drift slightly outside the username
            // box (looks more natural) but we don't want a giant invisible
            // hitbox blocking input.
            Masking = false;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            background = preset.CreateBackground();
            if (background != null)
            {
                // Background fills the same area as the emitter and lives
                // underneath every spawned particle.
                background.RelativeSizeAxes = Axes.Both;
                background.Anchor = Anchor.Centre;
                background.Origin = Anchor.Centre;
                Add(background);
            }
        }

        protected override void Update()
        {
            base.Update();

            // Skip work entirely when not visible (e.g. user is in a different
            // overlay tab, off-screen panel, or aura was disabled in settings).
            if (!IsPresent || Alpha <= 0)
                return;

            // Wait for first layout pass — without a real DrawSize the preset
            // has nothing meaningful to position particles within.
            if (DrawWidth <= 0 || DrawHeight <= 0)
                return;

            if (Time.Current < nextSpawnTime)
                return;

            // Hard cap on alive particles. Children naturally expire and remove
            // themselves, so we only need to gate spawning. Background drawable
            // (if any) is excluded from the count so it never blocks emission.
            int particleCount = background != null ? Children.Count - 1 : Children.Count;
            if (particleCount < preset.MaxAlive)
                preset.EmitParticle(this, new Vector2(DrawWidth, DrawHeight), random);

            nextSpawnTime = Time.Current
                            + preset.SpawnIntervalMs
                            + random.NextDouble() * preset.SpawnJitterMs;
        }
    }
}
