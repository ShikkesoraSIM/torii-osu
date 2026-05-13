// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Input.Bindings;
using osuTK;
using osuTK.Graphics;
using osu.Game.Localisation;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Utils;

namespace osu.Game.Screens.Play
{
    public abstract partial class GameplayMenuOverlay : OverlayContainer, IKeyBindingHandler<GlobalAction>
    {
        protected const int TRANSITION_DURATION = 200;

        private const int button_height = 70;
        private const float background_alpha = 0.75f;

        // ─── Torii: confirm-on-long-attempts gating ──────────────────────────
        //
        // Threshold for considering an attempt "important enough that an
        // accidental Retry/Quit would be painful". Counted against active
        // gameplay time (pause-resume cycles don't accrue because
        // gameplayClock stops while paused), so a 60-second value matches
        // user intent regardless of how often they paused.
        //
        // Confirm window is the time after the first click during which a
        // second click will fire the original action. After it expires the
        // button silently disarms.
        private const double long_attempt_threshold_ms = 60_000;
        private const double confirm_window_ms = 5_000;

        // The single button currently "armed" (waiting for a confirm tap).
        // Shared across all dangerous buttons so that arming Quit while
        // Retry was already armed cancels Retry — only one prompt at a time
        // keeps the UI legible and makes "any other click cancels" cheap.
        private Button? armedButton;

        private Bindable<bool>? confirmDangerousButtons;

        protected override bool BlockScrollInput => false;

        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => true;

        public Action? OnResume { get; init; }
        public Action? OnRetry { get; init; }
        public Action? OnQuit { get; init; }

        /// <summary>
        /// Action that is invoked when <see cref="GlobalAction.Back"/> is triggered.
        /// </summary>
        protected virtual Action BackAction => () =>
        {
            // We prefer triggering the button click as it will animate...
            // but sometimes buttons aren't present (see FailOverlay's constructor as an example).
            if (Buttons.Any())
                Buttons.Last().TriggerClick();
            else
                OnQuit?.Invoke();
        };

        /// <summary>
        /// Action that is invoked when <see cref="GlobalAction.Select"/> is triggered.
        /// </summary>
        protected virtual Action SelectAction => () => InternalButtons.Selected?.TriggerClick();

        public abstract LocalisableString Header { get; }

        protected SelectionCycleFillFlowContainer<DialogButton> InternalButtons = null!;
        public IReadOnlyList<DialogButton> Buttons => InternalButtons;

        private TextFlowContainer playInfoText = null!;

        [Resolved]
        private GlobalActionContainer globalAction { get; set; } = null!;

