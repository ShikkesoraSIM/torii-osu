// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Timing;
using osu.Framework.Utils;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Cosmetics
{
    /// <summary>
    /// A CONNECTED-RIBBON cursor trail: instead of fading dots or spawned
    /// shapes, it stitches the recent cursor path into one smooth band (a real
    /// mesh) that can taper, fade, wave and run a spectrum down its length.
    ///
    /// This is the "finite, long ribbon" look (a defined tail that snakes behind
    /// the cursor), and the base for the flashier styles: comet (wide head →
    /// thin tail), neon (additive glow), wavy (sine-offset), aurora/rainbow.
    ///
    /// Renders each path segment as a quad via the high-level DrawQuad helper, so
    /// every cross-section gets its own colour + alpha (gradient + tail fade) for
    /// free.
    /// </summary>
    public partial class CosmeticRibbonTrail : Drawable, IRequireHighFrequencyMousePosition, ICosmeticTrail
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

        public float HueBase { get; set; }
        public float HueSpread { get; set; } = 1f;
        public float HueCycleSpeed { get; set; } = 0.25f;

        /// <summary>Length of the ribbon, as how long (ms) a point lives. Bigger
        /// = longer tail (while moving).</summary>
        public double RibbonLifetime { get; set; } = 550;

        /// <summary>Full width at the head (cursor) and tail. Set equal for a
        /// constant ribbon, or HeadWidth &gt; TailWidth for a comet.</summary>
        public float HeadWidth { get; set; } = 10f;
        public float TailWidth { get; set; } = 2f;

        /// <summary>Fade the tail to transparent. Off = a hard-edged finite
        /// ribbon (the "defined tail" some players like).</summary>
        public bool FadeTail { get; set; } = true;

        /// <summary>Sine wobble across the ribbon. 0 = straight.</summary>
        public float WaveAmplitude { get; set; }
        public float WaveFrequency { get; set; } = 0.45f;
        public float WaveSpeed { get; set; } = 4f;

        private const int max_points = 300;
        private const float point_spacing = 5f;

        private struct RibbonPoint
        {
            public Vector2 Pos;
            public double Time;
        }

        private readonly List<RibbonPoint> points = new List<RibbonPoint>();
        private Vector2? lastPosition;
        private float wavePhase;
        private float huePhase;

        private IShader shader;
        private Texture texture;

        private double? baseLifetime;
        private float? baseHeadWidth;
        private float? baseTailWidth;

        public CosmeticRibbonTrail()
        {
            Clock = new FramedClock();
            RelativeSizeAxes = Axes.Both;
            Blending = BlendingParameters.Additive;
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer, ShaderManager shaders)
        {
            texture = renderer.WhitePixel;
            shader = shaders.Load(VertexShaderDescriptor.TEXTURE_2, FragmentShaderDescriptor.TEXTURE);
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
            // For a ribbon "density" reads best as thickness.
            baseHeadWidth ??= HeadWidth;
            baseTailWidth ??= TailWidth;
            HeadWidth = baseHeadWidth.Value * multiplier;
            TailWidth = baseTailWidth.Value * multiplier;
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

            if (distance < point_spacing)
                return;

            Vector2 direction = diff / distance;
            for (float d = point_spacing; d <= distance; d += point_spacing)
                addPoint(lastPosition.Value + direction * d);

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

            Invalidate(Invalidation.DrawNode);
        }

        protected override DrawNode CreateDrawNode() => new RibbonDrawNode(this);

        private class RibbonDrawNode : DrawNode
        {
            protected new CosmeticRibbonTrail Source => (CosmeticRibbonTrail)base.Source;

            private IShader shader;
            private Texture texture;

            private RibbonColourMode colourMode;
            private Color4 primary;
            private Color4 secondary;
            private float hueBase;
            private float hueSpread;
            private float huePhase;
            private float headWidth;
            private float tailWidth;
            private bool fadeTail;
            private float waveAmplitude;
            private float waveFrequency;
            private float wavePhase;

            private readonly List<Vector2> positions = new List<Vector2>();
            private readonly List<float> ages = new List<float>();

            private IVertexBatch<TexturedVertex2D> vertexBatch;

            public RibbonDrawNode(CosmeticRibbonTrail source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                shader = Source.shader;
                texture = Source.texture;
                colourMode = Source.ColourMode;
                primary = Source.PrimaryColour;
                secondary = Source.SecondaryColour;
                hueBase = Source.HueBase;
                hueSpread = Source.HueSpread;
                huePhase = Source.huePhase;
                headWidth = Source.HeadWidth;
                tailWidth = Source.TailWidth;
                fadeTail = Source.FadeTail;
                waveAmplitude = Source.WaveAmplitude;
                waveFrequency = Source.WaveFrequency;
                wavePhase = Source.wavePhase;

                positions.Clear();
                ages.Clear();

                double now = Source.Time.Current;
                double lifetime = Math.Max(1, Source.RibbonLifetime);

                foreach (var p in Source.points)
                {
                    positions.Add(p.Pos);
                    ages.Add((float)Math.Clamp((now - p.Time) / lifetime, 0, 1));
                }
            }

            private Color4 colourFor(float age)
            {
                Color4 c;
                switch (colourMode)
                {
                    case RibbonColourMode.Gradient:
                        c = lerp(primary, secondary, age);
                        break;

                    case RibbonColourMode.Rainbow:
                        float hue = hueBase + age * hueSpread - huePhase;
                        hue -= MathF.Floor(hue);
                        c = Colour4.FromHSV(hue, 0.9f, 1f);
                        break;

                    default:
                        c = primary;
                        break;
                }

                float alpha = fadeTail ? 1f - age : 1f;
                return new Color4(c.R, c.G, c.B, c.A * alpha);
            }

            private static Color4 lerp(Color4 a, Color4 b, float t)
                => new Color4(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                int count = positions.Count;
                if (count < 2)
                    return;

                vertexBatch ??= renderer.CreateQuadBatch<TexturedVertex2D>(max_points, 1);

                // Apply the optional sine wobble to a working copy of the path.
                var centre = new Vector2[count];
                for (int i = 0; i < count; i++)
                {
                    centre[i] = positions[i];

                    if (waveAmplitude != 0)
                    {
                        Vector2 prev = positions[Math.Max(0, i - 1)];
                        Vector2 next = positions[Math.Min(count - 1, i + 1)];
                        Vector2 tangent = next - prev;
                        if (tangent.LengthSquared > 0)
                        {
                            tangent = Vector2.Normalize(tangent);
                            var perp = new Vector2(-tangent.Y, tangent.X);
                            centre[i] += perp * (MathF.Sin(i * waveFrequency + wavePhase) * waveAmplitude);
                        }
                    }
                }

                shader.Bind();

                for (int i = 0; i < count - 1; i++)
                {
                    Vector2 c0 = centre[i];
                    Vector2 c1 = centre[i + 1];

                    Vector2 seg = c1 - c0;
                    if (seg.LengthSquared <= 0)
                        continue;

                    seg = Vector2.Normalize(seg);
                    var perp = new Vector2(-seg.Y, seg.X);

                    float w0 = Interpolation.ValueAt(ages[i], headWidth, tailWidth, 0f, 1f) * 0.5f;
                    float w1 = Interpolation.ValueAt(ages[i + 1], headWidth, tailWidth, 0f, 1f) * 0.5f;

                    var quad = new Quad(
                        Vector2Extensions.Transform(c0 + perp * w0, DrawInfo.Matrix),
                        Vector2Extensions.Transform(c1 + perp * w1, DrawInfo.Matrix),
                        Vector2Extensions.Transform(c0 - perp * w0, DrawInfo.Matrix),
                        Vector2Extensions.Transform(c1 - perp * w1, DrawInfo.Matrix));

                    Color4 col0 = colourFor(ages[i]);
                    Color4 col1 = colourFor(ages[i + 1]);

                    var colour = new ColourInfo
                    {
                        TopLeft = col0,
                        BottomLeft = col0,
                        TopRight = col1,
                        BottomRight = col1,
                    };

                    renderer.DrawQuad(texture, quad, colour, null, vertexBatch.AddAction);
                }

                shader.Unbind();
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);
                vertexBatch?.Dispose();
            }
        }
    }
}
