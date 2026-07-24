// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osu.Game.Cosmetics;
using osu.Game.Cosmetics.Definitions;
using osu.Game.Overlays;
using osuTK;

namespace Torii.CosmeticCreator.Editor
{
    /// <summary>
    /// torii: el "escenario" de preview en vivo. arma el trail desde la definicion data-driven (mismo
    /// runtime que ve el jugador) y lo mueve de tres formas:
    ///   - Sweep: un barrido lissajous automatico en loop (para juzgar la estela sin tocar nada).
    ///   - FollowMouse: el trail sigue TU cursor real dentro del area (como si estuvieras jugando).
    ///   - Gameplay: un cursor sintetico que reproduce un path osu-real (jumps + streams con easing) y
    ///     hace un "pop" en cada hit, para ver el cosmetico como se ve realmente jugando.
    /// cada edicion del panel llama <see cref="SetDefinition"/>, con un pequeno debounce para no
    /// reconstruir el drawable en cada tecla.
    /// </summary>
    public partial class TrailPreviewStage : CompositeDrawable, IRequireHighFrequencyMousePosition
    {
        public enum PreviewMode
        {
            Sweep,
            FollowMouse,
            Gameplay,
        }

        private Container trailLayer = null!;
        private Container cursorDot = null!;

        private ICosmeticTrail? trail;
        private CosmeticDefinition? pendingDefinition;
        private ScheduledDelegate? rebuildDebounce;

        private PreviewMode mode = PreviewMode.Sweep;
        private float speed = 1f;
        private bool paused;

        // ── gameplay sim ──────────────────────────────────────────────────
        private readonly List<GameplayNode> path = new List<GameplayNode>();
        private readonly Random rng = new Random();
        private int nodeIndex;
        private double simTime;

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

        // el stage captura input posicional siempre, asi el modo FollowMouse recibe OnMouseMove aunque el
        // trail de adentro tenga el input desactivado.
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

        /// <summary>rearma el preview con una definicion de trail nueva (con debounce).</summary>
        public void SetDefinition(CosmeticDefinition definition)
        {
            pendingDefinition = definition;

            // coalescemos ediciones rapidas (arrastrar un slider, tipear el nombre): reconstruir el
            // drawable en cada frame churnea texturas/vertex-batches y arriesga un load async sobre un
            // drawable ya dispuesto. 50ms es imperceptible y junta la rafaga en un solo rebuild.
            rebuildDebounce?.Cancel();
            rebuildDebounce = Scheduler.AddDelayed(rebuildNow, 50);
        }

        private void rebuildNow()
        {
            trailLayer.Clear();
            trail = null;

            var definition = pendingDefinition;
            if (definition == null || !CosmeticTrailFactory.CanBuild(definition))
                return;

            var drawable = CosmeticTrailFactory.Create(definition);
            trail = drawable as ICosmeticTrail;

            // en Sweep/Gameplay driveamos nosotros el cursor sintetico; en FollowMouse tambien driveamos
            // nosotros (desde OnMouseMove del stage), asi que el trail nunca escucha el mouse por su cuenta.
            trail?.SetInputActive(false);
            trail?.SetPaused(paused);
            if (mode != PreviewMode.Gameplay)
                trail?.SetClickExpansion(1f);

            trailLayer.Add(drawable);
            trail?.Reset();
        }

        public void SetMode(PreviewMode value)
        {
            mode = value;

            // limpiar la estela vieja para no dejar una raya cruzada al cambiar de modo, y resetear el
            // pop de click salvo que entremos a gameplay.
            trail?.Reset();
            if (mode != PreviewMode.Gameplay)
                trail?.SetClickExpansion(1f);
            else
                rebuildGameplayPath();
        }

        public PreviewMode Mode => mode;

        public void SetPaused(bool value)
        {
            paused = value;
            trail?.SetPaused(value);
        }

        public void SetSpeed(float value) => speed = value;

        public void ResetSweep() => trail?.Reset();

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            if (mode == PreviewMode.FollowMouse && trail != null && !paused)
            {
                cursorDot.Position = ToLocalSpace(e.ScreenSpaceMousePosition);
                // Drive espera SCREEN-space (hace ToLocalSpace adentro): le pasamos la posicion cruda.
                trail.Drive(e.ScreenSpaceMousePosition);
            }

            return base.OnMouseMove(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            // al salir del area cortamos la continuidad para que no dibuje una raya desde el borde cuando
            // el mouse vuelve a entrar.
            if (mode == PreviewMode.FollowMouse)
                trail?.Reset();
            base.OnHoverLost(e);
        }

        protected override void Update()
        {
            base.Update();

            if (trail == null || paused)
                return;

            switch (mode)
            {
                case PreviewMode.Sweep:
                    driveSweep();
                    break;

                case PreviewMode.Gameplay:
                    driveGameplay();
                    break;

                // FollowMouse se drivea desde OnMouseMove.
            }
        }

