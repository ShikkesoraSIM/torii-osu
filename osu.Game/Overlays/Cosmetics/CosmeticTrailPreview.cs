// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.PolygonExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Cosmetics;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>
    /// A live preview of a cursor-trail cosmetic that drives a synthetic cursor
    /// so the trail draws itself without real mouse input. The motion is a flat
    /// horizontal sweep (not a tight circle) so ribbons read as a clean flowing
    /// band instead of fanning into a disc.
    /// </summary>
    public partial class CosmeticTrailPreview : Container
    {
        private readonly CosmeticTrailDefinition def;
        private readonly float speed;
        private Drawable trailDrawable;
        private ICosmeticTrail trail;
        private bool wasAnimating = true;

        /// <summary>If set, the trail only animates while this drawable's screen
        /// quad overlaps ours. The store grid points every card's preview at the
        /// scroll viewport so off-screen cards go quiet (otherwise ~35 live
        /// trails would run at once and lag). Null = always animate (detail
        /// panel, where there's only one).</summary>
        public Drawable AnimationViewport { get; set; }

        public CosmeticTrailPreview(CosmeticTrailDefinition def, float speed = 1f)
        {
            this.def = def;
            this.speed = speed;
            Masking = true;
            CornerRadius = BriefingTheme.CornerSm;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            trailDrawable = def.Create();
            trail = trailDrawable as ICosmeticTrail;

            // Previews are small; a heavy particle trail (e.g. Galaxy at 220
            // alive) is overkill here and is the main grid-lag culprit. Trim it
            // hard for the preview only (the equipped trail keeps its full cap).
            if (trailDrawable is CosmeticParticleTrail particles)
            {
                particles.MaxAlive = Math.Min(particles.MaxAlive, 36);
                particles.SpawnInterval *= 1.4f;
            }

            InternalChildren = new[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(14, 14, 22, 255) },
                trailDrawable,
            };
        }

        public void ApplyCustomisation(float length, float density, float size)
        {
            trail?.SetLengthScale(length);
            trail?.SetDensityMultiplier(density);
            trail?.SetSizeMultiplier(size);
        }

        protected override void Update()
        {
            base.Update();

            if (trail == null || DrawWidth <= 0 || DrawHeight <= 0)
                return;

            bool animating = AnimationViewport == null
                             || AnimationViewport.ScreenSpaceDrawQuad.Intersects(ScreenSpaceDrawQuad);

            if (!animating)
            {
                wasAnimating = false;
                return;
            }

            // Just came back on screen: start a fresh path so there's no streak
            // drawn across wherever the cursor "was".
            if (!wasAnimating)
            {
                trail.Reset();
                wasAnimating = true;
            }

            // Flat, wide horizontal sweep with a gentle vertical wave. Wide X +
            // small Y keeps it a flowing band rather than a circular fan, and
            // the slightly off-ratio frequencies stop it retracing one line.
            float t = (float)(Time.Current / 1000.0) * speed;
            float cx = DrawWidth * 0.5f;
            float cy = DrawHeight * 0.44f;
            var p = new Vector2(
                cx + MathF.Sin(t * 1.15f) * (DrawWidth * 0.40f),
                cy + MathF.Sin(t * 2.30f) * (DrawHeight * 0.20f));
            trail.Drive(ToScreenSpace(p));
        }
    }
}
