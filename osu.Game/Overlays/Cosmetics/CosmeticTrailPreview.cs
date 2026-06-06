// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
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
    /// A small live preview of a cursor-trail cosmetic that auto-orbits a
    /// synthetic cursor (so the trail draws itself without real mouse input).
    /// One instance at a time (shown in the detail panel) to stay GC-light.
    /// </summary>
    public partial class CosmeticTrailPreview : Container
    {
        private readonly CosmeticTrailDefinition def;
        private readonly float speed;
        private Drawable trailDrawable;
        private ICosmeticTrail trail;
        private bool active = true;

        /// <param name="speed">Orbit speed multiplier. Cards use a calmer orbit;
        /// the detail panel a livelier one.</param>
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

            InternalChildren = new[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(14, 14, 22, 255) },
                trailDrawable,
            };
        }

        public void ApplyCustomisation(float length, float density)
        {
            trail?.SetLengthMultiplier(length);
            trail?.SetDensityMultiplier(density);
        }

        /// <summary>Gate the trail engine. The store grid sets this false for
        /// scrolled-out cards so we don't drive ~35 live trails at once; on
        /// re-show we reset so there's no streak across the gap.</summary>
        public void SetActive(bool value)
        {
            if (active == value)
                return;

            active = value;
            if (active)
                trail?.Reset();
        }

        protected override void Update()
        {
            base.Update();

            if (!active || trail == null || DrawWidth <= 0 || DrawHeight <= 0)
                return;

            float t = (float)(Time.Current / 1000.0) * speed;
            var centre = DrawSize / 2;
            var p = centre + new Vector2(MathF.Cos(t * 2.1f) * (DrawSize.X * 0.3f),
                                         MathF.Sin(t * 2.7f) * (DrawSize.Y * 0.32f));
            trail.Drive(ToScreenSpace(p));
        }
    }
}