        private void driveSweep()
        {
            // barrido lissajous centrado: dos senos de frecuencias distintas cubren la caja sin repetir feo.
            float t = (float)(Time.Current / 1000.0) * speed;
            Vector2 size = DrawSize;
            Vector2 centre = size * 0.5f;
            float rx = size.X * 0.34f;
            float ry = size.Y * 0.30f;

            Vector2 local = centre + new Vector2(MathF.Sin(t * 1.10f) * rx, MathF.Sin(t * 1.70f) * ry);

            cursorDot.Position = local;
            trail.Drive(ToScreenSpace(local));
        }

        // ── gameplay sim ──────────────────────────────────────────────────

        private readonly struct GameplayNode
        {
            public readonly Vector2 PosN; // 0..1 dentro del area
            public readonly double TimeMs; // momento del hit (acumulado)

            public GameplayNode(Vector2 posN, double timeMs)
            {
                PosN = posN;
                TimeMs = timeMs;
            }
        }

        private void rebuildGameplayPath()
        {
            path.Clear();
            nodeIndex = 0;
            simTime = 0;

            const int nodes = 48;
            Vector2 p = new Vector2(0.5f);
            double t = 0;

            while (path.Count < nodes)
            {
                bool stream = rng.NextDouble() < 0.42;
                int burst = stream ? rng.Next(3, 7) : 1;

                for (int i = 0; i < burst && path.Count < nodes; i++)
                {
                    // saltos cortos (streams) o largos (jumps), en un angulo al azar, rebotando adentro
                    // del margen para no pegarse a los bordes.
                    float dist = stream ? lerp(0.05f, 0.12f) : lerp(0.28f, 0.6f);
                    float ang = (float)(rng.NextDouble() * Math.PI * 2);
                    Vector2 np = p + new Vector2(MathF.Cos(ang), MathF.Sin(ang)) * dist;
                    np = clampMargin(np, 0.12f, 0.88f);

                    double dt = stream ? lerp(80, 140) : lerp(200, 350);
                    t += dt;
                    path.Add(new GameplayNode(np, t));
                    p = np;
                }
            }
        }

        private void driveGameplay()
        {
            if (path.Count < 2)
            {
                rebuildGameplayPath();
                return;
            }

            simTime += Time.Elapsed * speed;

            while (nodeIndex < path.Count - 1 && simTime >= path[nodeIndex + 1].TimeMs)
                nodeIndex++;

            // llegamos al final -> loop con un path nuevo (fresco) y sin raya.
            if (nodeIndex >= path.Count - 1)
            {
                rebuildGameplayPath();
                trail.Reset();
                return;
            }

            var a = path[nodeIndex];
            var b = path[nodeIndex + 1];
            double segDur = b.TimeMs - a.TimeMs;
            float f = segDur <= 0 ? 1f : (float)Math.Clamp((simTime - a.TimeMs) / segDur, 0, 1);

            // easing de aim: sale rapido y frena al llegar a la nota (snap). OutCubic imita bien el aim.
            float eased = 1f - MathF.Pow(1f - f, 3f);
            Vector2 posN = Vector2.Lerp(a.PosN, b.PosN, eased);

            // micro-jitter perpendicular: el aim humano tiembla, la linea no es un segmento perfecto.
            Vector2 dir = b.PosN - a.PosN;
            if (dir.LengthSquared > 1e-6f)
            {
                Vector2 perp = new Vector2(-dir.Y, dir.X);
                perp = Vector2.Normalize(perp);
                posN += perp * MathF.Sin((float)simTime * 0.05f) * 0.008f;
            }

            Vector2 local = posN * DrawSize;
            cursorDot.Position = local;
            trail.Drive(ToScreenSpace(local));

            // pop de click: al llegar a cada nodo (hit) agrandamos la salida del trail con una envolvente
            // rapida que decae (~200ms), igual que el Expand() del cursor real.
            double clickAge = simTime - a.TimeMs;
            float k = (float)Math.Clamp(clickAge / 200.0, 0, 1);
            float pop = 1.4f * (1f - k) * MathF.Exp(-3f * k);
            trail.SetClickExpansion(1f + pop * 0.4f);
        }

        // ── helpers ───────────────────────────────────────────────────────

        private float lerp(float min, float max) => min + (float)rng.NextDouble() * (max - min);
        private double lerp(double min, double max) => min + rng.NextDouble() * (max - min);

        private static Vector2 clampMargin(Vector2 v, float lo, float hi)
        {
            // reflejar hacia adentro en vez de clampear duro, para que un jump que se pasa del borde
            // rebote y no se pegue al margen.
            float x = reflect(v.X, lo, hi);
            float y = reflect(v.Y, lo, hi);
            return new Vector2(x, y);
        }

        private static float reflect(float v, float lo, float hi)
        {
            if (v < lo) return lo + (lo - v);
            if (v > hi) return hi - (v - hi);
            return Math.Clamp(v, lo, hi);
        }
    }

    internal static class Color4Ext
    {
        public static readonly osuTK.Graphics.Color4 White = new osuTK.Graphics.Color4(255, 255, 255, 255);
    }
}
