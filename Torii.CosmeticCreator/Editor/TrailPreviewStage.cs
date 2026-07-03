// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Cosmetics;
using osu.Game.Cosmetics.Definitions;
using osu.Game.Overlays;
using osuTK;

namespace Torii.CosmeticCreator.Editor
{
    /// <summary>
    /// torii: el "escenario" de preview en vivo. reconstruye el trail desde la definicion data-driven
    /// y lo drivea con un cursor SINTETICO (barrido lissajous) para que deje su estela en loop, sin
    /// depender del mouse real. cada cambio en el editor llama <see cref="SetDefinition"/> y el preview
    /// se rearma al toque. es EXACTAMENTE el mismo trail que va a ver el jugador (mismo runtime).
    /// </summary>
    public partial class TrailPreviewStage : CompositeDrawable
    {
        private Container trailLayer = null!;
        private Container cursorDot = null!;

        private ICosmeticTrail? trail;

        private float speed = 1f;
        private bool paused;

        public TrailPreviewStage()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colours)
        {
            Masking = true;
            CornerRadius = 12;

            InternalChildren = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Background5 },
                trailLayer = new Container { RelativeSizeAxes = Axes.Both },
                cursorDot = new Container
                {
                    Size = new Vector2(10),
                    Origin = Anchor.Centre,
                    Child = new Circle { RelativeSizeAxes = Axes.Both, Colour = Color4Ext.White, Alpha = 0.9f },
                },
            };
        }

        /// <summary>rearma el preview con una definicion de trail nueva.</summary>
        public void SetDefinition(CosmeticDefinition definition)
        {
            trailLayer.Clear();
            trail = null;

            if (definition == null || !CosmeticTrailFactory.CanBuild(definition))
                return;

            var drawable = CosmeticTrailFactory.Create(definition);
            trail = drawable as ICosmeticTrail;
            trail?.SetInputActive(false); // seguimos SOLO el cursor sintetico, no el mouse real
            trailLayer.Add(drawable);
            trail?.Reset();
        }

        public void SetPaused(bool value)
        {
            paused = value;
            trail?.SetPaused(value);
        }

        public void SetSpeed(float value) => speed = value;

        public void ResetSweep() => trail?.Reset();

        protected override void Update()
        {
            base.Update();

            if (trail == null || paused)
                return;

            // barrido lissajous centrado en el escenario: dos senos de frecuencias distintas dan una
            // curva que cubre bien la caja sin repetirse feo, para juzgar la estela en movimiento.
            float t = (float)(Time.Current / 1000.0) * speed;
            Vector2 size = DrawSize;
            Vector2 centre = size * 0.5f;
            float rx = size.X * 0.34f;
            float ry = size.Y * 0.30f;

            Vector2 local = centre + new Vector2(MathF.Sin(t * 1.10f) * rx, MathF.Sin(t * 1.70f) * ry);

            cursorDot.Position = local;
            trail.Drive(ToScreenSpace(local));
        }
    }

    internal static class Color4Ext
    {
        public static readonly osuTK.Graphics.Color4 White = new osuTK.Graphics.Color4(255, 255, 255, 255);
    }
}
