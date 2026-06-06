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
    /// Preview of a cursor-trail cosmetic. By default a card shows a STILL,
    /// full, dense snapshot (built by sweeping the trail fast for a moment then
    /// freezing it), which is cheap and conveys the colour / shape / length.
    /// Hovering a card (or the always-live detail panel) animates it for real.
    /// Off-screen or mid-scroll it freezes, so the grid never runs many live
    /// trails at once.
    /// </summary>
    public partial class CosmeticTrailPreview : Container
    {
        // How long (and how fast) to sweep when building the still snapshot. A
        // fast sweep fills the trail to its full length with a real head->tail
        // fade gradient, so the frozen frame actually looks like the trail.
        private const double snapshot_build_ms = 700;
        private const float snapshot_speed = 3.2f;

        private enum Mode
        {
            Paused,
            Building,
            Snapshot,
            Live,
        }

        private readonly CosmeticTrailDefinition def;
        private readonly float speed;
        private Drawable trailDrawable;
        private ICosmeticTrail trail;

        private Mode mode = Mode.Paused;
        private double snapshotElapsed;
        private bool snapshotReady;
        private bool hovered;
        private Vector2? lastScreenCentre;
        private BufferedContainer buffer;

        /// <summary>If set, the trail only runs while this drawable's screen quad
        /// overlaps ours (the store grid points cards at the scroll viewport).
        /// Null = the detail panel, which is always live.</summary>
        public Drawable AnimationViewport { get; set; }

        private bool alwaysLive => AnimationViewport == null;

        [Resolved(canBeNull: true)]
        private ToriiCosmeticsManager cosmetics { get; set; }

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

            // Render the trail into a framebuffer sized to the card so it hard-
            // clips to the card's bounds: the dot trail's custom draw node does
            // NOT honour the rounded mask, and at extreme UI scale its parts
            // leaked outside. A buffer captures only what's inside its bounds.
            //
            // cachedFrameBuffer: a frozen snapshot then costs nothing per frame
            // (the buffer isn't redrawn). While animating we call ForceRedraw()
            // each frame so the cached frame keeps up.
            InternalChild = buffer = new BufferedContainer(cachedFrameBuffer: true)
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(14, 14, 22, 255) },
                    trailDrawable,
                },
            };
        }

        public void ApplyCustomisation(float length, float density, float size)
        {
            trail?.SetLengthScale(length);
            trail?.SetDensityMultiplier(density);
            trail?.SetSizeMultiplier(size);
        }

        /// <summary>Card hover: live while hovered, snapshot otherwise.</summary>
        public void SetHovered(bool value) => hovered = value;

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

            // Detail panel is always live; a grid card goes live on hover (unless
            // potato mode, which keeps the grid as still snapshots).
            bool wantLive = alwaysLive || (!potato && hovered);

            // Scrolling or off-screen: freeze everything (cheap).
            if (!onScreen || moving)
            {
                pause();
                return;
            }

            if (wantLive)
            {
                if (mode != Mode.Live)
                {
                    trail.SetPaused(false);
                    trail.Reset();
                    snapshotReady = false; // live changes the trail; rebuild snapshot on unhover
                    mode = Mode.Live;
                }

                driveSweep(speed);
                buffer?.ForceRedraw();
                return;
            }

            // Already frozen: nothing to do (cached buffer, ~0 cost).
            if (mode == Mode.Snapshot)
                return;

            // Built once already (e.g. scrolled away and back): just re-freeze the
            // cached frame, don't replay the 1s build animation. Saves the work.
            if (snapshotReady)
            {
                trail.SetPaused(true);
                mode = Mode.Snapshot;
                return;
            }

            // Build the snapshot with a fast full sweep, then freeze it.
            if (mode != Mode.Building)
            {
                trail.SetPaused(false);
                trail.Reset();
                snapshotElapsed = 0;
                mode = Mode.Building;
            }

            driveSweep(snapshot_speed);
            buffer?.ForceRedraw();
            snapshotElapsed += Time.Elapsed;

            if (snapshotElapsed >= snapshot_build_ms)
            {
                trail.SetPaused(true);
                snapshotReady = true;
                mode = Mode.Snapshot;
            }
        }

        private void pause()
        {
            if (mode == Mode.Paused)
                return;

            trail.SetPaused(true);
            mode = Mode.Paused;
        }

        private void driveSweep(float sweepSpeed)
        {
            // Flat, wide horizontal sweep with a gentle vertical wave. Wide X +
            // small Y reads as a flowing band, not a circular fan; off-ratio
            // frequencies stop it retracing one line.
            float t = (float)(Time.Current / 1000.0) * sweepSpeed;
            float cx = DrawWidth * 0.5f;
            float cy = DrawHeight * 0.46f;
            var p = new Vector2(
                cx + MathF.Sin(t * 1.15f) * (DrawWidth * 0.36f),
                cy + MathF.Sin(t * 2.30f) * (DrawHeight * 0.18f));

            // Round-trip through the TRAIL's own matrix so screen -> local lands
            // exactly back on p (using ours drifted at extreme scale).
            trail.Drive(trailDrawable.ToScreenSpace(p));
        }
    }
}
