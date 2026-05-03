// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics;
using osuTK;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Input.Events;
using osu.Game.Rulesets;
using osu.Framework.Input.Bindings;
using osu.Game.Graphics.Containers;
using osu.Game.Input.Bindings;
using osu.Game.Configuration;
using osuTK.Graphics;

// Hue binding lives on the OverlayColourProvider scope; CustomUiHueScope.Menu
// matches the scope already used by ScreenFooter / SongSelect so the toolbar
// tints in lock-step with the rest of the menu chrome.

namespace osu.Game.Overlays.Toolbar
{
    public partial class Toolbar : OverlayContainer, IKeyBindingHandler<GlobalAction>
    {
        public const float HEIGHT = 40;
        public const float TOOLTIP_HEIGHT = 30;

        /// <summary>
        /// Reserved vertical space when the alpha (Torii pill) toolbar is
        /// active. Sized to fit the 56px pill exactly with no padding —
        /// the pill's drop shadow renders into the screen below (just
        /// visual blur, no input hijack), so we don't need to reserve
        /// extra space for it. Earlier this was 72px and song-select
        /// content felt squashed; the user could see and interact with
        /// less of it. Trimming back to ~pill-height regains those
        /// pixels for screens that anchor content below the toolbar.
        /// </summary>
        private const float alpha_reserved_height = 56f;

        /// <summary>
        /// Whether the user hid this <see cref="Toolbar"/> with <see cref="GlobalAction.ToggleToolbar"/>.
        /// In this state, automatic toggles should not occur, respecting the user's preference to have no toolbar.
        /// </summary>
        private bool hiddenByUser;

        public Action OnHome;

        private ToolbarUserButton userButton;
        private ToolbarRulesetSelector rulesetSelector;
        private IBindable<bool> alphaToolbarUnlocked;
        private IBindable<bool> alphaToolbarUse;

        private OsuConfigManager localConfig;
        // Lives across rebuildLayout(): the active background drawable is
        // replaced when the user toggles alpha-toolbar, so we keep the binding
        // as a hue-only callback that re-tints whatever ToolbarBackground
        // currently exists.
        private IDisposable customUiHueBinding;
        private ToolbarBackground activeBackground;

        private const double transition_time = 500;

        protected readonly IBindable<OverlayActivation> OverlayActivationMode = new Bindable<OverlayActivation>(OverlayActivation.All);

        // Toolbar and its components need keyboard input even when hidden.
        public override bool PropagateNonPositionalInputSubTree => OverlayActivationMode.Value != OverlayActivation.Disabled;

        public Toolbar()
        {
            RelativeSizeAxes = Axes.X;
            Size = new Vector2(1, HEIGHT);
            AlwaysPresent = true;
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            // this only needed to be set for the initial LoadComplete/Update, so layout completes and gets buttons in a state they can correctly handle keyboard input for hotkeys.
            AlwaysPresent = false;
        }

        [Resolved]
        private Bindable<RulesetInfo> ruleset { get; set; }

