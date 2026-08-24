// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Online;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Play;
using osu.Game.Screens.Ranking;
using osu.Game.Screens.Ranking.Expanded.Statistics;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.Ranking
{
    /// <summary>
    /// Torii: la animacion del "slash" de pausas en la result screen COMPLETA, tal
    /// como se ve al terminar una play — el pp sube hasta el valor completo junto
    /// con el score, y despues un corte rojo lo baja al valor real que otorga el
    /// server (-7% por pausa, compuesto).
    /// </summary>
    public partial class TestScenePausePpSlash : OsuTestScene
    {
        [Test]
        public void TestNoPauses()
        {
            AddStep("results without pauses", () => showResults(createScore(basePp: 412, pauses: 0)));
        }

        [Test]
        public void TestOnePause()
        {
            AddStep("results with 1 pause", () => showResults(createScore(basePp: 412, pauses: 1)));

            // espera el impacto (la secuencia entera tarda ~4s): garantiza que el camino
            // completo de la animacion corre tambien en el runner headless de CI.
            AddUntilStep("penalty shown in header", () => this.ChildrenOfType<PerformanceStatistic>()
                                                              .Any(st => st.ChildrenOfType<SpriteText>().Any(t => t.Text.ToString().Contains("PAUSE"))));
        }

        [Test]
        public void TestTwoPauses()
        {
            AddStep("results with 2 pauses", () => showResults(createScore(basePp: 412, pauses: 2)));
        }

        [Test]
        public void TestFivePauses()
        {
            AddStep("results with 5 pauses", () => showResults(createScore(basePp: 412, pauses: 5)));
        }

        private ScoreInfo createScore(double basePp, int pauses)
        {
            var score = TestResources.CreateTestScoreInfo();

            // el pp llega del server YA penalizado, igual que en una submission real.
            score.PP = ToriiPausePenalty.Apply(basePp, pauses);

            for (int i = 0; i < pauses; i++)
                score.Pauses.Add(10_000 * (i + 1));

            // condiciones para que el pp se muestre "de verdad" (sin el 50% de alpha):
            // mapa ranked, mods ranked, y un rank que no sea F.
            score.BeatmapInfo!.Status = BeatmapOnlineStatus.Ranked;
            score.Mods = new Mod[] { new OsuModHidden() };
            score.Rank = ScoreRank.S;

            return score;
        }

        private void showResults(ScoreInfo score) => Child = new PauseResultsContainer(new PauseTestResultsScreen(score));

        /// <summary>
        /// Mismo andamiaje que TestSceneResultsScreen: un Player cacheado y un
        /// screen stack de verdad, para que la results screen cargue con todo el
        /// flair de post-play (aplausos, paneles animados, score subiendo).
        /// </summary>
        private partial class PauseResultsContainer : Container
        {
            [Cached(typeof(Player))]
            public readonly Player Player = new TestPlayer();

            [Cached(typeof(UserStatisticsWatcher))]
            public readonly UserStatisticsWatcher UserStatisticsWatcher;

            public PauseResultsContainer(IScreen screen)
            {
                RelativeSizeAxes = Axes.Both;
                OsuScreenStack stack;

                InternalChildren = new Drawable[]
                {
                    UserStatisticsWatcher = new UserStatisticsWatcher(new LocalUserStatisticsProvider()),
                    stack = new OsuScreenStack
                    {
                        RelativeSizeAxes = Axes.Both,
                    }
                };

                stack.Push(screen);
            }
        }

        private partial class PauseTestResultsScreen : SoloResultsScreen
        {
            public PauseTestResultsScreen(ScoreInfo score)
                : base(score)
            {
                AllowRetry = true;
                IsLocalPlay = true;
            }

            // sin red: el leaderboard de al lado queda vacio, aca importa el panel.
            protected override Task<ScoreInfo[]> FetchScores() => Task.FromResult(Array.Empty<ScoreInfo>());
        }
    }
}
