// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Osu;
using osu.Game.Screens.Play;
using osu.Game.Skinning.Components;
using osu.Game.Tests.Beatmaps;

namespace osu.Game.Tests.Visual.Gameplay
{
    /// <summary>
    /// Torii: el contador de pausas del skin layout editor. Cada pausa que se
    /// agrega simula lo que hace <see cref="SubmittingPlayer"/> al pausar de
    /// verdad (agrega el timestamp a <c>ScoreInfo.Pauses</c>).
    /// </summary>
    public partial class TestScenePauseCountCounter : OsuTestScene
    {
        [Cached]
        private readonly GameplayState gameplayState = new GameplayState(
            new TestBeatmap(new OsuRuleset().RulesetInfo),
            new OsuRuleset());

        [SetUp]
        public void SetUp() => Schedule(() =>
        {
            gameplayState.Score.ScoreInfo.Pauses.Clear();

            Child = new PauseCountCounter
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Scale = new osuTK.Vector2(2),
            };
        });

        [Test]
        public void TestCounting()
        {
            AddStep("pause once", addPause);
            AddStep("pause again", addPause);
            AddStep("pause a third time", addPause);
            AddStep("clear pauses", () => gameplayState.Score.ScoreInfo.Pauses.Clear());
        }

        private void addPause()
            => gameplayState.Score.ScoreInfo.Pauses.Add(10_000 * (gameplayState.Score.ScoreInfo.Pauses.Count + 1));
    }
}
