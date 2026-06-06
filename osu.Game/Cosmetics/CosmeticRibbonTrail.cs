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
    /// A connected-ribbon cursor trail built on the framework's anti-aliased
    /// <see cref="SmoothPath"/>. The raw cursor path is de-jittered each frame
    /// (so a real, noisy mouse still reads smooth) and the head vertex is pinned
    /// to the live cursor.
    ///
    /// Render styles (one per cosmetic):
    ///   - simple   : one uniform-colour band (Solid, or whole-band hue cycle).
    ///   - segmented: a chain of sub-bands, each its own colour/alpha/width, for
    ///                a per-length spectrum / gradient / palette, taper + tail
    ///                fade. Sub-bands use normal blending so joins don't bloom
    ///                into bright dots.
    ///   - rgbSplit : three additive copies offset in R/G/B for a chromatic
    ///                "glitch" look.
    /// Plus optional glow halo, a head dot (small = comet, large = wisp orb), and
    /// a width pulse (heartbeat).
    /// </summary>
    public partial class CosmeticRibbonTrail : CompositeDrawable, IRequireHighFrequencyMousePosition, ICosmeticTrail
    {
        public enum RibbonColourMode
        {
            Solid,
            Gradient,
            Rainbow,
            Palette,
        }

        public RibbonColourMode ColourMode { get; set; } = RibbonColourMode.Solid;

        public Color4 PrimaryColour { get; set; } = Color4.White;
        public Color4 SecondaryColour { get; set; } = Color4.White;
        public Color4[] Palette { get; set; }

        /// <summary>Per-length colour/width/fade (vs one uniform band).</summary>
        public bool Segmented { get; set; }

        public float Width { get; set; } = 10f;
        public float HeadWidth { get; set; } = 12f;
        public float TailWidth { get; set; } = 4f;
        public bool FadeTail { get; set; } = true;

        public bool Glow { get; set; } = true;
        public Color4 GlowColour { get; set; } = Color4.White;

        public bool HeadDot { get; set; }
        public float HeadDotScale { get; set; } = 1.4f;

        /// <summary>Chromatic-aberration glitch: R/G/B copies offset apart.</summary>
        public bool RgbSplit { get; set; }
        public float RgbSplitOffset { get; set; } = 3f;

        /// <summary>Width pulse (heartbeat): 0 = none, e.g. 0.35 = +/-35%.</summary>
        public float PulseAmount { get; set; }
        public float PulseSpeed { get; set; } = 1.6f;

        public double RibbonLifetime { get; set; } = 550;
        public float HueSpread { get; set; } = 1f;
        public float HueCycleSpeed { get; set; } = 0.35f;

        private const int segment_count = 16;
        // Generous safety cap only. The trail length is governed by TIME
        // (RibbonLifetime), like osu!'s own cursor trail: points age out after a
        // fixed lifetime regardless of how far you moved. This cap sits high
        // enough (~2400px of path) that it never trims before the lifetime does
        // at sane speeds, so the ribbon stops feeling distance-bound / "weird".
        private const int max_points = 600;
        private const float point_spacing = 4f;
        private const int smoothing_passes = 3;

        private struct RibbonPoint
        {
            public Vector2 Pos;
            public double Time;
        }

        private readonly List<RibbonPoint> points = new List<RibbonPoint>();
        private readonly List<Vector2> pathBuffer = new List<Vector2>();
        private Vector2? lastPosition;
        private Vector2? liveHead;
        private float distanceCarry;
        private float huePhase;
        private float pulsePhase;
        private double lastMoveTime;

        private SmoothPath glowPath;
        private SmoothPath corePath;
        private SmoothPath[] segments;
        private SmoothPath[] rgb;
        private Circle headDot;

        private double? baseLifetime;
        private float? baseWidthScale;
        private float widthScale = 1f;

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
                    PathRadius = Math.Max(1f, baseWidth() * 0.95f),
                    Blending = BlendingParameters.Additive,
                };
                children.Add(glowPath);
            }

            if (RgbSplit)
            {
                rgb = new SmoothPath[3];
                Color4[] cols = { new Color4(255, 60, 80, 255), new Color4(70, 255, 120, 255), new Color4(90, 120, 255, 255) };
                for (int i = 0; i < 3; i++)
                {
                    rgb[i] = new SmoothPath
                    {
                        AutoSizeAxes = Axes.None,
                        RelativeSizeAxes = Axes.Both,
                        PathRadius = Math.Max(1f, Width * 0.5f),
                        Colour = cols[i],
                        Blending = BlendingParameters.Additive,
                    };
                    children.Add(rgb[i]);
                }
            }
            else if (Segmented)
            {
                segments = new SmoothPath[segment_count];
                for (int i = 0; i < segment_count; i++)
                {
                    segments[i] = new SmoothPath
                    {
                        AutoSizeAxes = Axes.None,
                        RelativeSizeAxes = Axes.Both,
                        PathRadius = Math.Max(1f, HeadWidth * 0.5f),
                        // Normal blend so sub-band joins don't bloom into dots.
                    };
                    children.Add(segments[i]);
                }
            }
            else
            {
                corePath = new SmoothPath
                {
                    AutoSizeAxes = Axes.None,
                    RelativeSizeAxes = Axes.Both,
                    PathRadius = Math.Max(1f, Width * 0.5f),
                };
                children.Add(corePath);
            }

            if (HeadDot)
            {
                headDot = new Circle
                {
                    Size = new Vector2(baseWidth() * HeadDotScale),
                    Origin = Anchor.Centre,
                    Alpha = 0,
                    Blending = BlendingParameters.Additive,
                };
                children.Add(headDot);
            }

            InternalChildren = children.ToArray();
        }

        private float baseWidth() => Segmented ? Math.Max(HeadWidth, TailWidth) : Width;

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

        public void SetLengthScale(float scale01)
        {
            // Length is purely the lifetime (TIME), so the trail behaves like
            // osu!'s: it persists for a set time and catches up to the cursor
            // when you stop, instead of being a fixed-distance smear. 0 maps to
            // a fixed short floor (same ms as every other trail), 1 to default.
            baseLifetime ??= RibbonLifetime;
            scale01 = Math.Clamp(scale01, 0f, 1f);
            RibbonLifetime = CosmeticEconomy.LengthFloorMilliseconds
                             + (baseLifetime.Value - CosmeticEconomy.LengthFloorMilliseconds) * scale01;
        }

        // A connected ribbon is one continuous band, so "density" (count per
        // travel) has no meaning. The shop hides this slider for ribbons; this
        // stays a no-op so the shared customisation path can call it blindly.
        public void SetDensityMultiplier(float multiplier)
        {
        }

        public void SetSizeMultiplier(float multiplier)
        {
            baseWidthScale ??= widthScale;
            widthScale = baseWidthScale.Value * Math.Max(0.1f, multiplier);
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
            while (points.Count > max_points)
                points.RemoveAt(0);
        }

        protected override void Update()
        {
            base.Update();

            huePhase += (float)(Time.Elapsed / 1000.0) * HueCycleSpeed;
            huePhase -= MathF.Floor(huePhase);
            pulsePhase += (float)(Time.Elapsed / 1000.0) * PulseSpeed;

            double cutoff = Time.Current - RibbonLifetime;
            int remove = 0;
            while (remove < points.Count && points[remove].Time < cutoff)
                remove++;
            if (remove > 0)
                points.RemoveRange(0, remove);

            rebuild();
        }

        private void clearAllVertices()
        {
            glowPath?.ClearVertices();
            corePath?.ClearVertices();
            if (segments != null)
                foreach (var s in segments)
                    s.ClearVertices();
            if (rgb != null)
                foreach (var s in rgb)
                    s.ClearVertices();
        }

        private void rebuild()
        {
            clearAllVertices();

            pathBuffer.Clear();
            int n = points.Count;
            for (int i = 0; i < n; i++)
                pathBuffer.Add(points[i].Pos);
            if (liveHead.HasValue && (pathBuffer.Count == 0 || Vector2.Distance(pathBuffer[pathBuffer.Count - 1], liveHead.Value) > 0.5f))
                pathBuffer.Add(liveHead.Value);

            int count = pathBuffer.Count;
            if (count < 2)
            {
                if (headDot != null)
                    headDot.Alpha = 0;
                return;
            }

            for (int pass = 0; pass < smoothing_passes && count >= 3; pass++)
            {
                Vector2 prev = pathBuffer[0];
                for (int i = 1; i < count - 1; i++)
                {
                    Vector2 cur = pathBuffer[i];
                    pathBuffer[i] = (prev + cur * 2f + pathBuffer[i + 1]) * 0.25f;
                    prev = cur;
                }
            }

            float pulse = PulseAmount > 0 ? 1f + PulseAmount * MathF.Sin(pulsePhase * MathF.PI * 2f) : 1f;
            float wscale = widthScale * pulse;

            // Glow halo (single wide path).
            if (glowPath != null)
            {
                for (int i = 0; i < count; i++)
                    glowPath.AddVertex(pathBuffer[i]);
                glowPath.PathRadius = Math.Max(1f, baseWidth() * 0.95f * wscale);
                Color4 g = ColourMode == RibbonColourMode.Rainbow ? (Color4)Colour4.FromHSV((huePhase + 0.06f) % 1f, 0.9f, 1f) : GlowColour;
                glowPath.Colour = new Color4(g.R, g.G, g.B, 0.30f);
            }

            if (rgb != null)
            {
                for (int k = 0; k < 3; k++)
                {
                    // No per-frame array alloc (this runs every frame on the cursor).
                    float ox = k == 0 ? RgbSplitOffset : k == 2 ? -RgbSplitOffset : 0f;
                    for (int i = 0; i < count; i++)
                        rgb[k].AddVertex(new Vector2(pathBuffer[i].X + ox, pathBuffer[i].Y));
                    rgb[k].PathRadius = Math.Max(1f, Width * 0.5f * wscale);
                }
            }
            else if (segments != null)
            {
                for (int j = 0; j < segment_count; j++)
                {
                    var seg = segments[j];

                    int lo = (int)Math.Round(j * (count - 1) / (double)segment_count);
                    int hi = (int)Math.Round((j + 1) * (count - 1) / (double)segment_count);
                    if (hi - lo < 1)
                        continue;

                    for (int i = lo; i <= hi; i++)
                        seg.AddVertex(pathBuffer[i]);

                    float posFrac = 1f - (lo + hi) * 0.5f / (count - 1);
                    float width = HeadWidth + (TailWidth - HeadWidth) * posFrac;
                    seg.PathRadius = Math.Max(1f, width * 0.5f * wscale);

                    Color4 c = colourFor(posFrac);
                    float alpha = FadeTail ? 1f - posFrac : 1f;
                    seg.Colour = new Color4(c.R, c.G, c.B, c.A * alpha);
                }
            }
            else if (corePath != null)
            {
                for (int i = 0; i < count; i++)
                    corePath.AddVertex(pathBuffer[i]);
                corePath.PathRadius = Math.Max(1f, Width * 0.5f * wscale);
                corePath.Colour = ColourMode == RibbonColourMode.Rainbow ? (Color4)Colour4.FromHSV(huePhase, 0.85f, 1f) : PrimaryColour;
            }

            if (headDot != null)
            {
                headDot.Position = pathBuffer[count - 1];
                headDot.Size = new Vector2(baseWidth() * HeadDotScale * wscale);
                headDot.Colour = colourFor(0f);
                float idle = (float)((Time.Current - lastMoveTime) / 220.0);
                headDot.Alpha = Math.Clamp(1f - idle, 0f, 1f);
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

                case RibbonColourMode.Palette when Palette != null && Palette.Length > 0:
                    if (Palette.Length == 1)
                        return Palette[0];
                    float f = Math.Clamp(posFrac, 0f, 1f) * (Palette.Length - 1);
                    int idx = (int)MathF.Floor(f);
                    if (idx >= Palette.Length - 1)
                        return Palette[Palette.Length - 1];
                    return lerp(Palette[idx], Palette[idx + 1], f - idx);

                default:
                    return PrimaryColour;
            }
        }

        private static Color4 lerp(Color4 a, Color4 b, float t)
            => new Color4(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);
    }
}
