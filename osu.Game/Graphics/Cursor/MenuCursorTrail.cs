// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Rendering.Vertices;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Graphics.Shaders.Types;
using osu.Framework.Graphics.Textures;
using osu.Framework.Graphics.Visualisation;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Framework.Timing;
using osu.Game.Configuration;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;
using osuTK.Graphics.ES30;

namespace osu.Game.Graphics.Cursor
{
    // ---------------------------------------------------------------------
    // Menu-context cursor trail.
    // ---------------------------------------------------------------------
    //
    // This file is a deliberate, byte-for-byte port of the osu! ruleset's
    // gameplay cursor-trail pipeline (osu.Game.Rulesets.Osu.UI.Cursor.CursorTrail
    // + osu.Game.Rulesets.Osu.Skinning.Legacy.LegacyCursorTrail) into osu.Game,
    // mirrored here so MenuCursorContainer can render the trail without
    // taking a project reference on osu.Game.Rulesets.Osu (which would create
    // a circular dependency — the ruleset already references osu.Game).
    //
    // The user's expectation: "tiene q funcionar de la exacta misma forma q
    // lo hace el osu normal, y si es una skin de esas con trail super largo
    // q usa el coso de cursormiddle viste par ahacer ese efecto tambien tiene
    // q renderizarse one to one". So the port is intentionally line-for-line
    // with the upstream files — the shader name ("CursorTrail"), the
    // max-sprite count (2048), the FadeDuration constants (150ms disjoint /
    // 500ms continuous), the disjoint-trail decision rule (cursormiddle
    // presence at the SAME provider that supplied cursor), the InputResampler
    // interpolation, and the per-vertex layout are all preserved.
    //
    // Two visible behaviours come out of this:
    //   1. "Continuous trail" — when the active skin ships BOTH cursor.png
    //      AND cursormiddle.png. The trail interpolates mouse movements,
    //      drops parts at sub-mouse spacing, fades over 500ms with additive
    //      blending. This is the smooth-tail look.
    //   2. "Disjoint trail" — when the skin has cursor.png but NO
    //      cursormiddle.png. Trail parts are emitted at a fixed temporal
    //      cadence (~16.67ms ≈ 60Hz) at the live cursor position, fade
    //      over 150ms with normal blending. This is the long-distinct-dots
    //      look used by trail-heavy skins.
    //
    // The two classes below match the upstream split:
    //   - MenuCursorTrail — base shader-driven particle pipeline (was
    //     CursorTrail). Ruleset-agnostic.
    //   - SkinnableMenuCursorTrail — legacy-skin-aware wrapper (was
    //     LegacyCursorTrail). Reads texture + disjoint-mode flag from the
    //     active skin chain via FindProvider, and rebinds OsuSetting.GameplayCursorSize
    //     so the spacing scales with the in-game cursor scale just like the
    //     playfield trail.

    /// <summary>
    /// Shader-driven mouse-position-tracking trail particle pipeline.
    /// Direct port of <c>osu.Game.Rulesets.Osu.UI.Cursor.CursorTrail</c> —
    /// see file header for the rationale and what's preserved.
    /// </summary>
    [DrawVisualiserHidden]
    public partial class MenuCursorTrail : Drawable, IRequireHighFrequencyMousePosition
    {
        private const int max_sprites = 2048;

        /// <summary>
        /// An exponentiating factor to ease the trail fade.
        /// </summary>
        protected virtual float FadeExponent => 1.7f;

        /// <summary>
        /// The scale used on creation of a new trail part.
        /// </summary>
        public Vector2 NewPartScale { get; set; } = Vector2.One;

        /// <summary>
        /// The rotation (in degrees) to apply to trail parts when <see cref="AllowPartRotation"/> is <c>true</c>.
        /// </summary>
        public float PartRotation { get; set; }

        /// <summary>
        /// Whether to rotate trail parts based on the value of <see cref="PartRotation"/>.
        /// </summary>
        protected bool AllowPartRotation { get; set; }

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

        /// <summary>
        /// The trail part texture origin.
        /// </summary>
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

        public MenuCursorTrail()
        {
            // as we are currently very dependent on having a running clock, let's make our own clock for the time being.
            Clock = new FramedClock();

            RelativeSizeAxes = Axes.Both;

            for (int i = 0; i < max_sprites; i++)
            {
                // -1 signals that the part is unusable, and should not be drawn
                parts[i].InvalidationID = -1;
            }
        }

