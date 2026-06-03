// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps.Timing;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;
using osuTK.Input;

namespace osu.Game.Tests.Visual.Gameplay
{
    /// <summary>
    /// Visual + behavioural coverage for the Torii mid-map break skip
    /// (<see cref="SkipBreakOverlay"/>): the button arming, the first-press
    /// briefing handshake, and the single- vs double-press confirm paths.
    /// Drives a real <see cref="MasterGameplayClockContainer"/> +
    /// <see cref="BreakTracker"/> so timing matches the real game.
    /// </summary>
    [TestFixture]
    public partial class TestSceneSkipBreakOverlay : OsuManualInputManagerTestScene
    {
        private TestSkipBreakOverlay skipBreak;
        private BreakTracker breakTracker;
        private MasterGameplayClockContainer gameplayClockContainer;

        private readonly Bindable<bool> singleConfirmation = new BindableBool();
        private readonly Bindable<bool> briefingSeen = new BindableBool();

        private int requestCount;
        private int briefingCount;
        private double lastTarget;

        private static readonly BreakPeriod test_break = new BreakPeriod(2000, 14000);

        private static double skip_target => test_break.EndTime - BreakOverlay.BREAK_FADE_DURATION - SkipBreakOverlay.SKIP_LEAD_IN_MS;

        // briefingSeen defaults true + single-confirmation true so the simplest
        // tests get "one press = skip". Individual tests flip these first.
        private void createTest(bool seen = true, bool single = true) => AddStep("create test", () =>
        {
            requestCount = 0;
            briefingCount = 0;
            lastTarget = 0;
            briefingSeen.Value = seen;
            singleConfirmation.Value = single;

            var working = CreateWorkingBeatmap(CreateBeatmap(new OsuRuleset().RulesetInfo));

            // Build the container first so the break tracker can be pinned to
            // the SAME clock the overlay resolves (IGameplayClock = the
            // container). The real game does this too — BreakTracker/BreakOverlay
            // run off DrawableRuleset.FrameStableClock with ProcessCustomClock
            // = false. Without it the tracker falls back to the scene's
            // real-time clock and never lines up with the seeked gameplay
            // clock, so the button never becomes skippable.
            gameplayClockContainer = new MasterGameplayClockContainer(working, 0) { RelativeSizeAxes = Axes.Both };

            breakTracker = new BreakTracker(0, new ScoreProcessor(new OsuRuleset()))
            {
                Clock = gameplayClockContainer,
                ProcessCustomClock = false,
                Breaks = new List<BreakPeriod> { test_break },
            };

            skipBreak = new TestSkipBreakOverlay(breakTracker, singleConfirmation, briefingSeen)
            {
                RequestSkip = target =>
                {
                    requestCount++;
                    lastTarget = target;
                    gameplayClockContainer.Seek(target);
                },
                RequestBriefing = () => briefingCount++,
            };

            gameplayClockContainer.AddRange(new Drawable[] { breakTracker, skipBreak });

            Child = gameplayClockContainer;
            gameplayClockContainer.Start();
        });

        private void seek(string description, double time) =>
            AddStep(description, () => gameplayClockContainer.Seek(time));

        private void clickButton() => AddStep("click skip", () =>
        {
            InputManager.MoveMouseTo(skipBreak.ScreenSpaceDrawQuad.Centre);
            InputManager.Click(MouseButton.Left);
        });

        [Test]
        public void TestSinglePressSkips()
        {
            createTest(seen: true, single: true);

            seek("jump into break", test_break.StartTime + 500);
            AddUntilStep("button skippable", () => skipBreak.IsSkippable);

            clickButton();

            AddAssert("requested once", () => requestCount, () => Is.EqualTo(1));
            AddAssert("no briefing", () => briefingCount, () => Is.EqualTo(0));
            AddAssert("target is break end", () => lastTarget, () => Is.EqualTo(skip_target).Within(1));
            AddUntilStep("button gone after skip", () => !skipBreak.IsSkippable);
        }

        [Test]
        public void TestFirstPressShowsBriefingInsteadOfSkipping()
        {
            createTest(seen: false, single: false);

            seek("jump into break", test_break.StartTime + 500);
            AddUntilStep("button skippable", () => skipBreak.IsSkippable);

            clickButton();

            AddAssert("briefing requested", () => briefingCount, () => Is.EqualTo(1));
            AddAssert("did not skip", () => requestCount, () => Is.EqualTo(0));
            AddAssert("not armed", () => !skipBreak.IsConfirmArmed);
        }

        [Test]
        public void TestDoublePressConfirm()
        {
            createTest(seen: true, single: false);

            seek("jump into break", test_break.StartTime + 500);
            AddUntilStep("button skippable", () => skipBreak.IsSkippable);

            clickButton();
            AddAssert("armed after first press", () => skipBreak.IsConfirmArmed);
            AddAssert("not skipped yet", () => requestCount, () => Is.EqualTo(0));

            clickButton();
            AddAssert("skipped on second press", () => requestCount, () => Is.EqualTo(1));
            AddAssert("disarmed after skip", () => !skipBreak.IsConfirmArmed);
        }

        [Test]
        public void TestNoButtonBeforeBreak()
        {
            createTest();

            seek("jump before break", test_break.StartTime - 500);
            AddWaitStep("settle", 3);
            AddAssert("not skippable before break", () => !skipBreak.IsSkippable);
        }

        [Test]
        public void TestNoButtonNearBreakEnd()
        {
            createTest();

            seek("jump near break end", skip_target - (SkipBreakOverlay.MINIMUM_SKIP_SAVINGS - 500));
            AddWaitStep("settle", 3);
            AddAssert("not skippable near end", () => !skipBreak.IsSkippable);
        }

        [Test]
        public void TestKeyboardSkip()
        {
            createTest(seen: true, single: true);

            seek("jump into break", test_break.StartTime + 500);
            AddUntilStep("button skippable", () => skipBreak.IsSkippable);

            AddStep("press skip key", () => InputManager.Key(Key.Space));
            AddAssert("requested once", () => requestCount, () => Is.EqualTo(1));
        }

        private partial class TestSkipBreakOverlay : SkipBreakOverlay
        {
            public TestSkipBreakOverlay(BreakTracker breakTracker, Bindable<bool> singleConfirmation, Bindable<bool> briefingSeen)
                : base(breakTracker, singleConfirmation, briefingSeen)
            {
            }

            public Drawable OverlayContent => InternalChild;

            public Drawable FadingContent => (OverlayContent as Container)?.Child;
        }
    }
}
