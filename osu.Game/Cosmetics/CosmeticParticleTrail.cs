// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osuTK;

namespace osu.Game.Cosmetics
{
    /// <summary>
    /// A cursor trail made of actual shaped drawables (stars, hearts, petals,
    /// bubbles, sparkles, snow, flames, notes...) spawned along the cursor path.
    /// This is what gives each cosmetic its OWN form and motion instead of a
    /// recoloured dot.
    ///
    /// The shape comes from <see cref="ParticleFactory"/>; the motion (drift,
    /// spin, lifetime, scale curve, spacing) is set per cosmetic. The path is
    /// interpolated so fast cursor moves still lay an even stream.
    /// </summary>
    public partial class CosmeticParticleTrail : CompositeDrawable, IRequireHighFrequencyMousePosition, ICosmeticTrail
    {
        /// <summary>Builds one styled particle for an emission. The int is the
        /// running emission index (use it to vary colour, e.g. rainbow).</summary>
        public Func<int, Drawable> ParticleFactory { get; set; }

        /// <summary>Pixels of cursor travel between emissions (lower = denser).</summary>
        public float SpawnInterval { get; set; } = 16f;

        /// <summary>How long (ms) a particle lives before it has faded out.</summary>
        public double ParticleLifetime { get; set; } = 700;

        /// <summary>Base drift applied over a particle's life (e.g. up for hearts,
        /// down for snow).</summary>
        public Vector2 Drift { get; set; } = new Vector2(0, -12);

        /// <summary>Random +/- spread added to the drift per particle.</summary>
        public float DriftJitter { get; set; } = 10f;

        /// <summary>Degrees a particle rotates over its life.</summary>
        public float SpinDegrees { get; set; }

        public float StartScale { get; set; } = 1f;
        public float EndScale { get; set; } = 0.5f;

        /// <summary>Hard cap on alive particles, to bound GPU/CPU work.</summary>
        public int MaxAlive { get; set; } = 160;

        private float? baseInterval;
        private double? baseLifetime;

        private int spawnIndex;
        private Vector2? lastPosition;
        private readonly InputResampler resampler = new InputResampler();
        private readonly Random random = new Random();

        public CosmeticParticleTrail()
        {
            RelativeSizeAxes = Axes.Both;
            // Particles glow into each other; catalog can override to normal.
            Blending = BlendingParameters.Additive;
        }

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            AddTrail(e.ScreenSpaceMousePosition);
            return base.OnMouseMove(e);
        }

        public void Drive(Vector2 screenSpacePosition) => AddTrail(screenSpacePosition);

        public void SetLengthMultiplier(float multiplier)
        {
            baseLifetime ??= ParticleLifetime;
            ParticleLifetime = baseLifetime.Value * multiplier;
        }

        public void SetDensityMultiplier(float multiplier)
        {
            baseInterval ??= SpawnInterval;
            SpawnInterval = baseInterval.Value / Math.Max(0.05f, multiplier);
        }

        private void AddTrail(Vector2 screenSpacePosition)
        {
            Vector2 position = ToLocalSpace(screenSpacePosition);

            if (!lastPosition.HasValue)
            {
                lastPosition = position;
                resampler.AddPosition(position);
                return;
            }

            foreach (Vector2 pos2 in resampler.AddPosition(position))
            {
                Vector2 pos1 = lastPosition.Value;
                Vector2 diff = pos2 - pos1;
                float distance = diff.Length;

                if (distance <= 0)
                    continue;

                Vector2 direction = diff / distance;

                for (float d = SpawnInterval; d < distance; d += SpawnInterval)
                    spawn(pos1 + direction * d);

                lastPosition = pos2;
            }
        }

        private void spawn(Vector2 localPosition)
        {
            if (ParticleFactory == null || InternalChildren.Count >= MaxAlive)
                return;

            Drawable particle = ParticleFactory(spawnIndex++);

            particle.Anchor = Anchor.TopLeft;
            particle.Origin = Anchor.Centre;
            particle.Position = localPosition;
            particle.Alpha = 0;
            particle.Scale = new Vector2(StartScale);

            float jitterX = (float)(random.NextDouble() - 0.5) * DriftJitter * 2;
            float jitterY = (float)(random.NextDouble() - 0.5) * DriftJitter;
            Vector2 target = localPosition + new Vector2(Drift.X + jitterX, Drift.Y + jitterY);

            AddInternal(particle);

            particle.FadeTo(1f, 90, Easing.OutQuad);
            particle.MoveTo(target, ParticleLifetime, Easing.OutSine);
            particle.ScaleTo(EndScale, ParticleLifetime, Easing.OutQuad);

            if (SpinDegrees != 0)
                particle.RotateTo(particle.Rotation + SpinDegrees, ParticleLifetime, Easing.InOutSine);

            particle.Delay(ParticleLifetime - 160).FadeOut(160, Easing.InQuad).Expire();
        }
    }
}
