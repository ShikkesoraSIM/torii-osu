// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Graphics.Containers;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Mods;
using osu.Game.Screens.Menu;
using osu.Game.Skinning;
using osu.Game.Skinning.Select;
using osuTK;

namespace osu.Game.Screens.Footer
{
    public partial class ScreenFooter : OverlayContainer
    {
        public ScreenBackButton BackButton { get; private set; } = null!;

        /// <summary>
        /// Called when logo tracking begins, intended to bring the osu! logo to the frontmost visually.
        /// </summary>
        public Action<bool>? RequestLogoInFront { private get; init; }

        /// <summary>
        /// The back button was pressed.
        /// </summary>
        public Action? BackButtonPressed { private get; init; }

        public const int HEIGHT = 50;

        private const int padding = 60;
        private const float delay_per_button = 30;
        private const double transition_duration = 500;

        // Disable masking because it breaks due to the height of this container being less than the displayed content.
        // The height being set as it is is required for transition purposes.
        public override bool UpdateSubTreeMasking() => false;

        private readonly List<OverlayContainer> overlays = new List<OverlayContainer>();

        private Box background = null!;
        private GridContainer buttonsGrid = null!;
        private FillFlowContainer<ScreenFooterButton> buttonsFlow = null!;
        private Container overlayContentContainer = null!;
        private Container<ScreenFooterButton> hiddenButtonsContainer = null!;
        private IDisposable? customUiHueBinding;

        // ─── Torii: legacy (stable-style) song-select footer ────────────────
        //
        // frenzibyte's upstream PRs (ppy/osu #37974 + #37676/#37855/#37876) add
        // the skinnable legacy footer COMPONENTS but leave them unwired ("hooked
        // up at a later stage"). We mount the housing here and swap the default
        // lazer chrome for it when (a) the current screen opts in via
        // LegacySkinningEnabled (song select) AND (b) the active skin is a legacy
        // skin AND (c) no footer overlay (mod select etc.) is open. When inactive
        // the default footer behaves exactly as before.
        [Resolved]
        private SkinManager skins { get; set; } = null!;

        private readonly IBindable<Skin> currentSkin = new Bindable<Skin>();
        private LegacyFooter? legacyFooter;
        private bool legacySkinningEnabled;

        /// <summary>
        /// Set by the current screen (song select) to allow the legacy footer
        /// chrome to take over when a legacy skin is active. Other screens leave
        /// this false so their footer is never replaced.
        /// </summary>
        public bool LegacySkinningEnabled
        {
            get => legacySkinningEnabled;
            set
            {
                if (legacySkinningEnabled == value)
                    return;

                legacySkinningEnabled = value;
                updateLegacyFooterState();
            }
        }

        private LogoTrackingContainer logoTrackingContainer = null!;
        private IDisposable? logoTracking;

        // TODO: This has some weird update logic local in this class, but it only works for overlay containers.
        // This is not what we want. The footer is to be displayed on *screens* with different colour schemes.
        // It needs to update on screen switch.
        //
        // For now it's locked to Blue to match song select (the most prominent usage).
        [Cached]
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Blue);

