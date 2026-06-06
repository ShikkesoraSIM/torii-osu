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
    /// A CONNECTED-RIBBON cursor trail rendered with the framework's
    /// <see cref="SmoothPath"/> — so it's a properly tessellated, anti-aliased
    /// band with rounded joins and caps (no faceted, stepped mesh).
    ///
    /// This is the "finite, long ribbon" look (a defined tail that snakes behind
    /// the cursor). Quality touches:
    ///   - an optional wider, additive GLOW path behind the core for neon,
    ///   - an optional bright HEAD dot for a snappy leading point (comet),
    ///   - hue-cycling for rainbow, and a sine WAVE option.
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

        /// <summary>Halo colour for the glow layer (defaults to the core).</summary>
        public Color4 GlowColour { get; set; } = Color4.White;

        /// <summary>Add a wide, soft, additive halo behind the core (neon look).</summary>
        public bool Glow { get; set; } = true;

        /// <summary>Add a bright dot at the head for a snappy leading point.</summary>
        public bool HeadDot { get; set; }

        /// <summary>Full ribbon width in px.</summary>
        public float Width { get; set; } = 10f;

        /// <summary>How long (ms) a point lives — the ribbon's length while moving.</summary>
        public double RibbonLifetime { get; set; } = 550;

        public float HueCycleSpeed { get; set; } = 0.3f;

        public float WaveAmplitude { get; set; }
        public float WaveFrequency { get; set; } = 0.45f;
        public float WaveSpeed { get; set; } = 4f;

        private const int max_points = 250;
        private const float point_spacing = 4f;

        private struct RibbonPoint
        {
            public Vector2 Pos;
            public double Time;
        }

        private readonly List<RibbonPoint> points = new List<RibbonPoint>();
        private Vector2? lastPosition;
        private float distanceCarry;
        private float wavePhase;
        private float huePhase;

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
                Blending = Glow ? BlendingParameters.Additive : BlendingParameters.Inherit,
            };
            children.Add(corePath);

            if (HeadDot)
            {
                headDot = new Circle
                {
                    Size = new Vector2(Width * 1.6f),
                    Origin = Anchor.Centre,
                    Alpha = 0,
                    Blending = BlendingParameters.Additive,
                };
                children.Add(headDot);
            }

            InternalChildren = children.ToArray();
            applyColours();
        }

        public override bool IsPresent => true;
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            AddTrail(e.ScreenSpaceMousePosition);
            return base.OnMouseMove(e);
        }

        public void Drive(Vector2 screenSpacePosition) => AddTrail(screenSpacePosition);

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
            if (headDot != null) headDot.Size = new Vector2(Width * 1.6f);
        }

        private void AddTrail(Vector2 screenSpacePosition)
        {
            Vector2 position = ToLocalSpace(screenSpacePosition);

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

            // Accumulate leftover travel across frames so points land evenly even
            // when each frame's move is smaller than the spacing.
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

            wavePhase += (float)(Time.Elapsed / 1000.0) * WaveSpeed;
            huePhase += (float)(Time.Elapsed / 1000.0) * HueCycleSpeed;
            huePhase -= MathF.Floor(huePhase);

            double cutoff = Time.Current - RibbonLifetime;
            int remove = 0;
            while (remove < points.Count && points[remove].Time < cutoff)
                remove++;
            if (remove > 0)
                points.RemoveRange(0, remove);

            rebuildPaths();
            applyColours();
        }

        private void rebuildPaths()
        {
            corePath.ClearVertices();
            glowPath?.ClearVertices();

            if (points.Count < 2)
            {
                if (headDot != null)
                    headDot.Alpha = 0;
                return;
            }

            for (int i = 0; i < points.Count; i++)
            {
                Vector2 v = points[i].Pos;

                if (WaveAmplitude != 0)
                {
                    Vector2 prev = points[Math.Max(0, i - 1)].Pos;
                    Vector2 next = points[Math.Min(points.Count - 1, i + 1)].Pos;
                    Vector2 tangent = next - prev;
                    if (tangent.LengthSquared > 0)
                    {
                        tangent = Vector2.Normalize(tangent);
                        var perp = new Vector2(-tangent.Y, tangent.X);
                        v += perp * (MathF.Sin(i * WaveFrequency + wavePhase) * WaveAmplitude);
                    }
                }

                corePath.AddVertex(v);
                glowPath?.AddVertex(v);
            }

            if (headDot != null)
            {
                headDot.Position = points[points.Count - 1].Pos;
                headDot.Alpha = 1;
            }
        }

        private void applyColours()
        {
            if (corePath == null)
                return;

            Color4 core = PrimaryColour;
            Color4 glow = GlowColour;

            if (ColourMode == RibbonColourMode.Rainbow)
            {
                core = Colour4.FromHSV(huePhase, 0.85f, 1f);
                glow = Colour4.FromHSV((huePhase + 0.08f) % 1f, 0.9f, 1f);
            }

            corePath.Colour = core;
            if (glowPath != null)
                glowPath.Colour = new Color4(glow.R, glow.G, glow.B, 0.35f);
            if (headDot != null)
                headDot.Colour = core;
        }
    }
}
