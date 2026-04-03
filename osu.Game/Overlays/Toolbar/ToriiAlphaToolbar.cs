// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Users.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    public partial class ToriiAlphaToolbar : CompositeDrawable
    {
        private readonly Action onHome;

        [Resolved(canBeNull: true)]
        private RankingsOverlay rankingsOverlay { get; set; }

        [Resolved(canBeNull: true)]
        private BeatmapListingOverlay beatmapListingOverlay { get; set; }

        [Resolved(canBeNull: true)]
        private SettingsOverlay settingsOverlay { get; set; }

        [Resolved(canBeNull: true)]
        private NotificationOverlay notificationOverlay { get; set; }

        [Resolved(canBeNull: true)]
        private LoginOverlay loginOverlay { get; set; }

        [Resolved(canBeNull: true)]
        private OsuGame game { get; set; }

        private IBindable<APIUser> localUser;
        private IBindable<int> unreadCount;
        private IBindable<ToolbarLayoutMode> layoutMode;
        private IBindable<ToolbarDensityMode> densityMode;

        private Container mainBarHost;
        private Container nodeBadge;
        private Container brandTextContainer;
        private Container subtitleContainer;
        private OsuScrollContainer navScroller;
        private FillFlowContainer rightButtonsFlow;
        private AlphaNavButton beatmapsButton;
        private AlphaNavButton joinButton;
        private AlphaIconButton rankingsButton;
        private AlphaIconButton settingsButton;
        private AlphaIconButton notificationButton;
        private AlphaUserButton userButton;
        private AlphaClockPill clockPill;

        private float lastBarWidth = -1;
        private bool lastCompact;
        private bool lastNarrow;
        private bool lastVeryNarrow;
        private DensityPreset lastDensityPreset = (DensityPreset)(-1);

        private const float compactBarHeight = 40f;
        private const float defaultBarHeight = 44f;
        private const float comfortableBarHeight = 46f;
        private const float maxBarWidth = 1320f;

        private enum DensityPreset
        {
            Compact,
            Default,
            Comfortable
        }

        public ToriiAlphaToolbar(Action onHome)
        {
            this.onHome = onHome;
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours, IAPIProvider api, OsuConfigManager config)
        {
            localUser = api.LocalUser.GetBoundCopy();
            unreadCount = notificationOverlay?.UnreadCount.GetBoundCopy() ?? new BindableInt();
            layoutMode = config.GetBindable<ToolbarLayoutMode>(OsuSetting.ToolbarLayoutMode);
            densityMode = config.GetBindable<ToolbarDensityMode>(OsuSetting.ToolbarDensityMode);

            string websiteUrl = api.Endpoints.WebsiteUrl ?? string.Empty;
            if (!string.IsNullOrEmpty(websiteUrl) && !websiteUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !websiteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                websiteUrl = $"https://{websiteUrl}";

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    mainBarHost = new Container
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Height = defaultBarHeight,
                        Y = 1,
                        Child = createMainBar(colours, websiteUrl),
                    },
                    nodeBadge = createNodeBadge(),
                }
            };

            localUser.BindValueChanged(v => userButton?.UpdateUser(v.NewValue), true);
            unreadCount.BindValueChanged(v => notificationButton?.SetBadge(v.NewValue), true);
            layoutMode.BindValueChanged(_ => applyAdaptiveLayout(true), true);
            densityMode.BindValueChanged(_ => applyAdaptiveLayout(true), true);
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            float available = MathF.Max(320f, DrawWidth - 28f);
            float target = MathF.Min(maxBarWidth, available);
            if (Math.Abs(target - lastBarWidth) > 0.5f)
            {
                mainBarHost.Width = target;
                lastBarWidth = target;
            }

            applyAdaptiveLayout(false);
        }

        private Drawable createMainBar(OsuColour colours, string websiteUrl)
            => new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 23,
                CornerExponent = 2f,
                MaskingSmoothness = 2.2f,
                BorderThickness = 0.9f,
                BorderColour = new Color4(129, 148, 220, 108),
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Radius = 10,
                    Roundness = 8,
                    Colour = new Color4(5, 8, 28, 204),
                },
                Children = new Drawable[]
                {
                    new BufferedContainer(cachedFrameBuffer: true)
                    {
                        RelativeSizeAxes = Axes.Both,
                        DrawOriginal = true,
                        BlurSigma = new Vector2(10),
                        EffectColour = new Color4(136, 160, 238, 74),
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(8, 13, 40, 236),
                        }
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientHorizontal(
                            new Color4(107, 134, 255, 20),
                            new Color4(255, 129, 197, 24)),
                    },
                    (new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 22,
                        Y = -7,
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Colour = new Color4(166, 194, 255, 90),
                    }).WithEffect(new BlurEffect
                    {
                        Sigma = new Vector2(20),
                        PadExtent = true,
                    }),
                    new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 1,
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Colour = new Color4(219, 228, 255, 84),
                    },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        ColumnDimensions = new[]
                        {
                            new Dimension(GridSizeMode.AutoSize),
                            new Dimension(),
                            new Dimension(GridSizeMode.AutoSize),
                        },
                        Content = new[]
                        {
                            new Drawable[]
                            {
                                createBrandBlock(colours),
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Child = navScroller = new OsuScrollContainer(Direction.Horizontal)
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        RelativeSizeAxes = Axes.X,
                                        Height = 34,
                                        ScrollbarVisible = false,
                                        Child = new FillFlowContainer
                                        {
                                            Direction = FillDirection.Horizontal,
                                            AutoSizeAxes = Axes.Both,
                                            Spacing = new Vector2(4, 0),
                                            Children = new Drawable[]
                                            {
                                                createHomeButton(),
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
                                                beatmapsButton = createOverlayNavButton("Beatmaps", FontAwesome.Solid.CompactDisc, beatmapListingOverlay),
                                                joinButton = new AlphaNavButton("Join Server", FontAwesome.Solid.Link)
                                                {
                                                    Action = string.IsNullOrEmpty(websiteUrl) ? null : () => game?.OpenUrlExternally(websiteUrl),
                                                },
                                            }
                                        }
                                    }
                                },
                                rightButtonsFlow = new FillFlowContainer
                                {
                                    Direction = FillDirection.Horizontal,
                                    AutoSizeAxes = Axes.Both,
                                    Anchor = Anchor.CentreRight,
                                    Origin = Anchor.CentreRight,
                                    Spacing = new Vector2(5, 0),
                                    Padding = new MarginPadding { Left = 5, Right = 8 },
                                    Children = new Drawable[]
                                    {
                                        rankingsButton = createOverlayIconButton(FontAwesome.Solid.ChartLine, rankingsOverlay),
                                        settingsButton = createOverlayIconButton(FontAwesome.Solid.Cog, settingsOverlay),
                                        notificationButton = createNotificationButton(),
                                        userButton = new AlphaUserButton { Action = () => loginOverlay?.ToggleVisibility() },
                                        clockPill = new AlphaClockPill(),
                                    }
                                }
                            }
                        }
                    }
                }
            };

        private Container createNodeBadge()
            => new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Both,
                Y = 50,
                Masking = true,
                CornerRadius = 10,
                CornerExponent = 2f,
                MaskingSmoothness = 1.4f,
                BorderThickness = 0.9f,
                BorderColour = new Color4(133, 149, 207, 85),
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(18, 22, 47, 214),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = OsuFont.GetFont(size: 9.5f, weight: FontWeight.SemiBold),
                        Spacing = new Vector2(1.1f, 0),
                        Padding = new MarginPadding { Horizontal = 11, Vertical = 3 },
                        Colour = new Color4(177, 192, 232, 255),
                        Text = "\u2022 EUROPE NODE \u2022 OSU!LAZER PRIVATE SERVER",
                    }
                }
            };

        private DensityPreset resolveDensityPreset()
        {
            ToolbarDensityMode configured = densityMode?.Value ?? ToolbarDensityMode.Auto;

            switch (configured)
            {
                case ToolbarDensityMode.Compact:
                    return DensityPreset.Compact;

                case ToolbarDensityMode.Comfortable:
                    return DensityPreset.Comfortable;

                default:
                    return DrawHeight <= 900 ? DensityPreset.Compact : DensityPreset.Default;
            }
        }

        private void applyDensityPreset(DensityPreset preset, bool force)
        {
            if (!force && preset == lastDensityPreset)
                return;

            lastDensityPreset = preset;

            switch (preset)
            {
                case DensityPreset.Compact:
                    mainBarHost.Height = compactBarHeight;
                    mainBarHost.Y = 0.5f;
                    nodeBadge.Y = 42;
                    if (navScroller != null)
                        navScroller.Height = 30;
                    if (rightButtonsFlow != null)
                    {
                        rightButtonsFlow.Spacing = new Vector2(4, 0);
                        rightButtonsFlow.Padding = new MarginPadding { Left = 4, Right = 7 };
                    }
                    break;

                case DensityPreset.Comfortable:
                    mainBarHost.Height = comfortableBarHeight;
                    mainBarHost.Y = 1;
                    nodeBadge.Y = 50;
                    if (navScroller != null)
                        navScroller.Height = 34;
                    if (rightButtonsFlow != null)
                    {
                        rightButtonsFlow.Spacing = new Vector2(5, 0);
                        rightButtonsFlow.Padding = new MarginPadding { Left = 5, Right = 8 };
                    }
                    break;

                default:
                    mainBarHost.Height = defaultBarHeight;
                    mainBarHost.Y = 1;
                    nodeBadge.Y = 46;
                    if (navScroller != null)
                        navScroller.Height = 32;
                    if (rightButtonsFlow != null)
                    {
                        rightButtonsFlow.Spacing = new Vector2(5, 0);
                        rightButtonsFlow.Padding = new MarginPadding { Left = 5, Right = 8 };
                    }
                    break;
            }
        }

        private void applyAdaptiveLayout(bool force)
        {
            DensityPreset densityPreset = resolveDensityPreset();
            applyDensityPreset(densityPreset, force);

            bool densityCompact = densityPreset == DensityPreset.Compact;
            bool compact = layoutMode?.Value == ToolbarLayoutMode.MinimalAutoExpand || densityCompact;
            bool narrow = mainBarHost?.Width < 1100f;
            bool veryNarrow = mainBarHost?.Width < 940f;

            if (!force && compact == lastCompact && narrow == lastNarrow && veryNarrow == lastVeryNarrow)
                return;

            lastCompact = compact;
            lastNarrow = narrow;
            lastVeryNarrow = veryNarrow;

            bool showBrandText = !veryNarrow;
            bool showSubtitle = !compact && !narrow && showBrandText;
            bool showNodeBadge = !compact && !veryNarrow && mainBarHost.Width >= 1180;
            bool showJoinButton = !veryNarrow;
            bool showClock = !compact && !narrow;
            bool showSecondaryIcons = !veryNarrow;

            brandTextContainer?.FadeTo(showBrandText ? 1 : 0, 160, Easing.OutQuint);
            brandTextContainer?.ScaleTo(showBrandText ? Vector2.One : new Vector2(0.01f, 1f), 160, Easing.OutQuint);

            subtitleContainer?.FadeTo(showSubtitle ? 1 : 0, 160, Easing.OutQuint);
            subtitleContainer?.ScaleTo(showSubtitle ? Vector2.One : new Vector2(0.01f, 1f), 160, Easing.OutQuint);

            nodeBadge?.FadeTo(showNodeBadge ? 1 : 0, 160, Easing.OutQuint);
            nodeBadge?.ScaleTo(showNodeBadge ? Vector2.One : new Vector2(0.96f), 160, Easing.OutQuint);

            beatmapsButton?.SetLabelVisible(!compact);
            joinButton?.SetLabelVisible(!compact && !narrow);
            joinButton?.SetVisibleInLayout(showJoinButton);

            rankingsButton?.SetVisibleInLayout(showSecondaryIcons);
            settingsButton?.SetVisibleInLayout(showSecondaryIcons);
            userButton?.SetCompact(compact || narrow);
            clockPill?.SetVisibleInLayout(showClock);
        }

        private Drawable createBrandBlock(OsuColour colours)
            => new FillFlowContainer
            {
                Direction = FillDirection.Horizontal,
                AutoSizeAxes = Axes.Both,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Padding = new MarginPadding { Left = 12, Right = 8 },
                Spacing = new Vector2(10, 0),
                Children = new Drawable[]
                {
                    new CircularContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(30),
                        Masking = true,
                        MaskingSmoothness = 1.6f,
                        BorderThickness = 1f,
                        BorderColour = new Color4(112, 132, 206, 170),
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(23, 30, 70, 230),
                            },
                            new SpriteIcon
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Icon = FontAwesome.Solid.ToriiGate,
                                Size = new Vector2(13),
                                Colour = colours.Pink,
                            }
                        }
                    },
                    brandTextContainer = new Container
                    {
                        AutoSizeAxes = Axes.Both,
                        Child = new FillFlowContainer
                        {
                            Direction = FillDirection.Vertical,
                            AutoSizeAxes = Axes.Both,
                            Spacing = new Vector2(0, -1),
                            Children = new Drawable[]
                            {
                            new OsuSpriteText
                            {
                                Text = "Torii",
                                Font = OsuFont.GetFont(size: 30, weight: FontWeight.Bold),
                                Scale = new Vector2(0.53f),
                                Colour = Color4.White,
                                Margin = new MarginPadding { Top = -1 },
                            },
                                subtitleContainer = new Container
                                {
                                    AutoSizeAxes = Axes.Both,
                                    Child = new OsuSpriteText
                                    {
                                        Text = "forged in Shikke's Dojo",
                                        Font = OsuFont.GetFont(size: 9.6f, weight: FontWeight.Regular),
                                        Colour = new Color4(183, 193, 228, 216),
                                    }
                                }
                            }
                        }
                    }
                }
            };

        private AlphaNavButton createHomeButton()
        {
            var button = new AlphaNavButton("Home", FontAwesome.Solid.Home);
            button.Action = () => onHome?.Invoke();
            button.SetPersistentActive(true);
            return button;
        }

        private AlphaNavButton createOverlayNavButton(LocalisableString text, IconUsage icon, OverlayContainer overlay)
        {
            var button = new AlphaNavButton(text, icon);
            if (overlay != null)
                button.BindOverlay(overlay);
            else
                button.Enabled.Value = false;
            return button;
        }

        private AlphaIconButton createOverlayIconButton(IconUsage icon, OverlayContainer overlay)
        {
            var button = new AlphaIconButton(icon);
            if (overlay != null)
                button.BindOverlay(overlay);
            else
                button.Enabled.Value = false;
            return button;
        }

        private AlphaIconButton createNotificationButton()
        {
            var button = new AlphaIconButton(FontAwesome.Regular.Bell);
            if (notificationOverlay != null)
                button.BindOverlay(notificationOverlay);
            else
                button.Enabled.Value = false;
            return button;
        }

        private abstract partial class AlphaButtonBase : OsuClickableContainer
        {
            private readonly Box inactiveBackground;
            private readonly Box activeBackground;
            private readonly Box hoverLayer;
            private readonly float inactiveAlpha;
            private IBindable<Visibility> boundOverlayState;

            private bool overlayVisible;
            private bool persistActive;
            private bool layoutVisible = true;

            protected readonly FillFlowContainer ContentFlow;

            protected AlphaButtonBase(bool iconOnly, float height, float cornerRadius, float horizontalPadding, float inactiveAlpha)
            {
                this.inactiveAlpha = inactiveAlpha;

                Height = height;

                if (iconOnly)
                    Size = new Vector2(height);
                else
                    AutoSizeAxes = Axes.X;

                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = cornerRadius,
                        CornerExponent = 2f,
                        MaskingSmoothness = 1.6f,
                        Children = new Drawable[]
                        {
                            inactiveBackground = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(19, 26, 61, 200),
                                Alpha = inactiveAlpha,
                            },
                            activeBackground = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientHorizontal(
                                    new Color4(255, 143, 216, 255),
                                    new Color4(255, 116, 174, 255)),
                                Alpha = 0,
                            },
                            hoverLayer = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = Color4.White.Opacity(0.08f),
                                Alpha = 0,
                            }
                        }
                    },
                    ContentFlow = new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Spacing = new Vector2(7, 0),
                        Padding = new MarginPadding { Horizontal = iconOnly ? 0 : horizontalPadding },
                    }
                };
            }

            public void SetPersistentActive(bool active)
            {
                persistActive = active;
                updateVisualState();
            }

            public void SetVisibleInLayout(bool visible)
            {
                if (layoutVisible == visible)
                    return;

                layoutVisible = visible;
                Enabled.Value = visible;

                this.ClearTransforms();
                this.FadeTo(visible ? 1 : 0, 140, Easing.OutQuint);
                this.ScaleTo(visible ? Vector2.One : new Vector2(0.01f, 1), 140, Easing.OutQuint);
            }

            public void BindOverlay(OverlayContainer overlay)
            {
                Action = overlay.ToggleVisibility;

                boundOverlayState?.UnbindAll();
                boundOverlayState = overlay.State.GetBoundCopy();
                boundOverlayState.BindValueChanged(v =>
                {
                    overlayVisible = v.NewValue == Visibility.Visible;
                    updateVisualState();
                }, true);
            }

            protected override bool OnHover(HoverEvent e)
            {
                hoverLayer.FadeIn(120, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                hoverLayer.FadeOut(120, Easing.OutQuint);
                base.OnHoverLost(e);
            }

            protected override bool OnClick(ClickEvent e)
            {
                this.ScaleTo(new Vector2(0.97f, Scale.Y), 70, Easing.OutQuint)
                    .Then()
                    .ScaleTo(new Vector2(1f, Scale.Y), 160, Easing.OutElasticHalf);
                return base.OnClick(e);
            }

            private void updateVisualState()
            {
                bool active = overlayVisible || persistActive;

                activeBackground.FadeTo(active ? 1f : 0f, 170, Easing.OutQuint);
                inactiveBackground.FadeTo(active ? MathF.Max(0.2f, inactiveAlpha * 0.4f) : inactiveAlpha, 170, Easing.OutQuint);
                updateActiveState(active);
            }

            protected virtual void updateActiveState(bool active)
            {
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);
                boundOverlayState?.UnbindAll();
            }
        }

        private partial class AlphaNavButton : AlphaButtonBase
        {
            private readonly SpriteIcon iconSprite;
            private readonly Container labelContainer;
            private readonly OsuSpriteText label;
            private bool labelVisible = true;

            public AlphaNavButton(LocalisableString text, IconUsage icon)
                : base(iconOnly: false, height: 32, cornerRadius: 16, horizontalPadding: 10, inactiveAlpha: 0)
            {
                ContentFlow.AddRange(new Drawable[]
                {
                    iconSprite = new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Icon = icon,
                        Size = new Vector2(10),
                        Colour = Color4.White.Opacity(0.93f),
                    },
                    labelContainer = new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Both,
                        Child = label = new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Font = OsuFont.GetFont(size: 13.2f, weight: FontWeight.SemiBold),
                            Colour = new Color4(214, 223, 248, 252),
                            Margin = new MarginPadding { Top = -0.5f },
                            Text = text,
                        }
                    }
                });
            }

            public void SetLabelVisible(bool visible)
            {
                if (labelVisible == visible)
                    return;

                labelVisible = visible;
                ContentFlow.Spacing = new Vector2(visible ? 6 : 0, 0);

                labelContainer.ClearTransforms();
                labelContainer.FadeTo(visible ? 1 : 0, 130, Easing.OutQuint);
                labelContainer.ScaleTo(visible ? Vector2.One : new Vector2(0.01f, 1), 130, Easing.OutQuint);
            }

            protected override void updateActiveState(bool active)
            {
                label.FadeColour(active ? Color4.White : new Color4(212, 221, 248, 238), 160, Easing.OutQuint);
                iconSprite.FadeColour(active ? Color4.White : Color4.White.Opacity(0.94f), 160, Easing.OutQuint);
            }
        }

        private partial class AlphaIconButton : AlphaButtonBase
        {
            private readonly CircularContainer ring;
            private readonly SpriteIcon icon;
            private readonly CircularContainer badgeContainer;
            private readonly OsuSpriteText badgeText;

            public AlphaIconButton(IconUsage icon)
                : base(iconOnly: true, height: 32, cornerRadius: 16, horizontalPadding: 0, inactiveAlpha: 0.58f)
            {
                Add(ring = new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    MaskingSmoothness = 1.4f,
                    BorderThickness = 1f,
                    BorderColour = new Color4(150, 168, 230, 140),
                });

                ContentFlow.Child = this.icon = new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = icon,
                    Size = new Vector2(13),
                    Colour = Color4.White.Opacity(0.96f),
                };

                Add(badgeContainer = new CircularContainer
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.Centre,
                    Position = new Vector2(0, 6),
                    AutoSizeAxes = Axes.X,
                    Height = 15,
                    Masking = true,
                    MaskingSmoothness = 1.2f,
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(255, 61, 61, 255),
                        },
                        badgeText = new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                            UseFullGlyphHeight = true,
                            Padding = new MarginPadding { Horizontal = 4, Top = 0.5f },
                            Colour = Color4.White,
                        }
                    }
                });
            }

            public void SetBadge(int value)
            {
                if (value <= 0)
                {
                    badgeContainer.FadeOut(120, Easing.OutQuint);
                    return;
                }

                badgeContainer.FadeIn(120, Easing.OutQuint);
                badgeText.Text = value > 99 ? "99+" : value.ToString();
            }

            protected override void updateActiveState(bool active)
            {
                ring.FadeColour(active ? new Color4(255, 201, 235, 215) : new Color4(150, 168, 230, 140), 160, Easing.OutQuint);
                icon.FadeColour(active ? Color4.White : Color4.White.Opacity(0.95f), 160, Easing.OutQuint);
            }
        }

        private partial class AlphaUserButton : AlphaButtonBase
        {
            private readonly OsuSpriteText usernameText;
            private readonly UpdateableAvatar avatar;
            private readonly Container infoContainer;
            private bool compact;

            public AlphaUserButton()
                : base(iconOnly: false, height: 32, cornerRadius: 16, horizontalPadding: 8, inactiveAlpha: 0.52f)
            {
                ContentFlow.AddRange(new Drawable[]
                {
                    new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(20),
                        Masking = true,
                        MaskingSmoothness = 1.2f,
                        CornerRadius = 7,
                        Child = avatar = new UpdateableAvatar(isInteractive: false)
                        {
                            RelativeSizeAxes = Axes.Both,
                        }
                    },
                    infoContainer = new Container
                    {
                        AutoSizeAxes = Axes.Both,
                        Child = new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(5, 0),
                            Children = new Drawable[]
                            {
                                usernameText = new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Font = OsuFont.GetFont(size: 12.5f, weight: FontWeight.SemiBold),
                                    Colour = Color4.White.Opacity(0.94f),
                                    Margin = new MarginPadding { Top = -0.5f },
                                    Text = "Guest",
                                },
                                new SpriteIcon
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Icon = FontAwesome.Solid.ChevronDown,
                                    Size = new Vector2(8),
                                    Colour = Color4.White.Opacity(0.72f),
                                    Margin = new MarginPadding { Right = 1 },
                                }
                            }
                        }
                    }
                });
            }

            public void UpdateUser(APIUser user)
            {
                usernameText.Text = string.IsNullOrWhiteSpace(user?.Username) ? "Guest" : user.Username;
                avatar.User = user;
            }

            public void SetCompact(bool compact)
            {
                if (this.compact == compact)
                    return;

                this.compact = compact;
                ContentFlow.Spacing = new Vector2(compact ? 0 : 7, 0);

                infoContainer.ClearTransforms();
                infoContainer.FadeTo(compact ? 0 : 1, 130, Easing.OutQuint);
                infoContainer.ScaleTo(compact ? new Vector2(0.01f, 1) : Vector2.One, 130, Easing.OutQuint);
            }
        }

        private partial class AlphaClockPill : CompositeDrawable
        {
            private Bindable<bool> prefer24HourTime;
            private DigitalClockDisplay digitalClock;
            private bool layoutVisible = true;

            public AlphaClockPill()
            {
                AutoSizeAxes = Axes.Both;

                InternalChild = new Container
                {
                    Height = 32,
                    AutoSizeAxes = Axes.X,
                    Masking = true,
                    CornerRadius = 16,
                    CornerExponent = 2f,
                    MaskingSmoothness = 1.8f,
                    BorderThickness = 1f,
                    BorderColour = new Color4(140, 159, 219, 128),
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(18, 24, 58, 204),
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.Y,
                            AutoSizeAxes = Axes.X,
                            Direction = FillDirection.Horizontal,
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Spacing = new Vector2(5, 0),
                            Padding = new MarginPadding { Horizontal = 8 },
                            Children = new Drawable[]
                            {
                                new CircularContainer
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Size = new Vector2(14),
                                    Masking = true,
                                    MaskingSmoothness = 1.2f,
                                    BorderThickness = 1f,
                                    BorderColour = new Color4(215, 225, 255, 100),
                                    Child = new SpriteIcon
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Icon = FontAwesome.Regular.Clock,
                                        Size = new Vector2(7),
                                        Colour = Color4.White.Opacity(0.95f),
                                    }
                                },
                                digitalClock = new DigitalClockDisplay
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Scale = new Vector2(0.62f),
                                    ShowRuntime = true,
                                }
                            }
                        }
                    }
                };
            }

            public void SetVisibleInLayout(bool visible)
            {
                if (layoutVisible == visible)
                    return;

                layoutVisible = visible;
                this.ClearTransforms();
                this.FadeTo(visible ? 1 : 0, 140, Easing.OutQuint);
                this.ScaleTo(visible ? Vector2.One : new Vector2(0.01f, 1), 140, Easing.OutQuint);
            }

            [BackgroundDependencyLoader]
            private void load(OsuConfigManager config)
            {
                prefer24HourTime = config.GetBindable<bool>(OsuSetting.Prefer24HourTime);
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                prefer24HourTime.BindValueChanged(v => digitalClock.Use24HourDisplay = v.NewValue, true);
            }

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);
                prefer24HourTime?.UnbindAll();
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            localUser?.UnbindAll();
            unreadCount?.UnbindAll();
            layoutMode?.UnbindAll();
            densityMode?.UnbindAll();
        }
    }
}
