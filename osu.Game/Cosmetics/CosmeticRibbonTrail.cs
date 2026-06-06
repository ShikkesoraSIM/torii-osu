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
    /// A connected-ribbon cursor trail. Each ribbon is a chain of anti-aliased
    /// <see cref="SmoothPath"/> sub-segments (rounded joins/caps); because each
    /// sub-segment carries its own colour/alpha/width we get a real spectrum
    /// ALONG the length (rainbow), a head→tail gradient, a fading tail, and a
    /// tapering width (comet). The head vertex is pinned to the live cursor each
    /// frame so the leading point tracks exactly (no stepped lag). Optional wide
    /// additive glow + a bright head dot finish the look.
    /// </summary>
    public partial class CosmeticRibbonTrail : CompositeDrawable, IRequireHighFrequencyMousePosition, ICosmeticTrail
    {
        public enum RibbonColourMode
        {
            Solid,
            Gradient,
            Rainbow,
        }

        public RibbonColourMode ColourMode { get; set; } = RibbonColourMode.Solid;

        public Color4 PrimaryColour { get; set; } = Color4.White;
        public Color4 SecondaryColour { get; set; } = Color4.White;

        public float HeadWidth { get; set; } = 10f;
        public float TailWidth { get; set; } = 10f;

        public bool FadeTail { get; set; } = true;

        public bool Glow { get; set; } = true;
        public Color4 GlowColour { get; set; } = Color4.White;

        public bool HeadDot { get; set; }

        public double RibbonLifetime { get; set; } = 550;

        public float HueSpread { get; set; } = 1f;
        public float HueCycleSpeed { get; set; } = 0.35f;

        private const int segment_count = 14;
        private const int max_points = 260;
        private const float point_spacing = 4f;

        private struct RibbonPoint
        {
            public Vector2 Pos;
            public double Time;
        }

        private readonly List<RibbonPoint> points = new List<RibbonPoint>();
        private readonly List<Vector2> pts = new List<Vector2>();
        private Vector2? lastPosition;
        private Vector2? liveHead;
        private float distanceCarry;
        private float huePhase;

        private SmoothPath glowPath;
        private SmoothPath[] segments;
        private Circle headDot;

        private double? baseLifetime;
        private float? baseHeadWidth;
        private float? baseTailWidth;

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
                    PathRadius = Math.Max(1f, HeadWidth),
                    Blending = BlendingParameters.Additive,
                };
                children.Add(glowPath);
            }

            segments = new SmoothPath[segment_count];
            for (int i = 0; i < segment_count; i++)
            {
                segments[i] = new SmoothPath
                {
                    AutoSizeAxes = Axes.None,
                    RelativeSizeAxes = Axes.Both,
                    PathRadius = Math.Max(1f, HeadWidth * 0.5f),
                    Blending = Glow ? BlendingParameters.Additive : BlendingParameters.Inherit,
                };
                children.Add(segments[i]);
            }

            if (HeadDot)
            {
                headDot = new Circle
                {
                    Size = new Vector2(HeadWidth * 1.5f),
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
            baseHeadWidth ??= HeadWidth;
            baseTailWidth ??= TailWidth;
            HeadWidth = baseHeadWidth.Value * multiplier;
            TailWidth = baseTailWidth.Value * multiplier;
        }

        private void AddTrail(Vector2 screenSpacePosition)
        {
            Vector2 position = ToLocalSpace(screenSpacePosition);
            liveHead = position;

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
            // Working vertices = committed points + the live cursor head, so the
            // leading point sits exactly on the cursor every frame (smooth head).
            pts.Clear();
            for (int i = 0; i < points.Count; i++)
                pts.Add(points[i].Pos);
            if (liveHead.HasValue && (pts.Count == 0 || Vector2.Distance(pts[pts.Count - 1], liveHead.Value) > 0.5f))
                pts.Add(liveHead.Value);

            int n = pts.Count;

            if (n < 2)
            {
                glowPath?.ClearVertices();
                foreach (var s in segments)
                    s.ClearVertices();
                if (headDot != null)
                    headDot.Alpha = 0;
                return;
            }

            if (glowPath != null)
            {
                glowPath.ClearVertices();
                for (int i = 0; i < n; i++)
                    glowPath.AddVertex(pts[i]);
                glowPath.PathRadius = Math.Max(1f, HeadWidth);

                Color4 g = ColourMode == RibbonColourMode.Rainbow ? (Color4)Colour4.FromHSV(huePhase, 0.7f, 1f) : GlowColour;
                glowPath.Colour = new Color4(g.R, g.G, g.B, 0.30f);
            }

            // pts[0] = tail (oldest), pts[n-1] = head (cursor).
            for (int j = 0; j < segment_count; j++)
            {
                var seg = segments[j];
                seg.ClearVertices();

                int lo = (int)Math.Round(j * (n - 1) / (double)segment_count);
                int hi = (int)Math.Round((j + 1) * (n - 1) / (double)segment_count);

                if (hi - lo < 1)
                    continue;

                for (int i = lo; i <= hi; i++)
                    seg.AddVertex(pts[i]);

                // 0 at the head (cursor), 1 at the tail.
                float posFrac = 1f - (lo + hi) * 0.5f / (n - 1);

                float width = HeadWidth + (TailWidth - HeadWidth) * posFrac;
                seg.PathRadius = Math.Max(1f, width * 0.5f);

                Color4 c = colourFor(posFrac);
                float alpha = FadeTail ? 1f - posFrac : 1f;
                seg.Colour = new Color4(c.R, c.G, c.B, c.A * alpha);
            }

            if (headDot != null)
            {
                headDot.Position = pts[n - 1];
                headDot.Size = new Vector2(HeadWidth * 1.5f);
                headDot.Colour = colourFor(0f);
                headDot.Alpha = 1;
            }
        }

        private Color4 colourFor(float posFrac)
        {
            switch (ColourMode)
            {
                case RibbonColourMode.Gradient:
                    return lerp(PrimaryColour, SecondaryColour, posFrac);

                case RibbonColourMode.Rainbow:
                    float hue = posFrac * HueSpread + huePhase;
                    hue -= MathF.Floor(hue);
                    return Colour4.FromHSV(hue, 0.9f, 1f);

                default:
                    return PrimaryColour;
            }
        }

        private static Color4 lerp(Color4 a, Color4 b, float t)
            => new Color4(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
    }
}
