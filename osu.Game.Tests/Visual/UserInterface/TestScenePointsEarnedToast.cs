// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Overlays.Cosmetics;
using osuTK;

namespace osu.Game.Tests.Visual.UserInterface
{
    /// <summary>
    /// Preview the "+N points" earned toast and its reason variants in the test
    /// browser, so the entrance / count-up animation can be tuned without having
    /// to actually earn points in game.
    /// </summary>
    [TestFixture]
    public partial class TestScenePointsEarnedToast : OsuTestScene
    {
        private FillFlowContainer<PointsEarnedToast> flow;

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(flow = new FillFlowContainer<PointsEarnedToast>
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 8),
                Y = 80,
            });
        }

        private void push(int amount, string reason, string reasonRef = null, bool reduced = false)
            => flow.Add(new PointsEarnedToast(amount, reason, reasonRef, reduced));

        [Test]
        public void TestReasons()
        {
            AddStep("top play veteran (228)", () => push(228, "top_play", "score:1|b:100|v:50|pp:78"));
            AddStep("top play newbie (8)", () => push(8, "top_play", "score:2|b:8|v:0|pp:0"));
            AddStep("top play developing (100)", () => push(100, "top_play", "score:3|b:40|v:0|pp:60"));
            AddStep("top play big #1 (353)", () => push(353, "top_play", "score:4|b:100|v:75|pp:178"));
            AddStep("daily first (+15)", () => push(15, "daily_play", "streak:1"));
            AddStep("daily streak x5 (+35)", () => push(35, "daily_play", "streak:5"));
            AddStep("gift (+500)", () => push(500, "gift"));
            AddStep("code redeemed (+1000)", () => push(1000, "access_code"));
            AddStep("unknown reason (+50)", () => push(50, "mystery"));
            AddStep("reduced motion (228)", () => push(228, "top_play", "score:5|b:100|v:50|pp:78", true));
            AddStep("burst: daily + top", () =>
            {
                push(15, "daily_play", "streak:3");
                push(228, "top_play", "score:6|b:100|v:50|pp:78");
            });
        }
    }
}
