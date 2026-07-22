// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.
// el HP graph del results legacy: la vida REAL de la play a lo largo del mapa, calculada
// re-jugando los hit events contra el HealthProcessor posta del ruleset (drain calibrado
// incluido) — headless, nunca se agrega al arbol. presentacion estilo stable: linea
// verde/roja con el corte en media vida, reveal de 4s, crosses de miss/50/100 clustereadas
// ("Nx" en pill) clavadas sobre la linea, y tooltip multi-linea con hit error + unstable
// rate como el hint de stable. si el beatmap no se puede convertir cae a una simulacion
// barata para que el graph nunca desaparezca.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Ranking
{
    public partial class LegacyHealthGraph : CompositeDrawable, IHasCustomTooltip<LegacyHealthGraph.TooltipData?>
    {
        private const int samples_count = 90;
        private const int marker_columns = 24;

        // margenes internos: con hp=1.0 la linea flota unos px adentro del marco en vez de
        // pegarse al borde (el "top glue" que se veia roto), y con hp=0 no toca el piso.
        private const float top_inset = 8;
        private const float bottom_inset = 6;

        private static readonly Vector2 region = new Vector2(186, 86) * 1.6f;

        private static readonly Color4 marker_miss = new Color4(231, 68, 58, 255);
        private static readonly Color4 marker_meh = new Color4(255, 163, 66, 255);
        private static readonly Color4 marker_ok = new Color4(109, 222, 73, 255);

        private readonly ScoreInfo score;
        private readonly bool animate;

        [Resolved]
        private Player? player { get; set; }

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        private Container? revealMask;
        private CancellationTokenSource? cancellation;

        public TooltipData? TooltipContent { get; private set; }

        public ITooltip<TooltipData?> GetCustomTooltip() => new LegacyHintTooltip();

        public LegacyHealthGraph(ScoreInfo score, bool animate)
        {
            this.score = score;
            this.animate = animate;
            Size = region;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // el tooltip es barato: se arma sincronico aca.
            double? unstableRate = score.HitEvents.CalculateUnstableRate()?.Result;
            var offsets = score.HitEvents.Where(e => e.Result.IsBasic() && e.Result.IsHit()).Select(e => e.TimeOffset).ToList();

            if (unstableRate != null && offsets.Count > 0)
            {
                TooltipContent = new TooltipData(
                    offsets.Where(o => o < 0).DefaultIfEmpty(0).Average(),
                    offsets.Where(o => o > 0).DefaultIfEmpty(0).Average(),
                    unstableRate.Value);
            }

            // la vida se computa off-thread (la conversion del beatmap puede ser pesada);
            // el beatmap post-mods del gameplay se captura ACA (update thread).
            var token = (cancellation = new CancellationTokenSource()).Token;
            var gameplayBeatmap = player?.GameplayState?.Beatmap;

            Task.Run(() => computeData(gameplayBeatmap, token), token).ContinueWith(t => Schedule(() =>
            {
                if (token.IsCancellationRequested || IsDisposed)
                    return;

                var data = t.IsCompletedSuccessfully ? t.GetResultSafely() : null;

                if (data != null)
                    buildContent(data);
            }));
        }

        #region computo

        private GraphData? computeData(IBeatmap? playable, CancellationToken token)
        {
            try
            {
                if (score.HitEvents.Count == 0)
                    return null;

                playable ??= beatmapManager.GetWorkingBeatmap(score.BeatmapInfo).GetPlayableBeatmap(score.Ruleset, score.Mods);

                if (playable.HitObjects.Count == 0)
                    return fallbackData();

                double drainStart = playable.HitObjects[0].StartTime;
                double gameplayEnd = playable.HitObjects[^1].GetEndTime();

                if (gameplayEnd <= drainStart)
                    return fallbackData();

                // el processor posta, como lo arma Player (mods tipo Classic traen el suyo).
                var processor = score.Mods.OfType<IApplicableHealthProcessor>().FirstOrDefault()?.CreateHealthProcessor(drainStart)
                                ?? score.Ruleset.CreateInstance().CreateHealthProcessor(drainStart);

                // sin esto el primer 0 HP prende HasFailed y ApplyResult ignora el resto:
                // el graph quedaria plano despues del primer casi-muerto.
                processor.Failed += () => false;
                processor.ApplyBeatmap(playable);

                double drainRate = (processor as DrainingHealthProcessor)?.DrainRate ?? 0;

                // los mismos periodos sin drain que arma DrainingHealthProcessor (breaks:
                // del fin del ultimo objeto previo al arranque del siguiente).
                var noDrain = new PeriodTracker(playable.Breaks.Select(b => new Period(
                    playable.HitObjects.Select(h => h.GetEndTime()).Where(t => t <= b.StartTime).DefaultIfEmpty(double.MinValue).Last(),
                    playable.HitObjects.Select(h => h.StartTime).Where(t => t >= b.EndTime).DefaultIfEmpty(double.MaxValue).First())));

                double[] samples = new double[samples_count];
                Array.Fill(samples, double.MaxValue);

                double cursor = drainStart;

                void record(double time)
                {
                    int i = (int)Math.Clamp((time - drainStart) / (gameplayEnd - drainStart) * (samples_count - 1), 0, samples_count - 1);
                    // minimo por bucket: preserva las V de los miss que un sampleo comun se come.
                    samples[i] = Math.Min(samples[i], processor.Health.Value);
                }

                void drainTo(double target)
                {
                    target = Math.Clamp(target, drainStart, gameplayEnd);

                    while (cursor < target)
                    {
                        double next = Math.Min(cursor + 50, target);

                        if (drainRate > 0 && !noDrain.IsInAny((cursor + next) / 2))
                            processor.Health.Value -= drainRate * (next - cursor);

                        record(next);
                        cursor = next;
                    }
                }

                record(drainStart);

                // re-jugamos los judgements EN ORDEN DE LISTA (el orden real de aplicacion:
                // los bonus de fin de combo del processor dependen de el, no reordenar).
                foreach (var e in score.HitEvents)
                {
                    token.ThrowIfCancellationRequested();

                    // los replays legacy convertidos pueden traer este result y ApplyResult tira.
                    if (e.Result == HitResult.LegacyComboIncrease)
                        continue;

                    double eventTime = e.HitObject.GetEndTime() + e.TimeOffset;

                    drainTo(eventTime);
                    processor.ApplyResult(new JudgementResult(e.HitObject, e.HitObject.Judgement) { Type = e.Result });
                    record(Math.Clamp(eventTime, drainStart, gameplayEnd));
                }

                drainTo(gameplayEnd);
                fillEmpty(samples);

                return new GraphData(samples, buildMarkers(drainStart, gameplayEnd));
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch
            {
                // beatmap corrupto / conversion fallida: mejor una simulacion barata que
                // un marco vacio.
                return fallbackData();
            }
        }

        /// <summary>
        /// simulacion barata (la de antes) para cuando el beatmap no esta disponible:
        /// sube con los aciertos, se desploma con los miss.
        /// </summary>
        private GraphData? fallbackData()
        {
            var basics = score.HitEvents.Where(e => e.Result.IsBasic()).OrderBy(e => e.HitObject.StartTime).ToList();

            if (basics.Count < 2)
                return null;

            double start = basics[0].HitObject.StartTime;
            double end = basics[^1].HitObject.StartTime;

            if (end <= start)
                return null;

            int n = basics.Count;
            double[] times = new double[n];
            double[] values = new double[n];
            double hp = 0.85;

            for (int i = 0; i < n; i++)
            {
                hp = Math.Clamp(hp + hpDelta(basics[i].Result), 0, 1);
                times[i] = basics[i].HitObject.StartTime;
                values[i] = hp;
            }

            double[] samples = new double[samples_count];
            int pointer = 0;

            for (int s = 0; s < samples_count; s++)
            {
                double t = start + (end - start) * s / (samples_count - 1);

                while (pointer < n - 1 && times[pointer + 1] <= t)
                    pointer++;

                samples[s] = values[pointer];
            }

            return new GraphData(samples, buildMarkers(start, end));
        }

        private List<Marker> buildMarkers(double domainStart, double domainEnd)
        {
            int[] missCount = new int[marker_columns];
            int[] mehCount = new int[marker_columns];
            int[] okCount = new int[marker_columns];
            double[] missTime = new double[marker_columns];
            double[] mehTime = new double[marker_columns];
            double[] okTime = new double[marker_columns];

            foreach (var e in score.HitEvents)
            {
                if (!e.Result.IsBasic())
                    continue;

                double t = e.HitObject.StartTime;
                int m = (int)Math.Clamp((t - domainStart) / (domainEnd - domainStart) * (marker_columns - 1), 0, marker_columns - 1);

                if (!e.Result.IsHit())
                {
                    missCount[m]++;
                    missTime[m] += t;
                }
                else if (e.Result == HitResult.Meh)
                {
                    mehCount[m]++;
                    mehTime[m] += t;
                }
                else if (e.Result == HitResult.Ok || e.Result == HitResult.Good)
                {
                    okCount[m]++;
                    okTime[m] += t;
                }
            }

            var markers = new List<Marker>();

            for (int m = 0; m < marker_columns; m++)
            {
                int count;
                double meanTime;
                int type;

                if (missCount[m] > 0)
                {
                    count = missCount[m];
                    meanTime = missTime[m] / count;
                    type = 0;
                }
                else if (mehCount[m] > 0)
                {
                    count = mehCount[m];
                    meanTime = mehTime[m] / count;
                    type = 1;
                }
                else if (okCount[m] > 0)
                {
                    count = okCount[m];
                    meanTime = okTime[m] / count;
                    type = 2;
                }
                else
                    continue;

                float xNorm = (float)Math.Clamp((meanTime - domainStart) / (domainEnd - domainStart), 0, 1);
                markers.Add(new Marker(xNorm, count, type));
            }

            return markers;
        }

        private static void fillEmpty(double[] samples)
        {
            double last = 1;

            for (int i = 0; i < samples.Length; i++)
            {
                if (samples[i] == double.MaxValue)
                    samples[i] = last;
                else
                    last = samples[i];
            }
        }

        private static double hpDelta(HitResult result)
        {
            switch (result)
            {
                case HitResult.Perfect:
                case HitResult.Great:
                    return 0.012;

                case HitResult.Good:
                    return 0.008;

                case HitResult.Ok:
                    return 0.005;

                case HitResult.Meh:
                    return 0.002;

                default:
                    return result.IsHit() ? 0.008 : -0.2;
            }
        }

        #endregion

        #region dibujo

        private void buildContent(GraphData data)
        {
            // suavizado corto para las pendientes suaves de stable.
            double[] smooth = new double[samples_count];

            for (int s = 0; s < samples_count; s++)
            {
                int lo = Math.Max(0, s - 1);
                int hi = Math.Min(samples_count - 1, s + 1);
                double sum = 0;

                for (int k = lo; k <= hi; k++)
                    sum += data.Samples[k];

                smooth[s] = sum / (hi - lo + 1);
            }

            Vector2 pointAt(int s) => new Vector2(region.X * s / (samples_count - 1), lineY(smooth[s]));

            // la linea con una pasada de sombra debajo (TODAS las sombras primero, sino la
            // sombra de un segmento pisa el color del anterior en las uniones).
            var shadows = new List<Drawable>(samples_count);
            var segments = new List<Drawable>(samples_count);

            for (int s = 1; s < samples_count; s++)
            {
                var a = pointAt(s - 1);
                var b = pointAt(s);
                var d = b - a;
                float rotation = MathF.Atan2(d.Y, d.X) * 180f / MathF.PI;

                shadows.Add(new Box
                {
                    Origin = Anchor.CentreLeft,
                    Position = a,
                    Size = new Vector2(d.Length + 1.6f, 7),
                    Rotation = rotation,
                    Colour = Color4.Black,
                    Alpha = 0.45f,
                });
                segments.Add(new Box
                {
                    Origin = Anchor.CentreLeft,
                    Position = a,
                    Size = new Vector2(d.Length + 1.6f, 3.2f),
                    Rotation = rotation,
                    Colour = smooth[s] > 0.5 ? Color4.YellowGreen : Color4.Red,
                });
            }

            var content = new List<Drawable>(shadows.Count + segments.Count + data.Markers.Count * 3);
            content.AddRange(shadows);
            content.AddRange(segments);

            foreach (var marker in data.Markers)
            {
                var colour = marker.Type switch
                {
                    0 => marker_miss,
                    1 => marker_meh,
                    _ => marker_ok,
                };

                float mx = Math.Clamp(marker.XNorm * region.X, 6, region.X - 6);
                int si = Math.Clamp((int)(marker.XNorm * (samples_count - 1)), 0, samples_count - 1);
                float my = Math.Clamp(lineY(smooth[si]), 7, region.Y - 7);

                // contorno negro + cross de color, clavadas sobre la linea.
                content.Add(new SpriteIcon
                {
                    Origin = Anchor.Centre,
                    Position = new Vector2(mx, my),
                    Size = new Vector2(12),
                    Icon = FontAwesome.Solid.Times,
                    Colour = Color4.Black,
                });
                content.Add(new SpriteIcon
                {
                    Origin = Anchor.Centre,
                    Position = new Vector2(mx, my),
                    Size = new Vector2(8.5f),
                    Icon = FontAwesome.Solid.Times,
                    Colour = colour,
                });

                if (marker.Count > 1)
                {
                    // pill oscuro con el borde del color del cluster; se flipea de lado/arriba
                    // cuando quedaria afuera del marco.
                    string label = $"{marker.Count}x";
                    float pillWidth = 12 + 7 * label.Length;
                    bool flipX = mx + 7.5f + pillWidth > region.X - 2;
                    bool flipY = my - 20 < 2;

                    content.Add(new Container
                    {
                        AutoSizeAxes = Axes.Both,
                        Origin = flipY ? (flipX ? Anchor.TopRight : Anchor.TopLeft) : (flipX ? Anchor.BottomRight : Anchor.BottomLeft),
                        Position = new Vector2(flipX ? mx - 7.5f : mx + 7.5f, flipY ? my + 5 : my - 5),
                        Masking = true,
                        CornerRadius = 7,
                        BorderThickness = 1.4f,
                        BorderColour = colour,
                        EdgeEffect = new EdgeEffectParameters
                        {
                            Type = EdgeEffectType.Shadow,
                            Colour = Color4.Black.Opacity(0.5f),
                            Radius = 3,
                        },
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(15, 15, 15, 255),
                                Alpha = 0.8f,
                            },
                            new OsuSpriteText
                            {
                                Text = label,
                                Font = LegacyFonts.Get(12.8f, FontWeight.Bold),
                                Margin = new MarginPadding { Horizontal = 4.5f, Vertical = 1 },
                                Shadow = false,
                            },
                        },
                    });
                }
            }

            InternalChild = revealMask = new Container
            {
                Masking = true,
                Size = new Vector2(animate ? 0 : region.X, region.Y),
                Children = content,
            };

            // el reveal arranca ACA (el contenido llega async; LoadComplete puede haber
            // corrido hace rato).
            if (animate)
                revealMask.Delay(300).ResizeWidthTo(region.X, 4000);
        }

        private static float lineY(double health) => top_inset + (float)(1 - health) * (region.Y - top_inset - bottom_inset);

        #endregion

        protected override void Dispose(bool isDisposing)
        {
            cancellation?.Cancel();
            base.Dispose(isDisposing);
        }

        private sealed record GraphData(double[] Samples, List<Marker> Markers);

        private readonly record struct Marker(float XNorm, int Count, int Type);

        public sealed record TooltipData(double NegAvg, double PosAvg, double UnstableRate);

        /// <summary>
        /// el hint boxeado de stable: "Accuracy:" + error medio + unstable rate, tres lineas
        /// en una cajita oscura. el TooltipContainer del juego lo clampea a la pantalla solo.
        /// </summary>
        public partial class LegacyHintTooltip : VisibilityContainer, ITooltip<TooltipData?>
        {
            private OsuSpriteText errorText = null!;
            private OsuSpriteText urText = null!;

            [BackgroundDependencyLoader]
            private void load()
            {
                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 3;
                BorderThickness = 1;
                BorderColour = Color4.White.Opacity(0.3f);

                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.Black,
                        Alpha = 0.8f,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Horizontal = 7, Vertical = 5 },
                        Spacing = new Vector2(0, 1),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = @"Accuracy:",
                                Font = LegacyFonts.Get(12.8f, FontWeight.Bold),
                            },
                            errorText = new OsuSpriteText
                            {
                                Font = LegacyFonts.Get(12.8f),
                                Colour = new Color4(255, 220, 120, 255),
                            },
                            urText = new OsuSpriteText
                            {
                                Font = LegacyFonts.Get(12.8f),
                                Colour = new Color4(255, 220, 120, 255),
                            },
                        },
                    },
                };
            }

            public void SetContent(TooltipData? content)
            {
                if (content == null)
                    return;

                errorText.Text = $"Error: {content.NegAvg:0.00}ms - +{content.PosAvg:0.00}ms avg";
                urText.Text = $"Unstable Rate: {content.UnstableRate:0.00}";
            }

            public void Move(Vector2 pos) => Position = pos;

            protected override void PopIn() => this.FadeIn(150, Easing.OutQuint);

            protected override void PopOut() => this.FadeOut(150, Easing.OutQuint);
        }
    }
}