        protected GameplayMenuOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours, OsuConfigManager? config)
        {
            // Optional so test scenes that mock the overlay don't have to
            // wire a config manager. Falls back to "always single-click" if
            // missing — same behaviour as upstream osu!.
            confirmDangerousButtons = config?.GetBindable<bool>(OsuSetting.ToriiConfirmDangerousButtonsOnLongAttempts);

            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = background_alpha,
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 50),
                    Origin = Anchor.Centre,
                    Anchor = Anchor.Centre,
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = Header,
                            Font = OsuFont.GetFont(size: 48),
                            Origin = Anchor.TopCentre,
                            Anchor = Anchor.TopCentre,
                            Colour = colours.Yellow,
                        },
                        InternalButtons = new SelectionCycleFillFlowContainer<DialogButton>
                        {
                            Origin = Anchor.TopCentre,
                            Anchor = Anchor.TopCentre,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Masking = true,
                            EdgeEffect = new EdgeEffectParameters
                            {
                                Type = EdgeEffectType.Shadow,
                                Colour = Color4.Black.Opacity(0.6f),
                                Radius = 50
                            },
                        },
                        playInfoText = new OsuTextFlowContainer(cp => cp.Font = OsuFont.GetFont(size: 18))
                        {
                            Origin = Anchor.TopCentre,
                            Anchor = Anchor.TopCentre,
                            TextAnchor = Anchor.TopCentre,
                            AutoSizeAxes = Axes.Both,
                        }
                    }
                },
            };

            if (OnResume != null)
                AddButton(GameplayMenuOverlayStrings.Continue, colours.Green, () => OnResume.Invoke());

            // Retry/Quit are the destructive actions: passing confirmable
            // makes them go through the arm-then-confirm flow when the
            // toggle is on AND the current attempt has crossed the
            // long_attempt_threshold_ms. Continue/Save Replay are explicitly
            // not confirmable — there's nothing to lose by clicking them.
            if (OnRetry != null)
                AddButton(GameplayMenuOverlayStrings.Retry, colours.YellowDark, () => OnRetry.Invoke(), confirmable: true);

            if (OnQuit != null)
                // Torii: fsyori swaps the hardcoded #AA1B27 quit-button
                // red for OsuColour.Red4 — which itself is theme-aware
                // (Torii: vivid red, grayscale: medium gray #707070).
                // Using colours.Red4 keeps the quit button "stays in
                // family" with the rest of the menu chrome on either
                // theme instead of hardcoding red into a grayscale UI.
                AddButton(GameplayMenuOverlayStrings.Quit, ThemeAware.Pick(new Color4(170, 27, 39, 255), colours.Red4), () => OnQuit.Invoke(), confirmable: true);

            State.ValueChanged += _ =>
            {
                InternalButtons.Deselect();
                // Hiding the overlay (e.g. user picks Continue) should reset
                // any armed state so the next show starts fresh.
                if (State.Value == Visibility.Hidden)
                    disarmCurrent();
            };

            updateInfoText();
        }

        private int retries;

        public int Retries
        {
            set
            {
                if (value == retries)
                    return;

                retries = value;

                if (IsLoaded)
                    updateInfoText();
            }
        }

        protected override void PopIn()
        {
            this.FadeIn(TRANSITION_DURATION, Easing.In);
            updateInfoText();
        }

        protected override void PopOut() => this.FadeOut(TRANSITION_DURATION, Easing.In);

        protected void AddButton(LocalisableString text, Color4 colour, Action? action, bool confirmable = false)
        {
            // Captured for the closure below so we can restore the label
            // when the button is disarmed (timeout, click on a different
            // button, or overlay hide).
            LocalisableString originalText = text;

            Button button = null!;
            button = new Button
            {
                Text = text,
                ButtonColour = colour,
                Origin = Anchor.TopCentre,
                Anchor = Anchor.TopCentre,
                Height = button_height,
                ConfirmableText = confirmable ? originalText : default,
                Action = delegate
                {
                    // Any click on a non-armed button cancels another button's
                    // pending arm. Keeps "press anywhere else to abort"
                    // intuitive without listening for global input.
                    if (armedButton != null && armedButton != button)
                        disarmCurrent();

                    if (confirmable && requiresLongAttemptConfirmation())
                    {
                        if (armedButton != button)
                        {
                            armButton(button);
                            return;
                        }

                        // Second click within the confirm window — fall
                        // through and actually invoke. Reset the visual state
                        // explicitly here (rather than relying on the
                        // overlay's Hide() to disarm) so the "Tap again to..."
                        // label and countdown bar don't linger through the
                        // fade-out animation.
                        button.SetArmed(false, 0, null);
                        armedButton = null;
                    }

                    action?.Invoke();
                    Hide();
                }
            };

            InternalButtons.Add(button);
        }

        /// <summary>
        /// Returns true when the current attempt has been running long enough
        /// (in active gameplay time, not wallclock) that an accidental
        /// destructive action is worth gating behind a second click.
        /// </summary>
        private bool requiresLongAttemptConfirmation()
        {
            if (confirmDangerousButtons?.Value != true)
                return false;

            // Without the gameplay clock we can't measure "how long" — fail
            // open (don't gate) so a config edge case never silently breaks
            // the existing one-click flow.
            if (gameplayClock == null || gameplayState == null)
                return false;

            (double firstHitTime, _) = gameplayState.Beatmap.CalculatePlayableBounds();
            double elapsed = gameplayClock.CurrentTime - firstHitTime;

            return elapsed >= long_attempt_threshold_ms;
        }

        private void armButton(Button button)
        {
            armedButton = button;
            button.SetArmed(true, confirm_window_ms, () =>
            {
                // Auto-disarm fires from the button itself (it owns the
                // schedule). Only clear our reference if it's still the
                // currently-armed one — guards against a fast disarm + arm
                // cycle racing with the scheduled callback.
                if (armedButton == button)
                    armedButton = null;
            });
        }

        private void disarmCurrent()
        {
            if (armedButton == null)
                return;

            armedButton.SetArmed(false, 0, null);
            armedButton = null;
        }

        public virtual bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            switch (e.Action)
            {
                case GlobalAction.SelectPrevious:
                    InternalButtons.SelectPrevious();
                    return true;

                case GlobalAction.SelectNext:
                    InternalButtons.SelectNext();
                    return true;

                case GlobalAction.Back:
                    BackAction.Invoke();
                    return true;

                case GlobalAction.Select:
                    SelectAction.Invoke();
                    return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        [Resolved]
        private IGameplayClock? gameplayClock { get; set; }

        [Resolved]
        private GameplayState? gameplayState { get; set; }

        private void updateInfoText()
        {
            playInfoText.Clear();
            playInfoText.AddText(GameplayMenuOverlayStrings.RetryCount);
            playInfoText.AddText(retries.ToString(), cp => cp.Font = cp.Font.With(weight: FontWeight.Bold));

            if (getSongProgress() is int progress)
            {
                playInfoText.NewLine();
                playInfoText.AddText(GameplayMenuOverlayStrings.SongProgress);
                playInfoText.AddText($"{progress}%", cp => cp.Font = cp.Font.With(weight: FontWeight.Bold));
            }

            if (gameplayState != null)
            {
                playInfoText.NewLine();
                playInfoText.AddText(BeatmapsetsStrings.ShowScoreboardHeadersAccuracy);
                playInfoText.AddText(": ");
                playInfoText.AddText(gameplayState!.ScoreProcessor.Accuracy.Value.FormatAccuracy(), cp => cp.Font = cp.Font.With(weight: FontWeight.Bold));
            }
        }

        private int? getSongProgress()
        {
            if (gameplayClock == null || gameplayState == null)
                return null;

            (double firstHitTime, double lastHitTime) = gameplayState.Beatmap.CalculatePlayableBounds();

            double playableLength = (lastHitTime - firstHitTime);

            if (playableLength == 0)
                return 0;

            return (int)Math.Clamp(((gameplayClock.CurrentTime - firstHitTime) / playableLength) * 100, 0, 100);
        }

        private partial class Button : DialogButton
        {
            // required to ensure keyboard navigation always starts from an extremity (unless the cursor is moved)
            protected override bool OnHover(HoverEvent e) => true;

            protected override bool OnMouseMove(MouseMoveEvent e)
            {
                State = SelectionState.Selected;
                return base.OnMouseMove(e);
            }

            // ─── Torii: arm-to-confirm visuals ───────────────────────────
            //
            // ConfirmableText is the original verb (Retry / Quit / ...) used
            // to build the "Tap again to {verb}" prompt when the button is
            // armed. Stored on the button rather than recomputed at click
            // time so the arm callback doesn't have to thread state back
            // through the overlay.
            public LocalisableString ConfirmableText { get; init; }

            private LocalisableString cachedOriginalText;
            private Box? confirmCountdownBar;
            private ScheduledDelegate? confirmExpireSchedule;

            /// <summary>
            /// Toggle the armed visual state. When <paramref name="armed"/> is
            /// true the label flips to "Tap again to {verb}" and a thin
            /// progress bar at the bottom edge of the button decays over
            /// <paramref name="windowMs"/> milliseconds. After the window
            /// expires <paramref name="onAutoDisarm"/> fires and the button
            /// reverts to its original text. Calling SetArmed(false) early
            /// cancels the schedule cleanly.
            /// </summary>
            public void SetArmed(bool armed, double windowMs, Action? onAutoDisarm)
            {
                // Always cancel the previous countdown — re-arming or
                // disarming should never leave a stale schedule alive.
                confirmExpireSchedule?.Cancel();
                confirmExpireSchedule = null;

                if (armed)
                {
                    // Stash the live label so disarm restores it verbatim
                    // (not the build-time text, which a subclass might have
                    // mutated for unrelated reasons).
                    cachedOriginalText = Text;
                    Text = GameplayMenuOverlayStrings.TapAgainToConfirm(ConfirmableText);

                    ensureCountdownBar();
                    confirmCountdownBar!.ClearTransforms();
                    confirmCountdownBar!.Width = 1f;
                    confirmCountdownBar!.FadeTo(0.85f, 80, Easing.OutQuint);
                    confirmCountdownBar!.ResizeWidthTo(0f, (float)windowMs, Easing.OutSine);

                    // Subtle pulse to telegraph "this changed, look here"
                    // without yanking attention. ScaleTo 1.04 keeps the
                    // button's origin still since both anchor and origin
                    // are TopCentre.
                    this.ScaleTo(1.04f, 90, Easing.OutQuint)
                        .Then().ScaleTo(1f, 220, Easing.OutQuint);

                    confirmExpireSchedule = Scheduler.AddDelayed(() =>
                    {
                        SetArmed(false, 0, null);
                        onAutoDisarm?.Invoke();
                    }, windowMs);
                }
                else
                {
                    if (cachedOriginalText != default)
                        Text = cachedOriginalText;

                    if (confirmCountdownBar != null)
                    {
                        confirmCountdownBar.ClearTransforms();
                        confirmCountdownBar.FadeOut(150, Easing.OutQuint);
                    }
                }
            }

            private void ensureCountdownBar()
            {
                if (confirmCountdownBar != null)
                    return;

                // Sits behind the foreground colour container so it reads as
                // part of the button rather than an overlaid widget. Anchored
                // bottom-left + Origin bottom-left + RelativeSizeAxes.X means
                // it shrinks toward the left as Width decays — natural
                // "draining" feedback for the 5-second window.
                Add(confirmCountdownBar = new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 4,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Colour = Color4.White,
                    Alpha = 0,
                    Blending = BlendingParameters.Additive,
                });
            }
        }

        protected override bool Handle(UIEvent e)
        {
            switch (e)
            {
                case ScrollEvent:
                    if (ReceivePositionalInputAt(e.ScreenSpaceMousePosition))
                        return globalAction.TriggerEvent(e);

                    break;
            }

            return base.Handle(e);
        }
    }
}