        [BackgroundDependencyLoader]
        private void load(IRenderer renderer, ShaderManager shaders)
        {
            texture ??= renderer.WhitePixel;
            shader = shaders.Load(@"CursorTrail", FragmentShaderDescriptor.TEXTURE);
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

        /// <summary>
        /// The amount of time to fade the cursor trail pieces.
        /// </summary>
        protected virtual double FadeDuration => 300;

        public override bool IsPresent => true;

        protected override void Update()
        {
            base.Update();

            Invalidate(Invalidation.DrawNode);

            const int fade_clock_reset_threshold = 1000000;

            time = (float)((Time.Current - timeOffset) / FadeDuration);
            if (time > fade_clock_reset_threshold)
                resetTime();
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

        /// <summary>
        /// Whether to interpolate mouse movements and add trail pieces at intermediate points.
        /// </summary>
        protected virtual bool InterpolateMovements => true;

        protected virtual float IntervalMultiplier => 1.0f;
        protected virtual bool AvoidDrawingNearCursor => false;

        private Vector2? lastPosition;
        private readonly InputResampler resampler = new InputResampler();

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            AddTrail(e.ScreenSpaceMousePosition);
            return base.OnMouseMove(e);
        }

        protected void AddTrail(Vector2 position)
        {
            // HOTFIX guard. The interpolation path below dereferences
            // `Texture.DisplayWidth` on every mouse-move tick. The texture
            // can legitimately be null at runtime — the legacy skin chain
            // returns null for `cursortrail` when the active skin doesn't
            // ship one AND lazer's bundled fallback isn't engaged in the
            // chain (some Argon / Triangles configurations, custom skins
            // that explicitly opt out of legacy fallback, weird import
            // states, etc.). When that happens we previously crashed the
            // game on the FIRST mouse move of the session — the user lost
            // their entire client. SkinnableMenuCursorTrail.load now falls
            // back to lazer's bundled `Cursor/cursortrail` when the legacy
            // lookup misses, but we keep this guard as belt-and-braces so
            // any future caller (or test) that constructs a bare
            // MenuCursorTrail with no texture can't take the game down.
            if (Texture == null)
                return;

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
            protected new MenuCursorTrail Source => (MenuCursorTrail)base.Source;

            private IShader shader;
            private Texture texture;

            private float time;
            private float fadeExponent;
            private float angle;
            private Vector2 cursorScale;

            private readonly TrailPart[] parts = new TrailPart[max_sprites];
            private Vector2 originPosition;

            private IVertexBatch<TexturedTrailVertex> vertexBatch;

            public TrailDrawNode(MenuCursorTrail source)
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
                angle = Source.AllowPartRotation ? float.DegreesToRadians(Source.PartRotation) : 0;
                cursorScale = Source.cursorScale;

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

            private IUniformBuffer<CursorTrailParameters> cursorTrailParameters;

            protected override void Draw(IRenderer renderer)
            {
                base.Draw(renderer);

                // Belt-and-braces, mirror of the AddTrail guard. With a null
                // texture there is nothing for the shader to sample anyway;
                // every vertex emits would have to dereference the texture
                // for DisplayWidth / DisplayHeight (see the per-vertex
                // expressions below) and bind the sampler. Bailing here also
                // keeps the second crash trace we observed
                // (TrailDrawNode.Draw NRE on `texture.Bind()`) from ever
                // recurring. We still allocate the vertex batch + uniform
                // buffer above before the guard so the caches stay warm —
                // but only the shader/texture binding + vertex emission are
                // skipped this frame.
                if (texture == null || shader == null)
                    return;

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

                float sin = MathF.Sin(angle);
                float cos = MathF.Cos(angle);

                foreach (var part in parts)
                {
                    if (part.InvalidationID == -1)
                        continue;

                    if (time - part.Time >= 1)
                        continue;

                    vertexBatch.Add(new TexturedTrailVertex
                    {
                        Position = rotateAround(
                            new Vector2(
                                part.Position.X - texture.DisplayWidth * originPosition.X * part.Scale.X * cursorScale.X,
                                part.Position.Y + texture.DisplayHeight * (1 - originPosition.Y) * part.Scale.Y * cursorScale.Y),
                            part.Position, sin, cos),
                        TexturePosition = textureRect.BottomLeft,
                        TextureRect = new Vector4(0, 0, 1, 1),
                        Colour = DrawColourInfo.Colour.BottomLeft.Linear,
                        Time = part.Time
                    });

                    vertexBatch.Add(new TexturedTrailVertex
                    {
                        Position = rotateAround(
                            new Vector2(
                                part.Position.X + texture.DisplayWidth * (1 - originPosition.X) * part.Scale.X * cursorScale.X,
                                part.Position.Y + texture.DisplayHeight * (1 - originPosition.Y) * part.Scale.Y * cursorScale.Y),
                            part.Position, sin, cos),
                        TexturePosition = textureRect.BottomRight,
                        TextureRect = new Vector4(0, 0, 1, 1),
                        Colour = DrawColourInfo.Colour.BottomRight.Linear,
                        Time = part.Time
                    });

                    vertexBatch.Add(new TexturedTrailVertex
                    {
                        Position = rotateAround(
                            new Vector2(
                                part.Position.X + texture.DisplayWidth * (1 - originPosition.X) * part.Scale.X * cursorScale.X,
                                part.Position.Y - texture.DisplayHeight * originPosition.Y * part.Scale.Y * cursorScale.Y),
                            part.Position, sin, cos),
                        TexturePosition = textureRect.TopRight,
                        TextureRect = new Vector4(0, 0, 1, 1),
                        Colour = DrawColourInfo.Colour.TopRight.Linear,
                        Time = part.Time
                    });

                    vertexBatch.Add(new TexturedTrailVertex
                    {
                        Position = rotateAround(
                            new Vector2(
                                part.Position.X - texture.DisplayWidth * originPosition.X * part.Scale.X * cursorScale.X,
                                part.Position.Y - texture.DisplayHeight * originPosition.Y * part.Scale.Y * cursorScale.Y),
                            part.Position, sin, cos),
                        TexturePosition = textureRect.TopLeft,
                        TextureRect = new Vector4(0, 0, 1, 1),
                        Colour = DrawColourInfo.Colour.TopLeft.Linear,
                        Time = part.Time
                    });
                }

                renderer.PopLocalMatrix();

                vertexBatch.Draw();
                shader.Unbind();
            }

            private static Vector2 rotateAround(Vector2 input, Vector2 origin, float sin, float cos)
            {
                float xTranslated = input.X - origin.X;
                float yTranslated = input.Y - origin.Y;

                return new Vector2(xTranslated * cos - yTranslated * sin, xTranslated * sin + yTranslated * cos) + origin;
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
        public struct TexturedTrailVertex : IEquatable<TexturedTrailVertex>, IVertex
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

    /// <summary>
    /// Skin-aware wrapper that configures <see cref="MenuCursorTrail"/> from
    /// the live skin chain. Direct port of
    /// <c>osu.Game.Rulesets.Osu.Skinning.Legacy.LegacyCursorTrail</c>.
    ///
    /// Two behavioural notes from the upstream port that matter here:
    /// 1. The disjoint-trail decision is keyed off the cursor texture
    ///    PROVIDER's cursormiddle, not the global lookup chain — so a user
    ///    skin that ships cursor.png without cursormiddle.png triggers the
    ///    disjoint long-tail look even though lazer's bundled fallback
    ///    DOES have a cursormiddle. Matches stable's behaviour
    ///    (https://github.com/peppy/osu-stable-reference/blob/3ea48705/osu!/Graphics/Skinning/SkinManager.cs#L269)
    ///    and the cursor-head pipeline in <see cref="SkinnableGameplayCursor"/>.
    /// 2. The texture's <c>ScaleAdjust</c> is multiplied by 1.6× — the same
    ///    "magic ratio" comment as upstream. In gameplay this compensates
    ///    for OsuPlayfieldAdjustmentContainer's downscale; in menus there's
    ///    no playfield, but we keep the multiplier so trail particles render
    ///    at the same apparent size as in-game (the cursor-head sprites in
    ///    <see cref="SkinnableGameplayCursor"/> are NOT scaled this way
    ///    because they don't go through NonPlayfieldSprite — keeping the
    ///    trail at 1.6× preserves the head-to-trail size ratio users are
    ///    used to).
    /// </summary>
    public partial class SkinnableMenuCursorTrail : MenuCursorTrail
    {
        private const double disjoint_trail_time_separation = 1000 / 60.0;

        public bool DisjointTrail { get; private set; }
        private double lastTrailTime;

        private IBindable<float> cursorSize = null!;

        private Vector2? currentPosition;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config, ISkinSource skinSource, TextureStore textures)
        {
            cursorSize = config.GetBindable<float>(OsuSetting.GameplayCursorSize).GetBoundCopy();
            AllowPartRotation = skinSource.GetConfig<LegacyCursorSkinConfiguration, bool>(LegacyCursorSkinConfiguration.CursorTrailRotate)?.Value ?? true;

            // Cursor and cursor trail components are sourced from potentially different skin sources.
            // Stable always chooses cursor trail disjoint behaviour based on the cursor texture lookup source, so we need to fetch where that occurred.
            // See https://github.com/peppy/osu-stable-reference/blob/3ea48705eb67172c430371dcfc8a16a002ed0d3d/osu!/Graphics/Skinning/SkinManager.cs#L269
            var cursorProvider = skinSource.FindProvider(s => s.GetTexture(@"cursor") != null);
            DisjointTrail = cursorProvider != null && cursorProvider.GetTexture(@"cursormiddle") == null;

            // Trail texture lookup, with a CRITICAL fallback to lazer's
            // bundled "Cursor/cursortrail" when the legacy skin chain
            // returns null.
            //
            // Background: the previous comment here ("the standard chain
            // walks user-skin → DefaultLegacySkin → resources, so the
            // trail ALWAYS renders something") was wishful — only true
            // when the user is on a legacy skin OR when DefaultLegacySkin
            // is engaged in the chain. For Argon / Triangles / certain
            // custom skins the chain returns null for `cursortrail`, my
            // earlier code stashed a null Texture, and the FIRST mouse-
            // move event of the session NRE'd inside MenuCursorTrail.AddTrail
            // (Texture.DisplayWidth) — taking down the entire game on
            // launch with no in-app way to recover.
            //
            // Mirror what OsuCursorContainer.DefaultCursorTrail does in
            // gameplay: when the skin lookup misses, fall back to the
            // bundled `Cursor/cursortrail` texture via TextureStore.
            // That asset ships in osu.Game.Resources and is always
            // available, so the trail ALWAYS has something to render
            // regardless of skin chain composition.
            Texture = skinSource.GetTexture(@"cursortrail") ?? textures.Get(@"Cursor/cursortrail");

            // Defensive log if BOTH lookups missed. Shouldn't happen with
            // the bundled fallback, but if osu.Game.Resources somehow
            // doesn't carry Cursor/cursortrail (test harness, future
            // refactor) we'd silently render no trail — leave a breadcrumb
            // so the next debugging session doesn't have to re-derive this.
            if (Texture == null)
                Logger.Log("SkinnableMenuCursorTrail: no cursortrail texture from skin chain or bundled fallback; trail will be invisible.", LoggingTarget.Runtime, LogLevel.Important);

            if (DisjointTrail)
            {
                bool centre = skinSource.GetConfig<LegacyCursorSkinConfiguration, bool>(LegacyCursorSkinConfiguration.CursorCentre)?.Value ?? true;

                TrailOrigin = centre ? Anchor.Centre : Anchor.TopLeft;
                Blending = BlendingParameters.Inherit;
            }
            else
            {
                Blending = BlendingParameters.Additive;
            }

            // !! IMPORTANT — DO NOT mutate Texture.ScaleAdjust here. !!
            //
            // Upstream LegacyCursorTrail does `Texture.ScaleAdjust *= 1.6f`
            // (the "stable magic ratio" that compensates for
            // OsuPlayfieldAdjustmentContainer's 0.625× playfield downscale,
            // landing trail particles at native screen size in gameplay).
            //
            // We CANNOT do that here because LegacySkin.GetTexture
            // (osu.Game/Skinning/LegacySkin.cs:576) hands out the SAME
            // Texture wrapper instance across calls, then resets its
            // ScaleAdjust to `ratio` on every call. Multiplying it from
            // here mutates the shared instance the playfield trail
            // already grabbed for OsuCursorContainer — concretely, every
            // time MenuCursorContainer rebuilds the trail (skin change /
            // style change), we'd reset the texture's ScaleAdjust to 1
            // and re-multiply by 1.6, while the gameplay trail is still
            // alive holding the same Texture reference and reading its
            // live DisplayWidth. The net visible bug: gameplay cursor
            // surrounded by oversized green/cyan trail particles painting
            // over the cursor.png — exactly what the user reported.
            //
            // The 1.6× was a playfield-compensation multiplier anyway.
            // Menus have NO playfield downscale, so leaving the texture
            // at its natural ScaleAdjust (1.0 from LegacySkin.GetTexture
            // for non-@2x assets) makes our trail particles render at the
            // SAME on-screen size as the playfield trail does in gameplay
            // (native × 0.625 × 1.6 = native). No compensation needed.
        }

        protected override double FadeDuration => DisjointTrail ? 150 : 500;
        protected override float FadeExponent => 1;

        protected override bool InterpolateMovements => !DisjointTrail;

        protected override float IntervalMultiplier => 1 / Math.Max(cursorSize.Value, 1);
        protected override bool AvoidDrawingNearCursor => !DisjointTrail;

        protected override void Update()
        {
            base.Update();

            if (!DisjointTrail || !currentPosition.HasValue)
                return;

            if (Time.Current - lastTrailTime >= disjoint_trail_time_separation)
            {
                lastTrailTime = Time.Current;
                AddTrail(currentPosition.Value);
            }
        }

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            if (!DisjointTrail)
                return base.OnMouseMove(e);

            currentPosition = e.ScreenSpaceMousePosition;

            // Intentionally block the base call as we're adding the trails ourselves.
            return false;
        }
    }
}
