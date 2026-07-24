// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osuTK;
using osuTK.Graphics;

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

        /// <summary>Recolour every spawned particle with <see cref="ParticleTint"/>
        /// instead of its shape's built-in colour, so the same shape (a star, a
        /// heart) can be any colour the author picks. Off = keep the shape's own
        /// colour.</summary>
        public bool UseParticleTint { get; set; }

        public Color4 ParticleTint { get; set; } = Color4.White;

        /// <summary>A user-supplied PNG (base64) used as the particle sprite instead
        /// of a built-in shape — this is what lets creators "upload" their own
        /// particle. The image travels INSIDE the cosmetic definition so it stays
        /// portable (shareable / contest-ready). Decoded once at load with strict
        /// size caps; if it's missing/too big/invalid the built-in shape is kept.
        /// It's plain data (no code), so it's safe to load from community files.</summary>
        public string CustomImage { get; set; }

        /// <summary>Hard cap on alive particles, to bound GPU/CPU work.</summary>
        public int MaxAlive { get; set; } = 160;

        // topes para la imagen custom (data de comunidad): lado max y largo max del base64.
        private const int custom_max_dimension = 256;
        private const int custom_max_base64_len = 700_000; // ~500KB decodificados

        // burst de click: agranda las particulas que se emiten durante el pop (1 = neutro).
        private float clickExpansion = 1f;

        // Particles need enough life to fade in (90ms) and out (160ms), so their
        // length floor is higher than the dot/ribbon one. They are inherently
        // chunkier than a line trail, so a slightly longer minimum is expected.
        private const double particle_length_floor_ms = 200;

        private float? baseInterval;
        private double? baseLifetime;
        private float? baseStartScale;
        private float? baseEndScale;

        private int spawnIndex;
        private Vector2? lastPosition;
        private float distanceCarry;
        private readonly Random random = new Random();

        public CosmeticParticleTrail()
        {
            RelativeSizeAxes = Axes.Both;
            // Particles glow into each other; catalog can override to normal.
            Blending = BlendingParameters.Additive;
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer)
        {
            // si vino una imagen custom valida, se convierte en la forma de la particula (sprite) y
            // pisa el ParticleFactory de la forma built-in. es 100% data (un PNG), sin code-exec.
            if (string.IsNullOrEmpty(CustomImage) || CustomImage.Length > custom_max_base64_len)
                return;

            try
            {
                byte[] bytes = Convert.FromBase64String(CustomImage);

                using var stream = new MemoryStream(bytes);
                var upload = new TextureUpload(stream);

                if (upload.Width <= 0 || upload.Height <= 0 || upload.Width > custom_max_dimension || upload.Height > custom_max_dimension)
                    return;

                var texture = renderer.CreateTexture(upload.Width, upload.Height);
                texture.SetData(upload);

                // normalizamos el lado mayor a ~26px preservando el aspecto, asi una imagen grande no
                // entra gigante (despues los sliders Start/End scale la ajustan encima).
                float longest = Math.Max(texture.DisplayWidth, texture.DisplayHeight);
                float scale = longest > 0 ? 26f / longest : 1f;
                var drawSize = new Vector2(texture.DisplayWidth * scale, texture.DisplayHeight * scale);

                ParticleFactory = _ => new Sprite
                {
                    Texture = texture,
                    Size = drawSize,
                    Origin = Anchor.Centre,
                };
            }
            catch
            {
                // base64 / imagen invalida -> nos quedamos con la forma built-in (ParticleFactory ya seteada).
            }
        }

        private bool inputActive = true;
        private bool paused;

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => inputActive;

        public void SetInputActive(bool active) => inputActive = active;

        public void SetClickExpansion(float scale) => clickExpansion = Math.Max(0.1f, scale);

        public void SetPaused(bool paused)
        {
            if (this.paused == paused)
                return;

            this.paused = paused;

            if (paused)
            {
                // Freeze every live particle where it is: clear its drift/fade/
                // expire transforms AND pin its lifetime, so it STAYS (a static
                // snapshot) instead of expiring and vanishing. Clearing the
                // transforms alone left the Expire-set LifetimeEnd in place, so
                // the particles still disappeared. Preview-only; the equipped
                // trail never pauses.
                foreach (var c in InternalChildren)
                {
                    c.ClearTransforms();
                    c.LifetimeEnd = double.MaxValue;
                }
            }
            else
            {
                // Resuming: drop the frozen particles so fresh ones spawn cleanly.
                ClearInternal();
            }
        }

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            if (inputActive)
                AddTrail(e.ScreenSpaceMousePosition);
            return base.OnMouseMove(e);
        }

        public void Drive(Vector2 screenSpacePosition) => AddTrail(screenSpacePosition);

        public void SetLengthScale(float scale)
        {
            baseLifetime ??= ParticleLifetime;
            scale = Math.Clamp(scale, 0f, 1.5f);
            ParticleLifetime = scale <= 1f
                ? particle_length_floor_ms + (baseLifetime.Value - particle_length_floor_ms) * scale
                : baseLifetime.Value * scale;
        }

        public void SetDensityMultiplier(float multiplier)
        {
            baseInterval ??= SpawnInterval;
            SpawnInterval = baseInterval.Value / Math.Max(0.05f, multiplier);
        }

        public void SetSizeMultiplier(float multiplier)
        {
            baseStartScale ??= StartScale;
            baseEndScale ??= EndScale;
            multiplier = Math.Max(0.1f, multiplier);
            StartScale = baseStartScale.Value * multiplier;
            EndScale = baseEndScale.Value * multiplier;
        }

        public void Reset()
        {
            // Leave live particles to fade out naturally; just break path
            // continuity so the next move doesn't draw a streak across the gap.
            lastPosition = null;
            distanceCarry = 0;
        }

        private void AddTrail(Vector2 screenSpacePosition)
        {
            Vector2 position = ToLocalSpace(screenSpacePosition);

            if (!lastPosition.HasValue)
            {
                lastPosition = position;
                return;
            }

            Vector2 diff = position - lastPosition.Value;
            float distance = diff.Length;
            if (distance <= 0)
                return;

            // Accumulate leftover travel across frames so particles emit evenly
            // even when each frame's move is smaller than the spawn interval.
            Vector2 direction = diff / distance;
            float traveled = 0;
            while (distanceCarry + (distance - traveled) >= SpawnInterval)
            {
                float step = SpawnInterval - distanceCarry;
                traveled += step;
                spawn(lastPosition.Value + direction * traveled);
                distanceCarry = 0;
            }

            distanceCarry += distance - traveled;
            lastPosition = position;
        }

        private void spawn(Vector2 localPosition)
        {
            if (paused || ParticleFactory == null || InternalChildren.Count >= MaxAlive)
                return;

            // el factory de la forma es una primitiva safe, pero si una forma custom/rota tira, que
            // se descarte la particula en vez de matar el frame entero.
            Drawable particle;
            try
            {
                particle = ParticleFactory(spawnIndex++);
            }
            catch
            {
                return;
            }

            if (particle == null)
                return;

            particle.Anchor = Anchor.TopLeft;
            particle.Origin = Anchor.Centre;
            particle.Position = localPosition;
            particle.Alpha = 0;
            particle.Scale = new Vector2(StartScale * clickExpansion);

            // recolor opcional: la misma forma (estrella/corazon) en el color que el autor elija.
            if (UseParticleTint)
                particle.Colour = ParticleTint;

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
