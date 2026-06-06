// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Shaders.Types;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Timing;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;
using osuTK.Graphics.ES30;

namespace osu.Game.Cosmetics
{
    /// <summary>
    /// A configurable, store-cosmetic cursor trail. This is a self-contained
    /// adaptation of the gameplay <c>CursorTrail</c> (osu! ruleset) — copied
    /// rather than subclassed because the per-part colour we need for gradients
    /// and rainbows lives inside the private draw node, which the original
    /// renders with a single inherited colour.
    ///
    /// What it adds over the stock trail:
    ///   - <see cref="ColourMode"/>: Solid / Gradient (head→tail) / Rainbow.
    ///   - Per-part colour computed at draw time from the part's position along
    ///     the trail, so a Rainbow trail is a true spectrum along its length
    ///     (and flows over time via <see cref="HueCycleSpeed"/>).
    ///   - Public knobs for length (<see cref="FadeDurationOverride"/>), density
    ///     / smoothness (<see cref="IntervalMultiplierOverride"/>) and thickness
    ///     (<see cref="Thickness"/>), so the catalog can data-drive every trail
    ///     from one class.
    ///
    /// Drive it from real input (it handles OnMouseMove) or programmatically via
    /// <see cref="Drive"/> (used by the preview test scene to orbit a cursor).
    /// </summary>
    public partial class ToriiCosmeticTrail : Drawable, IRequireHighFrequencyMousePosition, ICosmeticTrail
    {
        private const int max_sprites = 2048;

        public enum TrailColourMode
        {
            Solid,
            Gradient,
            Rainbow,
            Palette,
        }

        // ── Cosmetic configuration (set by the catalog) ─────────────────────

        public TrailColourMode ColourMode { get; set; } = TrailColourMode.Solid;

        /// <summary>Solid colour, and the head colour of a Gradient.</summary>
        public Color4 PrimaryColour { get; set; } = Color4.White;

        /// <summary>The tail colour of a Gradient (unused for Solid/Rainbow).</summary>
        public Color4 SecondaryColour { get; set; } = Color4.White;

        /// <summary>Palette mode: colours interpolated head→tail across this list
        /// (e.g. an aurora green/teal/violet), giving the same soft dot style as
        /// Rainbow but with chosen colours instead of the full wheel.</summary>
        public Color4[] Palette { get; set; }

        /// <summary>Rainbow: hue (0..1) at the head of the trail.</summary>
        public float HueBase { get; set; }

        /// <summary>Rainbow: how much of the colour wheel (0..1, 1 = full) is
        /// spanned from head to tail. 1 = a full rainbow along the trail.</summary>
        public float HueSpread { get; set; } = 1f;

        /// <summary>Rainbow: colour-wheel turns per second (animates the spectrum
        /// flowing down the trail). 0 = static spectrum.</summary>
        public float HueCycleSpeed { get; set; } = 0.25f;

        /// <summary>How long (ms) a trail part takes to fade out. Higher = a
        /// longer tail.</summary>
        public double FadeDurationOverride { get; set; } = 450;

        /// <summary>Spacing multiplier between parts. Lower = denser = smoother
        /// (and a more solid-looking line).</summary>
        public float IntervalMultiplierOverride { get; set; } = 0.6f;

        /// <summary>Fade easing exponent (higher = the tail fades sooner).</summary>
        public float FadeExponentOverride { get; set; } = 1.7f;

        /// <summary>Part size in pixels (the trail's thickness).</summary>
        public float Thickness { get; set; } = 22f;

        // ── Internals (mirrors CursorTrail) ─────────────────────────────────

        protected virtual float FadeExponent => FadeExponentOverride;

        public Vector2 NewPartScale { get; set; } = Vector2.One;

        private Vector2 cursorScale = Vector2.One;

