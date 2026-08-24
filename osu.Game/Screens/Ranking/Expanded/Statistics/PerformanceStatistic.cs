// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Audio;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Utils;
using osu.Game.Beatmaps;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Scoring;
using osu.Game.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Ranking.Expanded.Accuracy;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Ranking.Expanded.Statistics
{
    public partial class PerformanceStatistic : StatisticDisplay, IHasTooltip
    {
        public LocalisableString TooltipText { get; private set; }

        private readonly ScoreInfo score;

        private readonly Bindable<int> performance = new Bindable<int>();

        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        private SlashableStatisticCounter counter = null!;
        private Container content = null!;
        private Container counterArea = null!;
        private OsuSpriteText penaltyFly = null!;

        private DrawableSample? flySample;
        private DrawableSample? hitSample;

        // Torii: la penalizacion por pausas (0.93^n en el server) se comunica aca,
        // con la misma receta del danio de ranked play: el "-7%" aparece, se
        // queda un instante, VUELA hacia el numero, impacta (shake + particulas +
        // sample) y recien ahi el pp cae al valor real que otorga el server.
        // Sin esto el usuario ve un numero que no coincide con la web y nunca
        // aprende por que.
        private int basePerformance;
        private int finalPerformance;
        private int pauseCount;
        private bool eligibleForPp;
        private bool valueReady;
        private bool appeared;
        private bool revealStarted;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        public PerformanceStatistic(ScoreInfo score)
            : base(BeatmapsetsStrings.ShowScoreboardHeaderspp)
        {
            this.score = score;
        }

        [BackgroundDependencyLoader]
        private void load(BeatmapDifficultyCache difficultyCache, AudioManager audio, CancellationToken? cancellationToken)
        {
            // CreateContent corre durante el load de la clase BASE, antes de que los
            // [Resolved] de esta clase esten inyectados; los colores van aca.
            penaltyFly.Colour = colours.Red1;

            // los mismos samples del danio de ranked play. Si algun dia faltan en los
            // resources la animacion queda muda, pero jamas puede tirar la results screen.
            var fly = audio.Samples.Get(@"Multiplayer/Matchmaking/Ranked/Results/dmg-fly");
            var hit = audio.Samples.Get(@"Multiplayer/Matchmaking/Ranked/Results/dmg-hit");

            if (fly != null) AddInternal(flySample = new DrawableSample(fly));
            if (hit != null) AddInternal(hitSample = new DrawableSample(hit));

            if (score.PP.HasValue)
            {
                setPerformanceValue(score, score.PP.Value);
            }
            else
            {
                Task.Run(async () =>
                {
                    var attributes = await difficultyCache.GetDifficultyAsync(score.BeatmapInfo!, score.Ruleset, score.Mods, cancellationToken ?? CancellationToken.None).ConfigureAwait(false);
                    var performanceCalculator = score.Ruleset.CreateInstance().CreatePerformanceCalculator();

                    // Performance calculation requires the beatmap and ruleset to be locally available. If not, return a default value.
                    if (attributes?.DifficultyAttributes == null || performanceCalculator == null)
                        return;

                    var result = await performanceCalculator.CalculateAsync(score, attributes.Value.DifficultyAttributes, cancellationToken ?? CancellationToken.None).ConfigureAwait(false);

                    Schedule(() => setPerformanceValue(score, result.Total));
                }, cancellationToken ?? CancellationToken.None);
            }
        }

        private void setPerformanceValue(ScoreInfo scoreInfo, double? pp)
        {
            if (pp.HasValue)
            {
                pauseCount = scoreInfo.Pauses?.Count ?? 0;

                // Un pp que vino del server ya trae la penalizacion aplicada; el que
                // calculamos localmente no la conoce. Derivamos siempre los dos extremos
                // para poder animar completo -> penalizado, y para que el numero final
                // coincida con la web en ambos casos.
                if (scoreInfo.PP.HasValue)
                {
                    finalPerformance = (int)Math.Round(pp.Value, MidpointRounding.AwayFromZero);
                    basePerformance = (int)Math.Round(ToriiPausePenalty.Remove(pp.Value, pauseCount), MidpointRounding.AwayFromZero);
                }
                else
                {
                    basePerformance = (int)Math.Round(pp.Value, MidpointRounding.AwayFromZero);
                    finalPerformance = (int)Math.Round(ToriiPausePenalty.Apply(pp.Value, pauseCount), MidpointRounding.AwayFromZero);
                }

                if (!scoreInfo.BeatmapInfo!.Status.GrantsPerformancePoints())
                {
                    Alpha = 0.5f;
                    TooltipText = ResultsScreenStrings.NoPPForUnrankedBeatmaps;
                    eligibleForPp = false;
                }
                else if (hasUnrankedMods(scoreInfo))
                {
                    Alpha = 0.5f;
                    TooltipText = ResultsScreenStrings.NoPPForUnrankedMods;
                    eligibleForPp = false;
                }
                else if (scoreInfo.Rank == ScoreRank.F)
                {
                    Alpha = 0.5f;
                    TooltipText = ResultsScreenStrings.NoPPForFailedScores;
                    eligibleForPp = false;
                }
                else
                {
                    Alpha = 1f;
                    TooltipText = default;
                    eligibleForPp = true;
                }

                valueReady = true;
                tryBeginReveal();
            }
        }

        private static bool hasUnrankedMods(ScoreInfo scoreInfo)
        {
            IEnumerable<Mod> modsToCheck = scoreInfo.Mods;

            if (scoreInfo.IsLegacyScore)
                modsToCheck = modsToCheck.Where(m => m is not ModClassic);

            return modsToCheck.Any(m => !m.Ranked);
        }

        public override void Appear()
        {
            base.Appear();
            counter.Current.BindTo(performance);

            appeared = true;
            tryBeginReveal();
        }

        private void tryBeginReveal()
        {
            if (!appeared || !valueReady || revealStarted)
                return;

            revealStarted = true;

            bool slash = eligibleForPp && pauseCount > 0 && finalPerformance < basePerformance;

            if (!slash)
            {
                // Sin pausas (o sin pp real en juego): comportamiento de siempre.
                // Para un score no-elegible mostramos el valor guardado/calculado tal cual.
                performance.Value = eligibleForPp || !score.PP.HasValue ? basePerformance : finalPerformance;

                // Aunque no haya animacion, el tooltip educa igual si hubo pausas.
                if (pauseCount > 0 && eligibleForPp)
                    setPenaltyTooltip();
                return;
            }

            // 1. el contador sube hasta el pp completo, como el score total.
            performance.Value = basePerformance;
            setPenaltyTooltip();

            // 2. cuando termino de subir, arranca la secuencia de danio.
            Scheduler.AddDelayed(beginPenaltySequence, AccuracyCircle.ACCURACY_TRANSFORM_DURATION + 50);
        }

        private void setPenaltyTooltip()
        {
            TooltipText = LocalisableString.Interpolate(
                $"Paused {pauseCount}× during play — pp reduced 7% per pause: {basePerformance}pp → {finalPerformance}pp");
        }

        private LocalisableString penaltyLabel()
        {
            double percent = ToriiPausePenalty.TotalPercentLost(pauseCount);
            return pauseCount == 1
                ? LocalisableString.Interpolate($"-{percent:0.#}% (1 PAUSE)")
                : LocalisableString.Interpolate($"-{percent:0.#}% ({pauseCount} PAUSES)");
        }

        private void beginPenaltySequence()
        {
            const double hang_duration = 580;
            const double dive_duration = 260;

            penaltyFly.Text = penaltyLabel();

            // aparece flotando arriba del numero, con un pop.
            penaltyFly.MoveTo(new Vector2(0, -26))
                      .RotateTo(0)
                      .FadeInFromZero(160, Easing.OutQuint)
                      .ScaleTo(1.2f, 160, Easing.OutQuint)
                      .Then()
                      .ScaleTo(1f, 120, Easing.OutQuint);

            // se queda un instante y se lanza en picada contra el numero, acelerando,
            // igual que el texto de danio volador de ranked (InCubic + escala + giro).
            using (BeginDelayedSequence(hang_duration))
            {
                Schedule(() => flySample?.Play());

                penaltyFly.MoveTo(counterCentre(), dive_duration, Easing.InCubic)
                          .ScaleTo(0.7f, dive_duration, Easing.InCubic)
                          .RotateTo(-10, dive_duration, Easing.InCubic)
                          .Then()
                          .FadeOut();
            }

            Scheduler.AddDelayed(applyImpact, hang_duration + dive_duration);
        }

        private Vector2 counterCentre() => content.ToLocalSpace(counter.ScreenSpaceDrawQuad.Centre);

        private void applyImpact()
        {
            hitSample?.Play();

            // el golpe: shake del numero, flash rojo, y la caida rapida al valor real.
            counter.Shake(shakeDuration: 60, shakeMagnitude: 3, maximumLength: 120);

            counter.FadeColour(colours.Red1, 50, Easing.OutQuint)
                   .Then()
                   .FadeColour(Color4.White, 700, Easing.OutQuint);

            counter.FastRoll = true;
            performance.Value = finalPerformance;

            counterArea.ScaleTo(0.9f, 60, Easing.OutQuint)
                       .Then()
                       .ScaleTo(1f, 400, Easing.OutElasticHalf);

            // particulas fisicas del impacto, calcadas del danio de ranked.
            var impactPosition = counterCentre();

            for (int i = 0; i < 9; i++)
            {
                var particle = new DamageParticle
                {
                    Size = new Vector2(RNG.NextSingle(4, 10)),
                    Origin = Anchor.Centre,
                    Position = impactPosition,
                    Rotation = RNG.NextSingle(0, 360),
                    Blending = BlendingParameters.Additive,
                    Colour = Color4.White,
                    BypassAutoSizeAxes = Axes.Both,
                };

                content.Add(particle);

                particle.FadeOut(600)
                        .ScaleTo(0, 600)
                        .RotateTo(particle.Rotation + RNG.NextSingle(-20, 20), 600)
                        .FadeColour(colours.Red1, 600)
                        .Expire();
            }

            // la info persiste en el header de la columna ("PP" pasa a "PP -7% (1 PAUSE)"
            // en rojo): es espacio propio del pp, no lo tapa la fila de abajo ni mueve
            // el layout de las columnas vecinas.
            HeaderText.Text = LocalisableString.Interpolate($"PP {penaltyLabel()}");
            HeaderText.FadeColour(colours.Red1, 250, Easing.OutQuint);
            HeaderText.FadeOut(50).Then().FadeIn(250, Easing.OutQuint);
        }

        protected override void Dispose(bool isDisposing)
        {
            cancellationTokenSource.Cancel();
            base.Dispose(isDisposing);
        }

        protected override Drawable CreateContent() => content = new Container
        {
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 1),
                    Children = new Drawable[]
                    {
                        counterArea = new Container
                        {
                            Anchor = Anchor.TopCentre,
                            Origin = Anchor.TopCentre,
                            AutoSizeAxes = Axes.Both,
                            Child = counter = new SlashableStatisticCounter
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre
                            },
                        },
                    },
                },
                penaltyFly = new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.Centre,
                    Font = OsuFont.Torus.With(size: 14, weight: FontWeight.Bold),
                    Alpha = 0,
                    BypassAutoSizeAxes = Axes.Both,
                },
            },
        };

        /// <summary>
        /// El contador de siempre, pero con un modo "caida rapida" para cuando el
        /// impacto de pausas le baja el valor: la subida usa los 3 segundos estandar,
        /// la caida es un golpe corto.
        /// </summary>
        private partial class SlashableStatisticCounter : StatisticCounter
        {
            public bool FastRoll;

            protected override double RollingDuration => FastRoll ? 450 : base.RollingDuration;

            protected override Easing RollingEasing => FastRoll ? Easing.OutExpo : base.RollingEasing;
        }

        /// <summary>
        /// Particula fisica del impacto — triangulito con velocidad y gravedad,
        /// calcado del DamageParticle de la results screen de ranked play.
        /// </summary>
        private partial class DamageParticle : Triangle
        {
            private Vector2 velocity = new Vector2(RNG.NextSingle(-0.3f, 0.3f), RNG.NextSingle(-0.35f, 0.15f));

            private Vector2 gravity => new Vector2(0, 0.0004f);

            protected override void Update()
            {
                base.Update();

                velocity += gravity * (float)Time.Elapsed;
                Position += velocity * (float)Time.Elapsed;
            }
        }
    }
}
