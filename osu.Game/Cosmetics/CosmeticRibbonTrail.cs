// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Lines;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Timing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Cosmetics
{
    /// <summary>
    /// A connected-ribbon cursor trail rendered as a SINGLE anti-aliased
    /// <see cref="SmoothPath"/> (with an optional wider additive glow path behind
    /// and a head dot). One path = one continuous band with NO seams — smooth,
    /// not the stepped/dotted look a chain of sub-paths produces.
    ///
    /// A single path is uniform-colour, so "Rainbow" cycles the whole ribbon's
    /// hue over time (smoothly) rather than painting a per-length spectrum; the
    /// two-tone neon look comes from a different core vs glow colour. The head
    /// vertex is pinned to the live cursor so the leading point tracks exactly,
    /// and the head dot fades when you stop so it doesn't sit there.
    /// </summary>
    public partial class CosmeticRibbonTrail : CompositeDrawable, IRequireHighFrequencyMousePosition, ICosmeticTrail
    {
        public enum RibbonColourMode
        {
            Solid,
            Rainbow,
        }

        public RibbonColourMode ColourMode { get; set; } = RibbonColourMode.Solid;

        /// <summary>Core ribbon colour.</summary>
        public Color4 PrimaryColour { get; set; } = Color4.White;

        /// <summary>Halo colour for the glow layer (can differ from the core for
        /// a two-tone neon look).</summary>
        public Color4 GlowColour { get; set; } = Color4.White;

        public bool Glow { get; set; } = true;
        public bool HeadDot { get; set; }

        public float Width { get; set; } = 10f;
        public double RibbonLifetime { get; set; } = 550;
        public float HueCycleSpeed { get; set; } = 0.35f;

        private const int max_points = 260;
        private const float point_spacing = 3f;

        private struct RibbonPoint
        {
            public Vector2 Pos;
            public double Time;
        }

        private readonly List<RibbonPoint> points = new List<RibbonPoint>();
        private Vector2? lastPosition;
        private Vector2? liveHead;
        private float distanceCarry;
        private float huePhase;
        private double lastMoveTime;

        private SmoothPath glowPath;
        private SmoothPath corePath;
        private Circle headDot;

        private double? baseLifetime;
        private float? baseWidth;

        public CosmeticRibbonTrail()
        {
            Clock = new FramedClock();
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var children = new List<Drawable>();

            if (Glow)
            {
                glowPath = new SmoothPath
                {
                    AutoSizeAxes = Axes.None,
                    RelativeSizeAxes = Axes.Both,
                    PathRadius = Math.Max(1f, Width * 0.95f),
                    Blending = BlendingParameters.Additive,
                };
                children.Add(glowPath);
            }

            corePath = new SmoothPath
            {
                AutoSizeAxes = Axes.None,
                RelativeSizeAxes = Axes.Both,
                PathRadius = Math.Max(1f, Width * 0.5f),
            };
            children.Add(corePath);

            if (HeadDot)
            {
                headDot = new Circle
                {
                    Size = new Vector2(Width * 1.4f),
                    Origin = Anchor.Centre,
                    Alpha = 0,
                    Blending = BlendingParameters.Additive,
                };
                children.Add(headDot);
            }

            InternalChildren = children.ToArray();
        }

        public override bool IsPresent => true;
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            AddTrail(e.ScreenSpaceMousePosition);
            return base.OnMouseMove(e);
        }

        public void Drive(Vector2 screenSpacePosition) => AddTrail(screenSpacePosition);

        public void Reset()
        {
            points.Clear();
            lastPosition = null;
            liveHead = null;
            distanceCarry = 0;
        }

        public void SetLengthMultiplier(float multiplier)
        {
            baseLifetime ??= RibbonLifetime;
            RibbonLifetime = baseLifetime.Value * multiplier;
        }

        public void SetDensityMultiplier(float multiplier)
        {
            baseWidth ??= Width;
            Width = baseWidth.Value * multiplier;
            if (glowPath != null) glowPath.PathRadius = Math.Max(1f, Width * 0.95f);
            if (corePath != null) corePath.PathRadius = Math.Max(1f, Width * 0.5f);
        }

        private void AddTrail(Vector2 screenSpacePosition)
        {
            Vector2 position = ToLocalSpace(screenSpacePosition);
            liveHead = position;
            lastMoveTime = Time.Current;

            if (!lastPosition.HasValue)
            {
                lastPosition = position;
                addPoint(position);
                return;
            }

            Vector2 diff = position - lastPosition.Value;
            float distance = diff.Length;
            if (distance <= 0)
                return;

            Vector2 direction = diff / distance;
            float traveled = 0;
            while (distanceCarry + (distance - traveled) >= point_spacing)
            {
                float step = point_spacing - distanceCarry;
                traveled += step;
                addPoint(lastPosition.Value + direction * traveled);
                distanceCarry = 0;
            }

            distanceCarry += distance - traveled;
            lastPosition = position;
        }

        private void addPoint(Vector2 position)
        {
            points.Add(new RibbonPoint { Pos = position, Time = Time.Current });
            if (points.Count > max_points)
                points.RemoveAt(0);
        }

        protected override void Update()
        {
            base.Update();

            huePhase += (float)(Time.Elapsed / 1000.0) * HueCycleSpeed;
            huePhase -= MathF.Floor(huePhase);

            double cutoff = Time.Current - RibbonLifetime;
            int remove = 0;
            while (remove < points.Count && points[remove].Time < cutoff)
                remove++;
            if (remove > 0)
                points.RemoveRange(0, remove);

            rebuild();
        }

        private void rebuild()
        {
            corePath.ClearVertices();
            glowPath?.ClearVertices();

            int n = points.Count;
            bool hasHead = liveHead.HasValue && (n == 0 || Vector2.Distance(points[n - 1].Pos, liveHead.Value) > 0.5f);
            int total = n + (hasHead ? 1 : 0);

            if (total < 2)
            {
                if (headDot != null)
                    headDot.Alpha = 0;
                return;
            }

            for (int i = 0; i < n; i++)
            {
                corePath.AddVertex(points[i].Pos);
                glowPath?.AddVertex(points[i].Pos);
            }

            if (hasHead)
            {
                corePath.AddVertex(liveHead.Value);
                glowPath?.AddVertex(liveHead.Value);
            }

            Color4 core = ColourMode == RibbonColourMode.Rainbow ? (Color4)Colour4.FromHSV(huePhase, 0.85f, 1f) : PrimaryColour;
            corePath.Colour = core;

            if (glowPath != null)
            {
                Color4 g = ColourMode == RibbonColourMode.Rainbow ? (Color4)Colour4.FromHSV((huePhase + 0.06f) % 1f, 0.9f, 1f) : GlowColour;
                glowPath.Colour = new Color4(g.R, g.G, g.B, 0.32f);
            }

            if (headDot != null)
            {
                Vector2 headPos = hasHead ? liveHead.Value : points[n - 1].Pos;
                headDot.Position = headPos;
                headDot.Size = new Vector2(Width * 1.4f);
                headDot.Colour = core;
                // Fade the head dot out shortly after you stop, so it doesn't sit
                // there as a glowing ball at rest.
                float idle = (float)((Time.Current - lastMoveTime) / 220.0);
                headDot.Alpha = Math.Clamp(1f - idle, 0f, 1f);
            }
        }
    }
}