        public Vector2 CursorScale
        {
            get => cursorScale;
            set
            {
                cursorScale = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        protected Anchor TrailOrigin
        {
            get => trailOrigin;
            set
            {
                trailOrigin = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private readonly TrailPart[] parts = new TrailPart[max_sprites];
        private Anchor trailOrigin = Anchor.Centre;
        private int currentIndex;
        private IShader shader;
        private double timeOffset;
        private float time;

        // Monotonic 0..1 phase for the rainbow animation, advanced every frame.
        private float animationPhase;

        public ToriiCosmeticTrail()
        {
            // As with CursorTrail we depend on a running clock; make our own.
            Clock = new FramedClock();

            RelativeSizeAxes = Axes.Both;

            for (int i = 0; i < max_sprites; i++)
                parts[i].InvalidationID = -1;
        }

        [Resolved(canBeNull: true)]
        private ISkinSource skinSource { get; set; }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer, ShaderManager shaders, TextureStore textures)
        {
            // Soft, ROUND trail dot: prefer the active skin's cursortrail, then
            // the built-in game texture, only falling back to a (square) white
            // pixel if neither exists. This is what makes the trail read as a
            // smooth glowing ribbon instead of a chain of squares.
            texture ??= skinSource?.GetTexture(@"cursortrail") ?? textures.Get(@"Cursor/cursortrail") ?? renderer.WhitePixel;
            shader = shaders.Load(@"CursorTrail", FragmentShaderDescriptor.TEXTURE);

            // Normalise so Thickness is the part size in px regardless of the
            // source texture's native size.
            CursorScale = new Vector2(Thickness / Math.Max(1f, texture.DisplayWidth));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            resetTime();
        }

        private Texture texture;

        public Texture Texture
        {
            get => texture;
            set
            {
                if (texture == value)
                    return;

                texture = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        protected virtual double FadeDuration => FadeDurationOverride;

        public override bool IsPresent => true;

        protected override void Update()
        {
            base.Update();

            Invalidate(Invalidation.DrawNode);

            const int fade_clock_reset_threshold = 1000000;

            time = (float)((Time.Current - timeOffset) / FadeDuration);
            if (time > fade_clock_reset_threshold)
                resetTime();

            // Advance the rainbow phase. Wrapped to 0..1 so it never loses float
            // precision over a long session.
            animationPhase += (float)(Time.Elapsed / 1000.0) * HueCycleSpeed;
            animationPhase -= MathF.Floor(animationPhase);
        }

        private void resetTime()
        {
            for (int i = 0; i < parts.Length; ++i)
            {
                parts[i].Time -= time;

                if (parts[i].InvalidationID != -1)
                    ++parts[i].InvalidationID;
            }

            time = 0;
            timeOffset = Time.Current;
        }

        protected virtual bool InterpolateMovements => true;

        protected virtual float IntervalMultiplier => IntervalMultiplierOverride;
        protected virtual bool AvoidDrawingNearCursor => false;

        private Vector2? lastPosition;
        private readonly InputResampler resampler = new InputResampler();

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            AddTrail(e.ScreenSpaceMousePosition);
            return base.OnMouseMove(e);
        }

        /// <summary>Programmatically push the trail to a screen-space position
        /// (used by previews that orbit a synthetic cursor).</summary>
        public void Drive(Vector2 screenSpacePosition) => AddTrail(screenSpacePosition);

        private double? baseFade;
        private float? baseInterval;

        public void SetLengthMultiplier(float multiplier)
        {
            baseFade ??= FadeDurationOverride;
            FadeDurationOverride = baseFade.Value * multiplier;
        }

        public void SetDensityMultiplier(float multiplier)
        {
            baseInterval ??= IntervalMultiplierOverride;
            IntervalMultiplierOverride = baseInterval.Value / Math.Max(0.05f, multiplier);
        }

        public void Reset()
        {
            for (int i = 0; i < parts.Length; i++)
                parts[i].InvalidationID = -1;
            currentIndex = 0;
            lastPosition = null;
        }

        protected void AddTrail(Vector2 position)
        {
            position = ToLocalSpace(position);

            if (InterpolateMovements)
            {
                if (!lastPosition.HasValue)
                {
                    lastPosition = position;
                    resampler.AddPosition(lastPosition.Value);
                    return;
                }

                foreach (Vector2 pos2 in resampler.AddPosition(position))
                {
                    Trace.Assert(lastPosition.HasValue);

                    Vector2 pos1 = lastPosition.Value;
                    Vector2 diff = pos2 - pos1;
                    float distance = diff.Length;
                    Vector2 direction = diff / distance;

                    float interval = Texture.DisplayWidth * CursorScale.X / 2.5f * IntervalMultiplier;
                    float stopAt = distance - (AvoidDrawingNearCursor ? interval : 0);

                    for (float d = interval; d < stopAt; d += interval)
                    {
                        lastPosition = pos1 + direction * d;
                        addPart(lastPosition.Value);
                    }
                }
            }
            else
            {
                lastPosition = position;
                addPart(lastPosition.Value);
            }
        }

        private void addPart(Vector2 localSpacePosition)
        {
            parts[currentIndex].Position = localSpacePosition;
            parts[currentIndex].Time = time + 1;
            parts[currentIndex].Scale = NewPartScale;
            ++parts[currentIndex].InvalidationID;

            currentIndex = (currentIndex + 1) % max_sprites;
        }

        protected override DrawNode CreateDrawNode() => new TrailDrawNode(this);

        private struct TrailPart
        {
            public Vector2 Position;
            public float Time;
            public Vector2 Scale;
            public long InvalidationID;
        }

        private class TrailDrawNode : DrawNode
        {
            protected new ToriiCosmeticTrail Source => (ToriiCosmeticTrail)base.Source;

            private IShader shader;
            private Texture texture;

            private float time;
            private float fadeExponent;
            private Vector2 cursorScale;

            private TrailColourMode colourMode;
            private Color4 primaryLinear;
            private Color4 secondaryLinear;
            private Color4[] paletteLinear;
            private Color4[] lastSrcPalette;
            private float hueBase;
            private float hueSpread;
            private float animationPhase;

            private readonly TrailPart[] parts = new TrailPart[max_sprites];
            private Vector2 originPosition;

            private IVertexBatch<TexturedTrailVertex> vertexBatch;

            public TrailDrawNode(ToriiCosmeticTrail source)
                : base(source)
            {
            }

            public override void ApplyState()
            {
                base.ApplyState();

                shader = Source.shader;
                texture = Source.texture;
                time = Source.time;
                fadeExponent = Source.FadeExponent;
                cursorScale = Source.cursorScale;

                colourMode = Source.ColourMode;
                primaryLinear = Source.PrimaryColour.ToLinear();
                secondaryLinear = Source.SecondaryColour.ToLinear();

                // Cache the linear palette; only rebuild when the source array
                // changes, so ApplyState (every frame) allocates nothing.
                var srcPalette = Source.Palette;
                if (!ReferenceEquals(srcPalette, lastSrcPalette))
                {
                    lastSrcPalette = srcPalette;
                    if (srcPalette != null && srcPalette.Length > 0)
                    {
                        paletteLinear = new Color4[srcPalette.Length];
                        for (int i = 0; i < srcPalette.Length; i++)
                            paletteLinear[i] = srcPalette[i].ToLinear();
                    }
                    else
                        paletteLinear = null;
                }

                hueBase = Source.HueBase;
                hueSpread = Source.HueSpread;
                animationPhase = Source.animationPhase;

                originPosition = Vector2.Zero;

                if (Source.TrailOrigin.HasFlag(Anchor.x1))
                    originPosition.X = 0.5f;
                else if (Source.TrailOrigin.HasFlag(Anchor.x2))
                    originPosition.X = 1f;

                if (Source.TrailOrigin.HasFlag(Anchor.y1))
                    originPosition.Y = 0.5f;
                else if (Source.TrailOrigin.HasFlag(Anchor.y2))
                    originPosition.Y = 1f;

                Source.parts.CopyTo(parts, 0);
            }

            // Position along the trail: 0 at the cursor (newest part) → 1 at the
            // fading tail (oldest visible part).
            private static float positionFraction(float time, float partTime)
                => Math.Clamp((time - partTime + 1f) * 0.5f, 0f, 1f);

            private Color4 colourFor(float posFraction)
            {
                switch (colourMode)
                {
                    case TrailColourMode.Gradient:
                        return lerp(primaryLinear, secondaryLinear, posFraction);

                    case TrailColourMode.Rainbow:
                        float hue = hueBase + posFraction * hueSpread - animationPhase;
                        hue -= MathF.Floor(hue); // fractional part, wrapped to 0..1
                        var c = Colour4.FromHSV(hue, 1f, 1f);
                        return new Color4(c.R, c.G, c.B, 1f).ToLinear();

                    case TrailColourMode.Palette when paletteLinear != null:
                        if (paletteLinear.Length == 1)
                            return paletteLinear[0];
                        float f = Math.Clamp(posFraction, 0f, 1f) * (paletteLinear.Length - 1);
                        int idx = (int)MathF.Floor(f);
                        if (idx >= paletteLinear.Length - 1)
                            return paletteLinear[paletteLinear.Length - 1];
                        return lerp(paletteLinear[idx], paletteLinear[idx + 1], f - idx);

                    default:
                        return primaryLinear;
                }
            }

            private static Color4 lerp(Color4 a, Color4 b, float t)
                => new Color4(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t, a.A + (b.A - a.A) * t);

            private static Color4 mul(Color4 a, Color4 b)
                => new Color4(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);

            private IUniformBuffer<CursorTrailParameters> cursorTrailParameters;

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                vertexBatch ??= renderer.CreateQuadBatch<TexturedTrailVertex>(max_sprites, 1);

                cursorTrailParameters ??= renderer.CreateUniformBuffer<CursorTrailParameters>();
                cursorTrailParameters.Data = cursorTrailParameters.Data with
                {
                    FadeClock = time,
                    FadeExponent = fadeExponent
                };

                shader.Bind();
                shader.BindUniformBlock("m_CursorTrailParameters", cursorTrailParameters);

                texture.Bind();

                RectangleF textureRect = texture.GetTextureRect();

                renderer.PushLocalMatrix(DrawInfo.Matrix);

                foreach (var part in parts)
                {
                    if (part.InvalidationID == -1)
                        continue;

                    if (time - part.Time >= 1)
                        continue;

                    Color4 partColour = colourFor(positionFraction(time, part.Time));

                    vertexBatch.Add(new TexturedTrailVertex
                    {
                        Position = new Vector2(
                            part.Position.X - texture.DisplayWidth * originPosition.X * part.Scale.X * cursorScale.X,
                            part.Position.Y + texture.DisplayHeight * (1 - originPosition.Y) * part.Scale.Y * cursorScale.Y),
                        TexturePosition = textureRect.BottomLeft,
                        TextureRect = new Vector4(0, 0, 1, 1),
                        Colour = mul(partColour, DrawColourInfo.Colour.BottomLeft.Linear),
                        Time = part.Time
                    });

                    vertexBatch.Add(new TexturedTrailVertex
                    {
                        Position = new Vector2(
                            part.Position.X + texture.DisplayWidth * (1 - originPosition.X) * part.Scale.X * cursorScale.X,
                            part.Position.Y + texture.DisplayHeight * (1 - originPosition.Y) * part.Scale.Y * cursorScale.Y),
                        TexturePosition = textureRect.BottomRight,
                        TextureRect = new Vector4(0, 0, 1, 1),
                        Colour = mul(partColour, DrawColourInfo.Colour.BottomRight.Linear),
                        Time = part.Time
                    });

                    vertexBatch.Add(new TexturedTrailVertex
                    {
                        Position = new Vector2(
                            part.Position.X + texture.DisplayWidth * (1 - originPosition.X) * part.Scale.X * cursorScale.X,
                            part.Position.Y - texture.DisplayHeight * originPosition.Y * part.Scale.Y * cursorScale.Y),
                        TexturePosition = textureRect.TopRight,
                        TextureRect = new Vector4(0, 0, 1, 1),
                        Colour = mul(partColour, DrawColourInfo.Colour.TopRight.Linear),
                        Time = part.Time
                    });

                    vertexBatch.Add(new TexturedTrailVertex
                    {
                        Position = new Vector2(
                            part.Position.X - texture.DisplayWidth * originPosition.X * part.Scale.X * cursorScale.X,
                            part.Position.Y - texture.DisplayHeight * originPosition.Y * part.Scale.Y * cursorScale.Y),
                        TexturePosition = textureRect.TopLeft,
                        TextureRect = new Vector4(0, 0, 1, 1),
                        Colour = mul(partColour, DrawColourInfo.Colour.TopLeft.Linear),
                        Time = part.Time
                    });
                }

                renderer.PopLocalMatrix();

                vertexBatch.Draw();
                shader.Unbind();
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);

                vertexBatch?.Dispose();
                cursorTrailParameters?.Dispose();
            }

            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private record struct CursorTrailParameters
            {
                public UniformFloat FadeClock;
                public UniformFloat FadeExponent;
                private readonly UniformPadding8 pad1;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TexturedTrailVertex : IEquatable<TexturedTrailVertex>, IVertex
        {
            [VertexMember(2, VertexAttribPointerType.Float)]
            public Vector2 Position;

            [VertexMember(4, VertexAttribPointerType.Float)]
            public Color4 Colour;

            [VertexMember(2, VertexAttribPointerType.Float)]
            public Vector2 TexturePosition;

            [VertexMember(4, VertexAttribPointerType.Float)]
            public Vector4 TextureRect;

            [VertexMember(1, VertexAttribPointerType.Float)]
            public float Time;

            public bool Equals(TexturedTrailVertex other)
            {
                return Position.Equals(other.Position)
                       && TexturePosition.Equals(other.TexturePosition)
                       && Colour.Equals(other.Colour)
                       && Time.Equals(other.Time);
            }
        }
    }
}
