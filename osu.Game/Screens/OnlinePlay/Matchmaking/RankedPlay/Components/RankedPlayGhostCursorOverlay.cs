// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Utils;
using osu.Game.Configuration;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.Multiplayer;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay.Components
{
    /// <summary>
    /// torii GHOST CURSOR: muestra el cursor del rival en las pantallas de ranked play (cartas,
    /// warmup, etc) como un fantasmita translucido con su username — y de paso manda el nuestro.
    ///
    /// Red: mandamos la posicion NORMALIZADA (0..1 sobre el espacio virtual compartido del
    /// <see cref="InverseScalingDrawSizePreservingFillContainer"/>) a ~30hz solo cuando se movio,
    /// fire-and-forget. El server la relayea tal cual por nombre de metodo (sin tipos MessagePack
    /// nuevos — compatible con clientes que no la conocen). Al recibir NO se dibuja crudo: se
    /// interpola con <see cref="Interpolation.DampContinuously"/> cada frame, asi se ve fluido a
    /// los hz que tenga TU pantalla (240/360/lo que sea) aunque lleguen 30 posiciones por segundo.
    ///
    /// Goofy a proposito: chocar tu cursor contra el fantasma con velocidad hace un shockwave con
    /// "pop" (pitch aleatorio) y lo manda volando con vuelta elastica; pasarle por arriba despacio
    /// lo hace saludar con un meneito. Cooldowns para que no sature. Todo el costo es local:
    /// pool de drawables para el trail, cero allocs por frame, y el overlay no participa del input.
    /// </summary>
    public partial class RankedPlayGhostCursorOverlay : CompositeDrawable
    {
        private const double send_interval_ms = 1000d / 30;

        /// <summary>Radio de "contacto" entre cursores, en unidades del espacio virtual.</summary>
        private const float bump_distance = 44f;

        /// <summary>Velocidad relativa minima (px/s) para que un contacto cuente como CHOQUE.</summary>
        private const float bump_speed = 480f;

        /// <summary>Por debajo de esta velocidad relativa, un contacto es un "hola" (wave), no un choque.</summary>
        private const float wave_speed = 160f;

        private const double bump_cooldown_ms = 700;
        private const double wave_cooldown_ms = 2200;

        /// <summary>Mientras este apagado no se manda ni se procesa nada (ej: gameplay encima).</summary>
        public bool Active { get; set; } = true;

        [Resolved]
        private MultiplayerClient client { get; set; } = null!;

        private readonly Dictionary<int, GhostCursor> ghosts = new Dictionary<int, GhostCursor>();

        private DrawablePool<TrailPiece> trailPool = null!;
        private Sample? bumpSample;
        private Sample? waveSample;

        private double sendAccumulator;
        private Vector2 lastSentNormalised = new Vector2(float.MinValue);

        private Vector2 lastLocalPosition;
        private Vector2 previousLocalPosition;
        private Vector2 localVelocity;
        private bool hasLocalPosition;

        // render-only: que el mouse pase derecho a las cartas de abajo.
        public override bool PropagatePositionalInputSubTree => false;
        public override bool HandlePositionalInput => false;

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            RelativeSizeAxes = Axes.Both;

            AddInternal(trailPool = new DrawablePool<TrailPiece>(24));

            bumpSample = audio.Samples.Get(@"UI/dialog-pop-in");
            waveSample = audio.Samples.Get(@"UI/cursor-tap");
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // llega en un thread de SignalR — SIEMPRE agendar al update thread.
            client.RankedPlayCursorReceived += onRemoteCursor;
            client.RoomUpdated += onRoomUpdated;
        }

        private void onRemoteCursor(int userId, Vector2 normalised) => Scheduler.Add(() =>
        {
            if (client.LocalUser?.UserID == userId)
                return;

            normalised = new Vector2(Math.Clamp(normalised.X, -0.05f, 1.05f), Math.Clamp(normalised.Y, -0.05f, 1.05f));

            if (!ghosts.TryGetValue(userId, out var ghost))
            {
                string username = client.Room?.Users.FirstOrDefault(u => u.UserID == userId)?.User?.Username ?? $"user {userId}";

                AddInternal(ghosts[userId] = ghost = new GhostCursor(userId, username)
                {
                    // aparece DONDE esta el rival, sin viajar desde (0,0).
                    Position = normalised * DrawSize,
                });
                ghost.FadeInFromZero(400, Easing.OutQuint);
            }

            ghost.TargetNormalised = normalised;
        });

        private void onRoomUpdated() => Scheduler.AddOnce(pruneGhosts);

        private void pruneGhosts()
        {
            foreach (var (userId, ghost) in ghosts)
            {
                if (client.Room?.Users.Any(u => u.UserID == userId) != true)
                {
                    ghosts.Remove(userId);
                    ghost.FadeOut(300, Easing.OutQuint).Expire();
                }
            }
        }

        protected override void Update()
        {
            base.Update();

            double elapsed = Time.Elapsed;

            if (DrawWidth <= 0 || DrawHeight <= 0)
                return;

            // ---- posicion + velocidad del cursor LOCAL (para envio y colisiones) ----
            var inputManager = GetContainingInputManager();

            if (inputManager != null)
            {
                Vector2 local = ToLocalSpace(inputManager.CurrentState.Mouse.Position);

                if (!hasLocalPosition)
                {
                    // primer frame: sin historia no hay velocidad ni segmento — evita un falso
                    // choque gigante desde (0,0).
                    hasLocalPosition = true;
                    lastLocalPosition = previousLocalPosition = local;
                }

                if (elapsed > 0)
                    localVelocity = (local - lastLocalPosition) / (float)elapsed * 1000f;

                previousLocalPosition = lastLocalPosition;
                lastLocalPosition = local;

                // ---- envio throttled (solo si Active y se movio de verdad) ----
                sendAccumulator += elapsed;

                if (Active && sendAccumulator >= send_interval_ms)
                {
                    sendAccumulator = 0;

                    Vector2 normalised = Vector2.Divide(local, DrawSize);

                    if (normalised.X is >= -0.1f and <= 1.1f && normalised.Y is >= -0.1f and <= 1.1f
                        && Vector2.DistanceSquared(normalised, lastSentNormalised) > 0.000004f)
                    {
                        lastSentNormalised = normalised;
                        client.SendRankedPlayCursor(normalised).FireAndForget();
                    }
                }
            }

            // ---- fantasmas: interpolar + trail + interacciones ----
            foreach (var (_, ghost) in ghosts)
            {
                ghost.UpdateInterpolation(DrawSize, elapsed);

                if (!Active)
                    continue;

                // mini-trail mientras se mueve rapido (pooled, sin allocs).
                if (ghost.Speed > 260 && Time.Current - ghost.LastTrailTime > 45)
                {
                    ghost.LastTrailTime = Time.Current;
                    AddInternal(trailPool.Get(p =>
                    {
                        p.Position = ghost.Position;
                        p.Colour = ghost.Accent;
                    }));
                }

                if (!hasLocalPosition)
                    continue;

                // distancia contra el SEGMENTO que recorrio el mouse este frame, no contra el punto:
                // un flick rapido avanza 30-50px por frame y "tunelea" por encima del fantasma si
                // solo miramos la posicion final — asi el choque registra siempre.
                float distance = distanceToSegment(ghost.Position, previousLocalPosition, lastLocalPosition);
                float relativeSpeed = (localVelocity - ghost.Velocity).Length;

                if (distance < bump_distance && relativeSpeed > bump_speed && Time.Current - ghost.LastBumpTime > bump_cooldown_ms)
                {
                    ghost.LastBumpTime = Time.Current;
                    triggerBump(ghost);
                }
                else if (distance < bump_distance * 0.8f && relativeSpeed < wave_speed && Time.Current - ghost.LastWaveTime > wave_cooldown_ms)
                {
                    ghost.LastWaveTime = Time.Current;
                    triggerWave(ghost);
                }
            }
        }

        private void triggerBump(GhostCursor ghost)
        {
            Vector2 delta = ghost.Position - lastLocalPosition;
            Vector2 normal = delta.LengthSquared > 0.001f ? delta.Normalized() : Vector2.UnitX;

            ghost.Bump(normal);

            // shockwave en el punto de contacto, del color del fantasma golpeado.
            AddInternal(new BumpRing(ghost.Accent) { Position = (ghost.Position + lastLocalPosition) / 2 });

            var channel = bumpSample?.GetChannel();

            if (channel != null)
            {
                // pitch aleatorio: cada choque suena un toque distinto = mas goofy.
                channel.Frequency.Value = 0.85 + RNG.NextDouble(0.4);
                channel.Play();
            }
        }

        private void triggerWave(GhostCursor ghost)
        {
            ghost.Wave();

            var channel = waveSample?.GetChannel();

            if (channel != null)
            {
                channel.Frequency.Value = 1.1 + RNG.NextDouble(0.2);
                channel.Volume.Value = 0.6;
                channel.Play();
            }
        }

        /// <summary>Distancia de un punto al segmento AB (para colisiones sin tunneling).</summary>
        private static float distanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.LengthSquared;

            if (lengthSquared < 0.0001f)
                return Vector2.Distance(point, b);

            float t = Math.Clamp(Vector2.Dot(point - a, ab) / lengthSquared, 0, 1);
            return Vector2.Distance(point, a + ab * t);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (client.IsNotNull())
            {
                client.RankedPlayCursorReceived -= onRemoteCursor;
                client.RoomUpdated -= onRoomUpdated;
            }

            base.Dispose(isDisposing);
        }

        /// <summary>
        /// El cursor fantasma en si: la posicion EXTERIOR la maneja la interpolacion frame a frame;
        /// los transforms goofy (knockback, squish, saludo) van sobre el contenedor INTERIOR
        /// <see cref="visual"/>, asi no pelean con la interpolacion de posicion.
        /// </summary>
        private partial class GhostCursor : CompositeDrawable
        {
            public Vector2 TargetNormalised;
            public Vector2 Velocity { get; private set; }
            public float Speed => Velocity.Length;

            public double LastTrailTime;
            public double LastBumpTime;
            public double LastWaveTime;

            private readonly string username;
            private Container visual = null!;

            // clon del MenuCursorContainer real: textura a tamanio natural, base_scale 0.15 x
            // MenuCursorSize, hotspot en el pixel de arriba a la izquierda. Asi el fantasma se ve
            // IDENTICO al cursor propio (usamos el setting local — el del rival no viaja).
            private const float base_scale = 0.15f;

            private Bindable<float> menuCursorSize = null!;
            private Container cursorScaleContainer = null!;

            /// <summary>
            /// Color estable por usuario: hue por golden ratio sobre el id — cada jugador tiene SU
            /// tono, siempre el mismo, y dos ids consecutivos caen bien lejos en la rueda de color.
            /// </summary>
            public readonly Colour4 Accent;

            public GhostCursor(int userId, string username)
            {
                this.username = username;
                Accent = Colour4.FromHSV((float)(userId * 0.618033988749895 % 1), 0.55f, 1f);
            }

            [BackgroundDependencyLoader]
            private void load(TextureStore textures, OsuConfigManager config)
            {
                // la Position ES la punta del cursor, igual que el real (TopLeft-anchored).
                Origin = Anchor.TopLeft;
                AutoSizeAxes = Axes.Both;
                Alpha = 0.3f;

                menuCursorSize = config.GetBindable<float>(OsuSetting.MenuCursorSize);

                InternalChild = visual = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        cursorScaleContainer = new Container
                        {
                            AutoSizeAxes = Axes.Both,
                            Child = new Sprite
                            {
                                Texture = textures.Get(@"Cursor/menu-cursor"),
                                Colour = Accent,
                            },
                        },
                        new OsuSpriteText
                        {
                            Text = username,
                            Colour = Accent,
                            Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                            Position = new Vector2(14, 26),
                            Shadow = true,
                        },
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                menuCursorSize.BindValueChanged(s => cursorScaleContainer.Scale = new Vector2(s.NewValue * base_scale), true);
            }

            public void UpdateInterpolation(Vector2 drawSize, double elapsed)
            {
                Vector2 target = TargetNormalised * drawSize;
                Vector2 previous = Position;

                // damp exponencial con half-life de 40ms (Interpolation.DampContinuously no tiene
                // overload Vector2): pega rapidito al target sin teleports, y como corre POR FRAME
                // e independiente del rate de red, se ve fluido a CUALQUIER refresh rate.
                float amount = (float)(1 - Math.Pow(0.5, elapsed / 40.0));
                Position = previous + (target - previous) * amount;

                Velocity = elapsed > 0 ? (Position - previous) / (float)elapsed * 1000f : Vector2.Zero;
            }

            /// <summary>Choque: sale volando en la normal del golpe, se aplasta, y vuelve con onda elastica.</summary>
            public void Bump(Vector2 normal)
            {
                visual.ClearTransforms();
                visual.MoveTo(normal * 46, 80, Easing.OutQuint)
                      .Then()
                      .MoveTo(Vector2.Zero, 900, Easing.OutElasticHalf);

                visual.ScaleTo(new Vector2(1.35f, 0.65f), 80, Easing.OutQuint)
                      .Then()
                      .ScaleTo(1, 700, Easing.OutElastic);

                this.FadeTo(0.55f, 60).Then().FadeTo(0.3f, 700, Easing.OutQuint);
            }

            /// <summary>Saludo: un meneito simpatico cuando el otro cursor te pasa por arriba despacio.</summary>
            public void Wave()
            {
                visual.ClearTransforms();
                visual.RotateTo(-16, 90, Easing.OutQuint)
                      .Then().RotateTo(14, 150, Easing.InOutSine)
                      .Then().RotateTo(-9, 130, Easing.InOutSine)
                      .Then().RotateTo(0, 260, Easing.OutBack);

                visual.ScaleTo(1.15f, 120, Easing.OutQuint)
                      .Then().ScaleTo(1, 320, Easing.OutBack);
            }
        }

        private partial class TrailPiece : PoolableDrawable
        {
            [BackgroundDependencyLoader]
            private void load()
            {
                Origin = Anchor.Centre;
                Size = new Vector2(9);
                InternalChild = new Circle
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.White,
                };
            }

            protected override void PrepareForUse()
            {
                base.PrepareForUse();

                this.FadeTo(0.16f).FadeOut(420, Easing.OutQuint);
                this.ScaleTo(1).ScaleTo(0.25f, 420, Easing.OutQuint);
                this.Delay(420).Expire();
            }
        }

        private partial class BumpRing : CompositeDrawable
        {
            public BumpRing(Colour4 accent)
            {
                Origin = Anchor.Centre;
                Size = new Vector2(34);
                InternalChild = new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    BorderThickness = 3.5f,
                    BorderColour = accent.Opacity(0.9f),
                    Child = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Alpha = 0,
                        AlwaysPresent = true,
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                this.ScaleTo(0.35f).ScaleTo(2.4f, 450, Easing.OutQuint);
                this.FadeTo(0.7f).FadeOut(450, Easing.OutQuint);
                this.Delay(450).Expire();
            }
        }
    }
}