        public ScreenFooter(BackReceptor? receptor = null)
        {
            RelativeSizeAxes = Axes.X;
            Height = HEIGHT;
            Anchor = Anchor.BottomLeft;
            Origin = Anchor.BottomLeft;

            if (receptor == null)
                Add(receptor = new BackReceptor());

            receptor.OnBackPressed = () => BackButton.TriggerClick();
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            colourProvider.ChangeColourScheme(CustomUiHueHelper.ResolveHue(config, OverlayColourScheme.Blue.GetHue(), CustomUiHueScope.Menu));

            InternalChildren = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Background5
                },
                buttonsGrid = new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Left = OsuGame.SCREEN_EDGE_MARGIN + ScreenBackButton.BUTTON_WIDTH + padding },
                    ColumnDimensions = new[]
                    {
                        new Dimension(GridSizeMode.AutoSize),
                        new Dimension(),
                    },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            buttonsFlow = new FillFlowContainer<ScreenFooterButton>
                            {
                                Name = "Visible footer buttons",
                                Anchor = Anchor.BottomLeft,
                                Origin = Anchor.BottomLeft,
                                Y = ScreenFooterButton.CORNER_RADIUS,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(7, 0),
                                AutoSizeAxes = Axes.Both,
                            },
                            overlayContentContainer = new Container
                            {
                                Name = "Overlay-provided extra content",
                                RelativeSizeAxes = Axes.Both,
                                Y = -OsuGame.SCREEN_EDGE_MARGIN,
                            },
                        },
                    }
                },
                BackButton = new ScreenBackButton
                {
                    Margin = new MarginPadding { Bottom = OsuGame.SCREEN_EDGE_MARGIN, Left = OsuGame.SCREEN_EDGE_MARGIN },
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Action = onBackPressed,
                },
                hiddenButtonsContainer = new Container<ScreenFooterButton>
                {
                    Name = "Hidden footer buttons",
                    Margin = new MarginPadding { Left = OsuGame.SCREEN_EDGE_MARGIN + ScreenBackButton.BUTTON_WIDTH + padding },
                    Y = ScreenFooterButton.CORNER_RADIUS,
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    AutoSizeAxes = Axes.Both,
                },
                (logoTrackingContainer = new LogoTrackingContainer
                {
                    RelativeSizeAxes = Axes.Both,
                }).WithChild(logoTrackingContainer.LogoFacade.With(f =>
                {
                    f.Anchor = Anchor.BottomRight;
                    f.Origin = Anchor.Centre;
                    f.Position = new Vector2(-76, -36);
                })),
                // Legacy chrome sits at the bottom and is faded in (over the hidden
                // default chrome) when the swap is active. The osu! logo is left on
                // the default tracking facade above, whose position (-76,-36) matches
                // the legacy footer's own facade, so no logo re-routing is needed.
                legacyFooter = new LegacyFooter
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    Alpha = 0,
                    BackAction = onBackPressed,
                    ModsAction = () => triggerScreenFooterButton(0),
                    RandomAction = () => triggerScreenFooterButton(1),
                    OptionsAction = () => triggerScreenFooterButton(2),
                },
            };

            // Base hue: only applied when no overlay is "owning" the footer
            // (when an overlay is active the footer mirrors that overlay's
            // hue via updateColourScheme). Accent hue: pushed through
            // unconditionally so the donator's chosen accent reads through
            // mod buttons, BackButton, etc. even when the chrome is
            // borrowing an overlay's hue.
            customUiHueBinding = CustomUiHueHelper.BindHue(config, OverlayColourScheme.Blue.GetHue(), CustomUiHueScope.Menu, hue =>
            {
                if (ActiveOverlay != null)
                    return;

                if (background == null)
                    colourProvider.ChangeColourScheme(hue);
                else
                    updateColourScheme(hue);
            });

            customUiAccentBinding = bindFooterAccent(config);

            currentSkin.BindTo(skins.CurrentSkin);
            currentSkin.BindValueChanged(_ => updateLegacyFooterState());
        }

        private void triggerScreenFooterButton(int index)
        {
            // The legacy footer's mod/random/options buttons forward to the real
            // (currently hidden) ScreenFooterButtons of the song-select screen, so
            // they drive identical behaviour. Only reached while the swap is active,
            // where the button order is [mods, random, options].
            var button = buttonsFlow.ElementAtOrDefault(index);
            button?.TriggerClick();
        }

        private void updateLegacyFooterState()
        {
            if (legacyFooter == null)
                return;

            bool active = legacySkinningEnabled
                          && ActiveOverlay == null
                          && currentSkin.Value is LegacySkin;

            legacyFooter.FadeTo(active ? 1 : 0, 120, Easing.OutQuint);

            // Hide the default lazer chrome behind the legacy footer. The osu! logo
            // facade is intentionally left visible (transparent container; positions
            // the shared logo where the legacy footer expects it).
            background.FadeTo(active ? 0 : 1, 120, Easing.OutQuint);
            buttonsGrid.FadeTo(active ? 0 : 1, 120, Easing.OutQuint);
            BackButton.FadeTo(active ? 0 : 1, 120, Easing.OutQuint);
        }

        [Resolved(CanBeNull = true)]
        private IAPIProvider? api { get; set; }

        private IDisposable bindFooterAccent(OsuConfigManager config)
        {
            var accentEnabled = config.GetBindable<bool>(OsuSetting.CustomUIAccentEnabled);
            var accentHue = config.GetBindable<float>(OsuSetting.CustomUIAccentHue);
            var hueEnabled = config.GetBindable<bool>(OsuSetting.CustomUIHueEnabled);
            var applyToMenu = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToMenu);
            var accentUnlocked = config.GetBindable<bool>(OsuSetting.CustomUIAccentUnlocked);
            var localUser = api?.LocalUser.GetBoundCopy();

            void apply()
            {
                // Mirror the central CustomUiHueHelper.ResolveAccentHue gate so
                // the footer's accent obeys the same store-unlock check as every
                // other surface (chrome / overlays / settings panel).
                bool active = hueEnabled.Value && applyToMenu.Value && accentEnabled.Value && accentUnlocked.Value;

                if (active)
                    colourProvider.ChangeAccentColourScheme((int)accentHue.Value);
                else
                    colourProvider.ResetAccentToBase();
            }

            accentEnabled.BindValueChanged(_ => apply());
            accentHue.BindValueChanged(_ => apply());
            hueEnabled.BindValueChanged(_ => apply());
            applyToMenu.BindValueChanged(_ => apply());
            accentUnlocked.BindValueChanged(_ => apply());
            localUser?.BindValueChanged(_ => apply(), true);

            // If api wasn't available (test scenes etc.), fall back to
            // the original immediate apply so the footer still resolves.
            if (localUser == null)
                apply();

            return new FooterAccentSubscription(() =>
            {
                accentEnabled.UnbindAll();
                accentHue.UnbindAll();
                hueEnabled.UnbindAll();
                applyToMenu.UnbindAll();
                accentUnlocked.UnbindAll();
                localUser?.UnbindAll();
            });
        }

        private sealed class FooterAccentSubscription : IDisposable
        {
            private Action? unsubscribe;
            public FooterAccentSubscription(Action unsubscribe) { this.unsubscribe = unsubscribe; }
            public void Dispose()
            {
                unsubscribe?.Invoke();
                unsubscribe = null;
            }
        }

        private IDisposable? customUiAccentBinding;

        private ScheduledDelegate? changeLogoDepthDelegate;

        public void StartTrackingLogo(OsuLogo logo, float duration = 0, Easing easing = Easing.None)
        {
            changeLogoDepthDelegate?.Cancel();
            changeLogoDepthDelegate = null;

            logoTracking = logoTrackingContainer.StartTracking(logo, duration, easing);
            RequestLogoInFront?.Invoke(true);
        }

        public void StopTrackingLogo()
        {
            logoTracking?.Dispose();
            logoTracking = null;

            changeLogoDepthDelegate = Scheduler.AddDelayed(() => RequestLogoInFront?.Invoke(false), transition_duration);
        }

        protected override void PopIn()
        {
            buttonsFlow.FadeIn(transition_duration / 4, Easing.OutQuint);

            this.MoveToY(0, transition_duration, Easing.OutQuint)
                .FadeIn();
        }

        protected override void PopOut()
        {
            // Really we shouldn't need to do this, but some buttons protrude vertically more than expected
            // (see FooterButtonMods).
            buttonsFlow.FadeOut(transition_duration, Easing.OutQuint);

            this.MoveToY(ScreenFooterButton.HEIGHT, transition_duration, Easing.OutQuint)
                .Then()
                .FadeOut();
        }

        public void SetButtons(IReadOnlyList<ScreenFooterButton> buttons)
        {
            temporarilyHiddenButtons.Clear();
            overlays.Clear();

            this.HidePopover();
            clearActiveOverlayContainer();

            var oldButtons = buttonsFlow.ToArray();

            for (int i = 0; i < oldButtons.Length; i++)
            {
                var oldButton = oldButtons[i];
                oldButton.State.Value = Visibility.Hidden;

                buttonsFlow.Remove(oldButton, false);
                hiddenButtonsContainer.Add(oldButton);

                if (buttons.Count > 0)
                    makeButtonDisappearToRight(oldButton, i, oldButtons.Length, true);
                else
                    makeButtonDisappearToBottom(oldButton, i, oldButtons.Length, true);
            }

            for (int i = 0; i < buttons.Count; i++)
            {
                var newButton = buttons[i];

                if (newButton.Overlay != null)
                {
                    newButton.Action = () => showOverlay(newButton.Overlay);
                    overlays.Add(newButton.Overlay);
                }

                Debug.Assert(!newButton.IsLoaded);
                buttonsFlow.Add(newButton);

                int index = i;

                // ensure transforms are added after LoadComplete to not be aborted by the FinishTransforms call.
                newButton.OnLoadComplete += _ =>
                {
                    if (oldButtons.Length > 0)
                        makeButtonAppearFromLeft(newButton, index, buttons.Count, 240);
                    else
                        makeButtonAppearFromBottom(newButton, index);
                };
            }
        }

        public ShearedOverlayContainer? ActiveOverlay { get; private set; }

        private VisibilityContainer? activeOverlayContent;

        private readonly List<ScreenFooterButton> temporarilyHiddenButtons = new List<ScreenFooterButton>();

        public IDisposable RegisterActiveOverlayContainer(ShearedOverlayContainer overlay, out VisibilityContainer? overlayContent)
        {
            if (ActiveOverlay != null)
            {
                throw new InvalidOperationException(@"Cannot set overlay content while one is already present. " +
                                                    $@"The previous overlay ({ActiveOverlay.GetType().Name}) should be hidden first.");
            }

            ActiveOverlay = overlay;

            // An overlay (mod select etc.) owns the footer content now, so step the
            // legacy chrome aside and let the default overlay-footer content show.
            updateLegacyFooterState();

            Debug.Assert(temporarilyHiddenButtons.Count == 0);

            var targetButton = buttonsFlow.SingleOrDefault(b => b.Overlay == overlay);

            temporarilyHiddenButtons.AddRange(targetButton != null
                ? buttonsFlow.SkipWhile(b => b != targetButton).Skip(1)
                : buttonsFlow);

            for (int i = temporarilyHiddenButtons.Count - 1; i >= 0; i--)
            {
                var button = temporarilyHiddenButtons[i];
                button.State.Value = Visibility.Hidden;
                buttonsFlow.Remove(button, false);
                hiddenButtonsContainer.Add(button);

                makeButtonDisappearToBottom(button, 0, 0, false);
            }

            updateColourScheme(overlay.ColourProvider.Hue);

            overlayContent = overlay.CreateFooterContent();
            activeOverlayContent = overlayContent;
            var content = overlayContent;

            if (content != null)
                overlayContentContainer.Child = content;

            if (temporarilyHiddenButtons.Count > 0)
                this.Delay(60).Schedule(() => content?.Show());
            else
                content?.Show();

            return new InvokeOnDisposal(clearActiveOverlayContainer);
        }

        private void clearActiveOverlayContainer()
        {
            if (ActiveOverlay == null)
                return;

            Debug.Assert(activeOverlayContent != null);

            activeOverlayContent.Hide();
            activeOverlayContent.Expire();

            double timeUntilRun = activeOverlayContent.LatestTransformEndTime - Time.Current;

            for (int i = 0; i < temporarilyHiddenButtons.Count; i++)
            {
                var button = temporarilyHiddenButtons[i];
                button.State.Value = Visibility.Visible;
                hiddenButtonsContainer.Remove(button, false);

                // temporarily bypass autosize on the X axis to prevent the buttons taking space
                // immediately upon being moved back to the flow.
                // this prevents the overlay content jumping to the right during its fade-out.
                button.BypassAutoSizeAxes = Axes.X;
                buttonsFlow.Add(button);

                makeButtonAppearFromBottom(button, 0);
            }

            temporarilyHiddenButtons.Clear();

            updateColourScheme(OverlayColourScheme.Aquamarine.GetHue());

            Scheduler.AddDelayed(() =>
            {
                // overlay content is done displaying, re-enable autosize on all active buttons
                foreach (var button in buttonsFlow)
                    button.BypassAutoSizeAxes = Axes.None;
            }, timeUntilRun);

            activeOverlayContent = null;
            ActiveOverlay = null;

            // Overlay closed; the legacy chrome can take over again if applicable.
            updateLegacyFooterState();
        }

        private void updateColourScheme(int hue)
        {
            colourProvider.ChangeColourScheme(hue);

            background.FadeColour(colourProvider.Background5, 150, Easing.OutQuint);

            foreach (var button in buttonsFlow)
                button.UpdateDisplay();
        }

        private void makeButtonAppearFromLeft(ScreenFooterButton button, int index, int count, float startDelay)
            => button.AppearFromLeft(startDelay + (count - index) * delay_per_button);

        private void makeButtonAppearFromBottom(ScreenFooterButton button, int index)
            => button.AppearFromBottom(index * delay_per_button);

        private void makeButtonDisappearToRight(ScreenFooterButton button, int index, int count, bool expire)
            => button.DisappearToRight((count - index) * delay_per_button, expire);

        private void makeButtonDisappearToBottom(ScreenFooterButton button, int index, int count, bool expire)
            => button.DisappearToBottom((count - index) * delay_per_button, expire);

        private void showOverlay(OverlayContainer overlay)
        {
            this.HidePopover();

            foreach (var o in overlays.Where(o => o != overlay))
                o.Hide();

            overlay.ToggleVisibility();
        }

        private void onBackPressed()
        {
            if (ActiveOverlay != null)
            {
                if (ActiveOverlay.OnBackButton())
                    return;

                ActiveOverlay.Hide();
                return;
            }

            BackButtonPressed?.Invoke();
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing)
            {
                customUiHueBinding?.Dispose();
                customUiHueBinding = null;
                customUiAccentBinding?.Dispose();
                customUiAccentBinding = null;
            }

            base.Dispose(isDisposing);
        }

        public partial class BackReceptor : Drawable, IKeyBindingHandler<GlobalAction>
        {
            public Action? OnBackPressed;

            public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
            {
                if (e.Repeat)
                    return false;

                switch (e.Action)
                {
                    case GlobalAction.Back:
                        OnBackPressed?.Invoke();
                        return true;
                }

                return false;
            }

            public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
            {
            }
        }
    }
}