        [BackgroundDependencyLoader(true)]
        private void load(OsuGame osuGame, OsuConfigManager config)
        {
            if (osuGame != null)
                OverlayActivationMode.BindTo(osuGame.OverlayActivationMode);

            localConfig = config;

            alphaToolbarUnlocked = config.GetBindable<bool>(OsuSetting.AlphaToolbarEnabled);
            alphaToolbarUse = config.GetBindable<bool>(OsuSetting.AlphaToolbarUse);

            alphaToolbarUnlocked.BindValueChanged(_ => Scheduler.AddOnce(rebuildLayout), true);
            alphaToolbarUse.BindValueChanged(_ => Scheduler.AddOnce(rebuildLayout), true);

            // Track the active CustomUIHue (Menu scope) and push it into
            // whatever ToolbarBackground currently exists. Re-applies on
            // rebuildLayout because the new background pulls the cached hue
            // back out of the helper at construction.
            customUiHueBinding = CustomUiHueHelper.BindHue(config, OverlayColourScheme.Blue.GetHue(), CustomUiHueScope.Menu, hue =>
            {
                if (activeBackground != null)
                    activeBackground.Hue = hue;
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            rebuildLayout();
        }

        private bool shouldUseAlphaToolbar => alphaToolbarUnlocked?.Value == true && alphaToolbarUse?.Value == true;

        private void rebuildLayout()
        {
            bool alphaStyle = shouldUseAlphaToolbar;

            rulesetSelector?.Current.UnbindBindings();
            Size = new Vector2(1, alphaStyle ? alpha_reserved_height : HEIGHT);

            if (alphaStyle)
            {
                userButton = null;
                rulesetSelector = null;

                Children = new Drawable[]
                {
                    new ToriiAlphaToolbar(() => OnHome?.Invoke())
                    {
                        RelativeSizeAxes = Axes.Both,
                    }
                };

                return;
            }

            ToolbarBackground background;
            HoverInterceptor interceptor;

            Children = new Drawable[]
            {
                background = new ToolbarBackground(),
                createClassicToolbarGrid(),
                interceptor = new HoverInterceptor
                {
                    RelativeSizeAxes = Axes.Both
                }
            };

            ((IBindable<bool>)background.ShowGradient).BindTo(interceptor.ReceivedHover);
            rulesetSelector?.Current.BindTo(ruleset);

            // Wire the new background to the live hue. The binding callback
            // in load() pushes future changes; this seeds the initial value
            // so the toolbar matches the saved hue on every rebuild.
            activeBackground = background;
            if (localConfig != null)
                background.Hue = CustomUiHueHelper.ResolveHue(localConfig, OverlayColourScheme.Blue.GetHue(), CustomUiHueScope.Menu);
        }

        private Drawable createClassicToolbarGrid()
        {
            return new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                ColumnDimensions = new[]
                {
                    new Dimension(GridSizeMode.AutoSize),
                    new Dimension(),
                    new Dimension(GridSizeMode.AutoSize)
                },
                Content = new[]
                {
                    new Drawable[]
                    {
                        new Container
                        {
                            Name = "Left buttons",
                            RelativeSizeAxes = Axes.Y,
                            AutoSizeAxes = Axes.X,
                            Depth = float.MinValue,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    Colour = OsuColour.Gray(0.1f),
                                    RelativeSizeAxes = Axes.Both,
                                },
                                new FillFlowContainer
                                {
                                    Direction = FillDirection.Horizontal,
                                    RelativeSizeAxes = Axes.Y,
                                    AutoSizeAxes = Axes.X,
                                    Children = new Drawable[]
                                    {
                                        new ToolbarSettingsButton(),
                                        new ToolbarHomeButton
                                        {
                                            Action = () => OnHome?.Invoke()
                                        },
                                        new Container
                                        {
                                            RelativeSizeAxes = Axes.Y,
                                            AutoSizeAxes = Axes.X,
                                            Child = new ToriiPpDevIndicator
                                            {
                                                Anchor = Anchor.CentreLeft,
                                                Origin = Anchor.CentreLeft,
                                                Y = 2,
                                            }
                                        },
                                    },
                                },
                            }
                        },
                        new Container
                        {
                            Name = "Ruleset selector",
                            RelativeSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                new OsuScrollContainer(Direction.Horizontal)
                                {
                                    ScrollbarVisible = false,
                                    RelativeSizeAxes = Axes.Both,
                                    Masking = false,
                                    Children = new Drawable[]
                                    {
                                        rulesetSelector = new ToolbarRulesetSelector()
                                    }
                                },
                                new Box
                                {
                                    Colour = ColourInfo.GradientHorizontal(OsuColour.Gray(0.1f).Opacity(0), OsuColour.Gray(0.1f)),
                                    Width = 50,
                                    RelativeSizeAxes = Axes.Y,
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                },
                            }
                        },
                        new Container
                        {
                            Name = "Right buttons",
                            RelativeSizeAxes = Axes.Y,
                            AutoSizeAxes = Axes.X,
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            Children = new Drawable[]
                            {
                                new Box
                                {
                                    Colour = OsuColour.Gray(0.1f),
                                    RelativeSizeAxes = Axes.Both,
                                },
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.TopRight,
                                    Origin = Anchor.TopRight,
                                    Direction = FillDirection.Horizontal,
                                    RelativeSizeAxes = Axes.Y,
                                    AutoSizeAxes = Axes.X,
                                    Children = new Drawable[]
                                    {
                                        //new ToolbarNewsButton(),
                                        new ToolbarChangelogButton(),
                                        //new ToolbarWikiButton(),
                                        new ToolbarRankingsButton(),
                                        new ToolbarBeatmapListingButton(),
                                        new ToolbarChatButton(),
                                        new ToolbarSocialButton(),
                                        new ToolbarMusicButton(),
                                        userButton = new ToolbarUserButton(),
                                        new ToolbarClock(),
                                        new ToolbarNotificationButton(),
                                    }
                                },
                            }
                        },
                    },
                }
            };
        }

        public partial class ToolbarBackground : Container
        {
            public Bindable<bool> ShowGradient { get; } = new BindableBool();

            private readonly Box solidBackground;
            private readonly Box gradientBackground;

            // Default fallback (Blue scheme hue 200°) matches the rest of
            // the menu chrome. ResolveHue() in Toolbar.load overrides this
            // with the user's CustomUIHue if enabled for the Menu scope.
            private int hue = 200;

            public int Hue
            {
                get => hue;
                set
                {
                    if (hue == value) return;
                    hue = value;
                    applyHue();
                }
            }

            public ToolbarBackground()
            {
                RelativeSizeAxes = Axes.Both;
                Children = new Drawable[]
                {
                    solidBackground = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                    gradientBackground = new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Anchor = Anchor.BottomLeft,
                        Alpha = 0,
                        Height = 80,
                        Colour = ColourInfo.GradientVertical(
                            OsuColour.Gray(0f).Opacity(0.7f), OsuColour.Gray(0).Opacity(0)),
                    },
                };

                applyHue();
            }

            // OverlayColourProvider derives Background6 as HSL(hue, 0.1, 0.1)
            // — same lightness as the legacy Gray(0.1f) but tinted by the
            // active hue. We instantiate a throwaway provider rather than
            // resolving the ambient one because the Toolbar isn't always
            // inside an OverlayColourProvider scope (it lives at the very
            // root of the OsuGame tree).
            private void applyHue()
            {
                if (solidBackground == null)
                    return;

                solidBackground.Colour = new OverlayColourProvider(hue).Background6;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                ShowGradient.BindValueChanged(_ => updateState(), true);
            }

            private void updateState()
            {
                if (ShowGradient.Value)
                    gradientBackground.FadeIn(2500, Easing.OutQuint);
                else
                    gradientBackground.FadeOut(200, Easing.OutQuint);
            }
        }

        /// <summary>
        /// Whenever the mouse cursor is within the bounds of the toolbar, we want the background gradient to show, for toolbar button descriptions to be legible.
        /// Unfortunately we also need to ensure that the toolbar buttons handle hover, to prevent the possibility of multiple descriptions being shown
        /// due to hover events passing through multiple buttons.
        /// This drawable is a workaround, that when placed front-most in the toolbar, allows to see whether hover events have been propagated through it without handling them.
        /// </summary>
        private partial class HoverInterceptor : Drawable
        {
            public IBindable<bool> ReceivedHover => receivedHover;
            private readonly Bindable<bool> receivedHover = new BindableBool();

            protected override bool OnHover(HoverEvent e)
            {
                receivedHover.Value = true;
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                receivedHover.Value = false;
                base.OnHoverLost(e);
            }
        }

        protected override void UpdateState(ValueChangedEvent<Visibility> state)
        {
            bool blockShow = hiddenByUser || OverlayActivationMode.Value == OverlayActivation.Disabled;

            if (state.NewValue == Visibility.Visible && blockShow)
            {
                State.Value = Visibility.Hidden;
                return;
            }

            base.UpdateState(state);
        }

        protected override void PopIn()
        {
            this.MoveToY(0, transition_time, Easing.OutQuint);
            this.FadeIn(transition_time / 4, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            userButton?.StateContainer?.Hide();

            this.MoveToY(-DrawSize.Y, transition_time, Easing.OutQuint);
            this.FadeOut(transition_time, Easing.InQuint);
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (OverlayActivationMode.Value == OverlayActivation.Disabled)
                return false;

            switch (e.Action)
            {
                case GlobalAction.ToggleToolbar:
                    hiddenByUser = State.Value == Visibility.Visible; // set before toggling to allow the operation to always succeed.
                    ToggleVisibility();
                    return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        protected override void Dispose(bool isDisposing)
        {
            customUiHueBinding?.Dispose();
            customUiHueBinding = null;
            base.Dispose(isDisposing);
        }
    }
}
