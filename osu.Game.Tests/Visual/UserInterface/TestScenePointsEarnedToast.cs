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
            AddStep("top play (+100)", () => push(100, "top_play"));
            AddStep("daily first (+15)", () => push(15, "daily_play", "streak:1"));
            AddStep("daily streak x5 (+35)", () => push(35, "daily_play", "streak:5"));
            AddStep("gift (+500)", () => push(500, "gift"));
            AddStep("code redeemed (+1000)", () => push(1000, "access_code"));
            AddStep("unknown reason (+50)", () => push(50, "mystery"));
            AddStep("big number (+12,500)", () => push(12500, "top_play"));
            AddStep("reduced motion (+100)", () => push(100, "top_play", null, true));
            AddStep("burst: daily + top", () =>
            {
                push(15, "daily_play", "streak:3");
                push(100, "top_play");
            });
        }
    }
}
