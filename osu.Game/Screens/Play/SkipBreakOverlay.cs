// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osu.Framework.Utils;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Input.Bindings;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Play
{
    /// <summary>
    /// A SKIP affordance that appears DURING break periods, letting the player
    /// fast-forward to the end of the current break.
    ///
    /// Unlike <see cref="SkipOverlay"/> — which targets a single fixed
    /// intro/outro time and expires once that window passes — this overlay
    /// re-arms for every break in the map by watching
    /// <see cref="BreakTracker.CurrentPeriod"/>. The skip itself is a plain
    /// forward seek of the gameplay clock (wired up by <see cref="Player"/>),
    /// which is score-neutral: breaks contain no hit objects and don't drain
    /// health, so seeking from break start to break end skips nothing the
    /// player would have interacted with.
    ///
    /// The button visual (triangles + chevrons + "SKIP" label) and the
    /// pop-in / auto-hide behaviour are reused verbatim from
    /// <see cref="SkipOverlay"/> so the two skip affordances are
    /// indistinguishable to the player.
    /// </summary>
    public partial class SkipBreakOverlay : Container, IKeyBindingHandler<GlobalAction>
    {
        /// <summary>
        /// Invoked with the absolute gameplay time the clock should seek to
        /// when the player confirms a break skip. <see cref="Player"/> owns
        /// the actual seek (and its guards) — this overlay only decides
        /// "skip now, to here".
        /// </summary>
        public Action<double> RequestSkip;

        /// <summary>
        /// Invoked the very first time the player presses the skip button,
        /// before they've ever seen the explanatory briefing. <see cref="Player"/>
        /// soft-pauses gameplay and shows the one-time popup; the press that
        /// triggered it does NOT skip. Once the briefing has been dismissed
        /// (<see cref="briefingSeen"/> flips true), this is never called again
        /// and presses fall through to the normal skip / double-press path.
        /// </summary>
        public Action RequestBriefing;

        /// <summary>
        /// A break only shows a skip button if fast-forwarding would save at
        /// least this many milliseconds. Filters out short breaks where a
        /// skip would barely advance the clock (and where the button would
        /// flash in and out distractingly). The "giga breaks" this feature
        /// targets are far longer than this floor.
        /// </summary>
        public const double MINIMUM_SKIP_SAVINGS = 2000;

        /// <summary>
        /// Extra lead-in (ms) left before the break's natural end when
        /// skipping, so the player lands with room to breathe and react
        /// rather than getting dropped straight onto the first post-break
        /// note. Bumped to 2s after players reported skips landing so close to
        /// the first notes that they couldn't react and dropped them (lost
        /// plays). With the break-end fade on top, this leaves well over 2s
        /// before the first hit object resumes.
        /// </summary>
        public const double SKIP_LEAD_IN_MS = 2000;

        /// <summary>
        /// How long (ms) a first press stays "armed" waiting for the
        /// confirming second press in double-press mode. After this the
        /// button disarms and the next press arms again.
        /// </summary>
        private const double confirm_window_ms = 2500;

        /// <summary>
        /// Total number of successful break skips performed this play. Exposed
        /// mainly for tests / telemetry parity with <see cref="SkipOverlay.SkipCount"/>.
        /// </summary>
        public int SkipCount { get; private set; }

        private readonly BreakTracker breakTracker;

        // Single press skips immediately when true; otherwise a confirming
        // double press is required. Bound to the config setting by Player.
        private readonly Bindable<bool> singleConfirmation;

        // Whether the one-time skip briefing has already been shown. The
        // overlay only READS this; Player flips it true when the briefing is
        // dismissed (it owns the config write).
        private readonly Bindable<bool> briefingSeen;

        private readonly Bindable<Period?> currentPeriod = new Bindable<Period?>();

        private SkipOverlay.FadeContainer fadingContent;
        private SkipOverlay.Button button;
        private SkipOverlay.ButtonContainer buttonContainer;
        private Circle remainingTimeBox;
        private OsuSpriteText confirmHint;

        /// <summary>Whether the active break currently has enough time left to be worth skipping.</summary>
        private readonly BindableBool canSkip = new BindableBool();

        /// <summary>The absolute gameplay time a confirmed skip should seek to, for the active break.</summary>
        private double currentSkipTarget;

        /// <summary>True between the first and second press of a double-press confirm.</summary>
        private bool confirmArmed;

        private ScheduledDelegate disarmDelegate;

        [Resolved]
        private IGameplayClock gameplayClock { get; set; }

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

        /// <summary>Whether the active break currently offers a skippable window. Test seam.</summary>
        internal bool IsSkippable => canSkip.Value;

        /// <summary>Whether the button is armed waiting for a confirming second press. Test seam.</summary>
        internal bool IsConfirmArmed => confirmArmed;

        public SkipBreakOverlay(BreakTracker breakTracker, Bindable<bool> singleConfirmation, Bindable<bool> briefingSeen)
        {
            this.breakTracker = breakTracker;
            this.singleConfirmation = singleConfirmation;
            this.briefingSeen = briefingSeen;

            RelativePositionAxes = Axes.Both;
            RelativeSizeAxes = Axes.X;

            Position = new Vector2(0.5f, 0.7f);
            Size = new Vector2(1, 100);

            Origin = Anchor.Centre;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            InternalChildren = new Drawable[]
            {
                buttonContainer = new SkipOverlay.ButtonContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = fadingContent = new SkipOverlay.FadeContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            button = new SkipOverlay.Button
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Enabled = { BindTarget = canSkip },
                            },
                            remainingTimeBox = new Circle
                            {
                                Height = 5,
                                Anchor = Anchor.BottomCentre,
                                Origin = Anchor.BottomCentre,
                                Colour = colours.Orange3,
                                RelativeSizeAxes = Axes.X,
                            }
                        }
                    }
                },
                // "press again" prompt shown only while armed for a confirming
                // second press. Lives outside the auto-hiding FadeContainer so
                // it stays put for the whole confirm window. Sits just above
                // the button.
                confirmHint = new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.BottomCentre,
                    Y = -34,
                    Text = "press again to skip",
                    Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                    Colour = colours.Orange1,
                    Alpha = 0,
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            button.Action = attemptSkip;

            currentPeriod.BindTo(breakTracker.CurrentPeriod);
            currentPeriod.BindValueChanged(onPeriodChanged, true);
        }

        private void onPeriodChanged(ValueChangedEvent<Period?> period)
        {
            // A fresh break just became active — pop the button up so the
            // player notices it without having to wiggle the mouse first.
            // The FadeContainer auto-hides again after a second; the skip
            // key still works while it's faded (it checks Enabled, not
            // visibility), matching the intro skip's behaviour.
            if (period.NewValue != null && isWorthSkipping(period.NewValue.Value))
                fadingContent.TriggerShow();
        }

        /// <summary>
        /// The clock time a skip should land on for the given break. Starts
        /// from the tracker's period end (already the real break end minus the
        /// break-end fade, <see cref="BreakOverlay.BREAK_FADE_DURATION"/>) and
        /// backs off a further <see cref="SKIP_LEAD_IN_MS"/> so the player gets
        /// a moment to settle before the first post-break note instead of
        /// landing right on top of it.
        /// </summary>
        private static double skipTargetFor(Period period) => period.End - SKIP_LEAD_IN_MS;

        private bool isWorthSkipping(Period period) => skipTargetFor(period) - gameplayClock.CurrentTime > MINIMUM_SKIP_SAVINGS;

        protected override void Update()
        {
            base.Update();

            var period = currentPeriod.Value;

            if (period == null)
            {
                canSkip.Value = false;
                buttonContainer.State.Value = Visibility.Hidden;
                return;
            }

            double target = skipTargetFor(period.Value);
            double now = gameplayClock.CurrentTime;

            currentSkipTarget = target;

            // Progress runs 1 → 0 across the skippable portion of the break,
            // shrinking the orange bar the same way the intro skip does.
            double span = target - period.Value.Start;
            double progress = span <= 0 ? 0 : Math.Clamp((target - now) / span, 0, 1);

            remainingTimeBox.Width = (float)Interpolation.DampContinuously(remainingTimeBox.Width, progress, 40, Math.Abs(Time.Elapsed));

            canSkip.Value = target - now > MINIMUM_SKIP_SAVINGS;
            buttonContainer.State.Value = canSkip.Value ? Visibility.Visible : Visibility.Hidden;

            // If the window closed (break ending, or seek already past it)
            // while we were armed, drop the confirm state so a stale "press
            // again" prompt can't linger into the next break.
            if (confirmArmed && !canSkip.Value)
                disarmConfirm();
        }

        private void attemptSkip()
        {
            if (!canSkip.Value)
                return;

            // Re-read the live target so we never seek backwards even if the
            // click landed a frame after the clock crossed it.
            if (currentSkipTarget - gameplayClock.CurrentTime <= 0)
                return;

            // First time ever: educate instead of skipping. Player pauses and
            // shows the one-time briefing; this press is "spent" on discovery.
            if (!briefingSeen.Value)
            {
                disarmConfirm();
                RequestBriefing?.Invoke();
                return;
            }

            // Single-confirmation mode (opt-in): one press is enough.
            if (singleConfirmation.Value)
            {
                performSkip();
                return;
            }

            // Default: require a confirming double press. The first press arms
            // and shows "press again"; the second within the window skips.
            if (confirmArmed)
                performSkip();
            else
                armConfirm();
        }

        private void performSkip()
        {
            disarmConfirm();
            SkipCount++;
            RequestSkip?.Invoke(currentSkipTarget);
        }

        private void armConfirm()
        {
            confirmArmed = true;
            confirmHint.FadeIn(120, Easing.OutQuint);
            // Keep the button on-screen for the whole window so the second
            // press has something to land on without a mouse wiggle.
            fadingContent.TriggerShow();

            disarmDelegate?.Cancel();
            disarmDelegate = Scheduler.AddDelayed(disarmConfirm, confirm_window_ms);
        }

        private void disarmConfirm()
        {
            confirmArmed = false;
            disarmDelegate?.Cancel();
            disarmDelegate = null;
            confirmHint.FadeOut(160, Easing.OutQuint);
        }

        protected override bool OnMouseMove(MouseMoveEvent e)
        {
            if (canSkip.Value && !e.HasAnyButtonPressed)
                fadingContent.TriggerShow();

            return base.OnMouseMove(e);
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Repeat)
                return false;

            switch (e.Action)
            {
                case GlobalAction.SkipCutscene:
                    if (!button.Enabled.Value)
                        return false;

                    button.TriggerClick();
                    return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }
    }
}
