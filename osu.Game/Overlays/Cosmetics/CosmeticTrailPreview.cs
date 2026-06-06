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
    ///
    /// In "Potato PC" mode it animates a brief burst to form a representative
    /// frame, then freezes into a static snapshot (near-zero per-frame cost).
    /// </summary>
    public partial class CosmeticTrailPreview : Container
    {
        private const double snapshot_build_ms = 750;

        private readonly CosmeticTrailDefinition def;
        private readonly float speed;
        private Drawable trailDrawable;
        private ICosmeticTrail trail;
        private bool wasAnimating;
        private Vector2? lastScreenCentre;

        private bool lastPotato;
        private bool snapshotBuilt;
        private double snapshotElapsed;

        [Resolved(canBeNull: true)]
        private ToriiCosmeticsManager cosmetics { get; set; }

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

            // Driven only by our synthetic sweep; ignore the real mouse so moving
            // over a card doesn't flood the trail and make it lag / go haywire.
            trail?.SetInputActive(false);

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

            var centre = ScreenSpaceDrawQuad.Centre;
            bool moving = lastScreenCentre is Vector2 last && Vector2.Distance(centre, last) > 0.5f;
            lastScreenCentre = centre;

            bool onScreen = AnimationViewport == null
                            || AnimationViewport.ScreenSpaceDrawQuad.Intersects(ScreenSpaceDrawQuad);

            bool potato = cosmetics?.StorePotatoMode.Value ?? false;

            // Mode flipped: rebuild cleanly under the new mode.
            if (potato != lastPotato)
            {
                lastPotato = potato;
                snapshotBuilt = false;
                snapshotElapsed = 0;
                wasAnimating = false;
                trail.SetPaused(false);
                trail.Reset();
            }

            // Potato: once the snapshot is built, keep it frozen forever (cheap).
            if (potato && snapshotBuilt)
                return;

            // Drive only while on screen AND still (both modes). Scrolling or
            // off-screen freezes, so a fast scroll doesn't rebuild a dozen trails
            // per frame.
            if (!onScreen || moving)
            {
                trail.SetPaused(true);
                wasAnimating = false;
                return;
            }

            if (!wasAnimating)
            {
                trail.SetPaused(false);
                trail.Reset();
                snapshotElapsed = 0;
                wasAnimating = true;
            }

            driveSweep();

            // Potato: animate a short burst into a representative mid-animation
            // frame, then freeze it.
            if (potato)
            {
                snapshotElapsed += Time.Elapsed;
                if (snapshotElapsed >= snapshot_build_ms)
                {
                    trail.SetPaused(true);
                    snapshotBuilt = true;
                }
            }
        }

        private void driveSweep()
        {
            // Flat, wide horizontal sweep with a gentle vertical wave. Wide X +
            // small Y keeps it a flowing band rather than a circular fan, and the
            // slightly off-ratio frequencies stop it retracing one line.
            float t = (float)(Time.Current / 1000.0) * speed;
            float cx = DrawWidth * 0.5f;
            float cy = DrawHeight * 0.46f;
            var p = new Vector2(
                cx + MathF.Sin(t * 1.15f) * (DrawWidth * 0.36f),
                cy + MathF.Sin(t * 2.30f) * (DrawHeight * 0.18f));

            // Round-trip through the TRAIL's own matrix (not ours), so screen ->
            // local lands exactly back on p. Using our matrix here let parts drift
            // outside the trail's local bounds at extreme UI scale and spill past
            // the card's mask.
            trail.Drive(trailDrawable.ToScreenSpace(p));
        }
    }
}
