// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Threading;
using osu.Framework.Utils;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Play
{
    /// <summary>
    /// Torii: aviso de que la cola de audio del modo exclusivo se vacio antes de
    /// entrar al mapa (ver <c>AudioThread.FlushExclusiveQueueNow</c>).
    ///
    /// Una pill que se desenrolla desde la esquina (borde derecho clavado), cuenta
    /// lo que esta pasando con texto explicito ("flushing audio queue" con el numero
    /// REAL de la cola drenando en vivo al lado), y cuando la latencia se asienta
    /// hace el seal: check verde con pop, rodillo de texto a "audio flushed", numero
    /// en verde, y la pill exhala al ancho corto antes de replegarse al punto.
    /// Coreografia y vocabulario tomados del motion work de ranked play.
    /// </summary>
    public partial class AudioLatencyFlushIndicator : CompositeDrawable
    {
        private const float pill_height = 26;
        private const float icon_x = 16;
        private const float label_x = 28;
        private const float ms_slot_width = 56;
        private const float ms_slot_margin = 10;
        private const float label_ms_gap = 8;

        // piso y techo del momento del seal: el piso deja terminar la entrada y leer
        // el numero; el techo garantiza que la timeline SIEMPRE termina aunque el
        // driver nunca se asiente.
        private const double settle_floor_ms = 1100;
        private const double settle_ceiling_ms = 2000;

        private Container pill = null!;
        private Container particleLayer = null!;
        private SpriteIcon syncIcon = null!;
        private SpriteIcon checkIcon = null!;
        private Container labelArea = null!;
        private OsuSpriteText flushingText = null!;
        private OsuSpriteText doneText = null!;
        private Container msSlot = null!;
        private OsuSpriteText msText = null!;

        private Sample? sealSample;

        private readonly IBindable<double> latency = new Bindable<double>();

        private ScheduledDelegate? settlePoll;
        private ScheduledDelegate? exitDelegate;

        private double showStart;
        private double lastPolledLatency;
        private int stablePolls;

        // el numero final muestra el MINIMO visto desde el flush (con ~ de aproximado):
        // es el piso real conseguido, no el valor del instante arbitrario del seal.
        private double minSeenSinceFlush = double.MaxValue;
        private bool frozen;

        private float widthFlushing = 230;
        private float widthDone = 185;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        public AudioLatencyFlushIndicator()
        {
            AutoSizeAxes = Axes.Both;
            Alpha = 0;

            // el badge vive con Alpha 0 y su coreografia arranca desde su propio
            // Scheduler: sin esto, un drawable no-presente nunca corre Update y el
            // Schedule de Show() queda encolado para siempre (nunca se ve nada).
            AlwaysPresent = true;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            InternalChildren = new Drawable[]
            {
                // fuera de la pill: adentro el masking decapitaria las particulas.
                particleLayer = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    BypassAutoSizeAxes = Axes.Both,
                    Depth = -1,
                },
                pill = new Container
                {
                    Anchor = Anchor.BottomRight,
                    Origin = Anchor.BottomRight,
                    Size = new Vector2(pill_height, pill_height),
                    Masking = true,
                    CornerRadius = pill_height / 2,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.Black,
                            Alpha = 0.7f,
                        },
                        syncIcon = new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.Centre,
                            X = icon_x,
                            Icon = FontAwesome.Solid.Sync,
                            Size = new Vector2(12),
                            Alpha = 0,
                            Scale = new Vector2(0.6f),
                        },
                        checkIcon = new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.Centre,
                            X = icon_x,
                            Icon = FontAwesome.Solid.CheckCircle,
                            Size = new Vector2(12),
                            Alpha = 0,
                            Scale = new Vector2(0.4f),
                        },
                        labelArea = new Container
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            X = label_x,
                            AutoSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                flushingText = new OsuSpriteText
                                {
                                    Text = @"flushing audio queue",
                                    Font = OsuFont.Torus.With(size: 13, weight: FontWeight.SemiBold),
                                    Shadow = false,
                                    Margin = new MarginPadding { Bottom = 1 },
                                    Alpha = 0,
                                    AlwaysPresent = true,
                                },
                                doneText = new OsuSpriteText
                                {
                                    Text = @"audio flushed",
                                    Font = OsuFont.Torus.With(size: 13, weight: FontWeight.SemiBold),
                                    Shadow = false,
                                    Margin = new MarginPadding { Bottom = 1 },
                                    Alpha = 0,
                                    Y = 4,
                                    AlwaysPresent = true,
                                    // el autosize del labelArea mide solo el texto activo;
                                    // este bypass se flipea en el seal (rodillo de texto).
                                    BypassAutoSizeAxes = Axes.Both,
                                },
                            },
                        },
                        msSlot = new Container
                        {
                            Anchor = Anchor.CentreRight,
                            Origin = Anchor.CentreRight,
                            Width = ms_slot_width,
                            RelativeSizeAxes = Axes.Y,
                            Margin = new MarginPadding { Right = ms_slot_margin },
                            Alpha = 0,
                            Child = msText = new OsuSpriteText
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                Font = OsuFont.Torus.With(size: 13, weight: FontWeight.SemiBold, fixedWidth: true),
                                Shadow = false,
                                Margin = new MarginPadding { Bottom = 1 },
                                AlwaysPresent = true,
                            },
                        },
                    },
                },
            };

            latency.BindTo(audio.OutputLatency);
            sealSample = audio.Samples.Get(@"UI/osd-change");
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // OutputLatency puede notificar desde el hilo de audio.
            latency.BindValueChanged(e => Schedule(() =>
            {
                if (IsDisposed)
                    return;

                if (e.NewValue > 0 && e.NewValue < minSeenSinceFlush)
                    minSeenSinceFlush = e.NewValue;

                if (!frozen)
                    msText.Text = formatMs(e.NewValue);
            }), true);
        }

        // tiers para que "104.3 ms" no reviente el slot fijo y "6.83" no baile.
        private static string formatMs(double v)
        {
            if (v >= 100) return $"{v:0} ms";
            if (v >= 10) return $"{v:0.#} ms";

            return $"{v:0.0} ms";
        }

        private static string formatMsApprox(double v)
        {
            if (v >= 100) return $"{v:0}~ ms";
            if (v >= 10) return $"{v:0.#}~ ms";

            return $"{v:0.0}~ ms";
        }

        /// <summary>
        /// Arranca la coreografia completa. Idempotente: una llamada re-entrante
        /// (retry rapido) resetea todo estado y re-coreografia desde cero.
        /// </summary>
        public void Show()
        {
            // FASE 0: reset explicito, nunca dos timelines peleando.
            cancelDelegates();
            ClearTransforms(true);
            particleLayer.Clear();

            pill.Width = pill_height;
            syncIcon.Alpha = 0;
            syncIcon.Scale = new Vector2(0.6f);
            syncIcon.Rotation = 0;
            checkIcon.Alpha = 0;
            checkIcon.Scale = new Vector2(0.4f);
            flushingText.Alpha = 0;
            flushingText.Y = 0;
            flushingText.BypassAutoSizeAxes = Axes.None;
            doneText.Alpha = 0;
            doneText.Y = 4;
            doneText.BypassAutoSizeAxes = Axes.Both;
            msSlot.Alpha = 0;
            msText.Colour = Color4.White;
            msText.Scale = Vector2.One;
            labelArea.Alpha = 1;
            Alpha = 0;

            double startValue = latency.Value;
            bool hasValue = startValue > 0 && !double.IsNaN(startValue);

            showStart = Time.Current;
            lastPolledLatency = startValue;
            stablePolls = 0;
            minSeenSinceFlush = hasValue ? startValue : double.MaxValue;
            frozen = false;

            // los anchos se miden del texto REAL (regla: el texto variable nunca se
            // hardcodea); un frame de Schedule para que las fuentes esten medidas.
            Schedule(() =>
            {
                if (flushingText.DrawWidth > 0)
                    widthFlushing = label_x + flushingText.DrawWidth + label_ms_gap + ms_slot_width + ms_slot_margin;
                if (doneText.DrawWidth > 0)
                    widthDone = label_x + doneText.DrawWidth + label_ms_gap + ms_slot_width + ms_slot_margin;

                // FASE 1: la pill se desenrolla hacia la izquierda desde un punto,
                // con el borde derecho clavado en la esquina.
                this.FadeIn(150, Easing.OutQuint);
                pill.ResizeWidthTo(widthFlushing, 500, Easing.OutExpo);

                // FASE 1b: poblado escalonado. El sync girando dice "laburando".
                syncIcon.Delay(100).FadeIn(200);
                syncIcon.Delay(100).ScaleTo(1f, 400, Easing.OutBack);
                syncIcon.Spin(1600, RotationDirection.Clockwise);

                flushingText.Delay(150).FadeIn(250, Easing.OutQuint);

                if (hasValue)
                    msSlot.Delay(250).FadeIn(250, Easing.OutQuint);

                // FASE 2: silencio coreografico. El dato real actua: el numero drena
                // solo. El poll decide cuando llega el seal.
                settlePoll = Scheduler.AddDelayed(pollSettle, 100, true);
            });
        }

        private void pollSettle()
        {
            double elapsed = Time.Current - showStart;

            if (elapsed < 900)
                return;

            double v = latency.Value;

            if (Math.Abs(v - lastPolledLatency) < 0.1)
                stablePolls++;
            else
                stablePolls = 0;

            lastPolledLatency = v;

            bool settled = stablePolls >= 2 && elapsed >= settle_floor_ms;
            bool timeUp = elapsed >= settle_ceiling_ms;

            if (!settled && !timeUp)
                return;

            settlePoll?.Cancel();
            settlePoll = null;

            performSeal();
        }

        private void performSeal()
        {
            // matar el Spin loop ANTES de animar la salida del icono: un loop vivo
            // pelea contra el FadeOut para siempre.
            syncIcon.ClearTransforms();

            var channel = sealSample?.GetChannel();

            if (channel != null)
            {
                channel.Volume.Value = 0.5;
                channel.Play();
            }

            // el engranaje cede el lugar; el check entra con el MISMO pop.
            syncIcon.FadeOut(120, Easing.OutQuint);
            syncIcon.ScaleTo(0.5f, 150, Easing.InQuint);
            checkIcon.FadeIn(150);
            checkIcon.ScaleTo(1f, 400, Easing.OutBack);

            // rodillo de texto: odometro vertical de 4px.
            flushingText.FadeOut(150, Easing.OutQuint);
            flushingText.MoveToY(-4, 200, Easing.OutQuint);
            flushingText.BypassAutoSizeAxes = Axes.Both;
            doneText.BypassAutoSizeAxes = Axes.None;
            doneText.FadeIn(200, Easing.OutQuint);
            doneText.MoveToY(0, 250, Easing.OutExpo);

            // un frame despues (el labelArea ya re-midio): la pill exhala al ancho corto.
            Schedule(() => pill.ResizeWidthTo(widthDone, 450, Easing.OutExpo));

            // el numero queda CONGELADO en el minimo conseguido, con ~ de aproximado.
            frozen = true;
            double best = Math.Min(minSeenSinceFlush, latency.Value > 0 ? latency.Value : double.MaxValue);
            if (best < double.MaxValue)
                msText.Text = formatMsApprox(best);

            // punch de absorcion en el numero; Origin CentreRight = crece hacia la
            // izquierda sin mover los digitos.
            msText.FadeColour(colours.Green1, 300, Easing.OutQuint);
            msText.ScaleTo(1.12f, 200, Easing.OutQuint).Then().ScaleTo(1f, 200);

            // tres particulitas verdes desde el check.
            for (int i = 0; i < 3; i++)
            {
                var particle = new FlushParticle
                {
                    Size = new Vector2(RNG.NextSingle(3, 6)),
                    Origin = Anchor.Centre,
                    Position = new Vector2(icon_x, pill_height / 2),
                    Rotation = RNG.NextSingle(0, 360),
                    Blending = BlendingParameters.Additive,
                    Colour = colours.Green1,
                };

                particleLayer.Add(particle);

                particle.FadeOut(450)
                        .ScaleTo(0, 450)
                        .Expire();
            }

            // FASE 4 (hold, quieto y legible) y FASE 5 (exit simetrico a la entrada:
            // fade corto, furl largo — la cápsula sigue colapsando ya invisible).
            exitDelegate = Scheduler.AddDelayed(() =>
            {
                checkIcon.FadeOut(150, Easing.OutQuint);
                labelArea.FadeOut(150, Easing.OutQuint);
                msSlot.FadeOut(150, Easing.OutQuint);

                exitDelegate = Scheduler.AddDelayed(() =>
                {
                    pill.ResizeWidthTo(pill_height, 400, Easing.OutExpo);
                    this.FadeOut(300, Easing.OutQuint);
                }, 80);
            }, 1400);
        }

        /// <summary>
        /// Interrupcion desde cualquier fase (el loader se va): colapso digno de
        /// 200ms, nunca un corte en seco. Cancela delegates y mata el Spin loop.
        /// </summary>
        public void HideNow()
        {
            cancelDelegates();
            ClearTransforms(true);
            particleLayer.Clear();

            this.FadeOut(150, Easing.OutQuint);
            pill.ResizeWidthTo(pill_height, 200, Easing.OutQuint);
        }

        private void cancelDelegates()
        {
            settlePoll?.Cancel();
            settlePoll = null;
            exitDelegate?.Cancel();
            exitDelegate = null;
        }

        private partial class FlushParticle : Triangle
        {
            private Vector2 velocity = new Vector2(RNG.NextSingle(-0.12f, 0.12f), RNG.NextSingle(-0.18f, 0.02f));

            private Vector2 gravity => new Vector2(0, 0.0003f);

            protected override void Update()
            {
                base.Update();

                velocity += gravity * (float)Time.Elapsed;
                Position += velocity * (float)Time.Elapsed;
            }
        }
    }
}
