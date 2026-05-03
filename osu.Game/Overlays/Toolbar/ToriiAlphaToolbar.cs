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
    /// <summary>
    /// The "alpha" toolbar look for Torii — a single rounded-full glass pill
    /// that intentionally mirrors the web frontend navbar
    /// (<c>torii-lazer-web/src/components/Layout/Navbar.tsx</c>) rather than
    /// stretching across the whole top of the screen like classic lazer.
    ///
    /// Why a rewrite
    /// -------------
    /// The previous version tried to be three things at once — three density
    /// presets ("Compact / Default / Comfortable"), an adaptive-layout pass
    /// that hid pieces at three different width thresholds, and a horizontally
    /// scrolling nav strip — and the result was a too-thin, mis-aligned bar
    /// that sprawled across the full viewport even when it had four chips
    /// to display. The user complained, fairly: "es mas finita, esta todo
    /// como el orto y mal alineado, muchas cosas faltan, es ree larga aunque
    /// no necesitarsela".
    ///
    /// What we do here
    /// --------------
    /// One design, one set of dimensions. The bar is intrinsic-width
    /// (<see cref="Axes.X"/> auto-sized) and centred at the top, so it
    /// shrinks to the size of its content instead of always being a 1320px
    /// strip. Three content blocks are laid out left/centre/right with fixed
    /// spacing between them, mirroring the web's
    /// <c>grid-cols-[1fr_auto_1fr]</c>. The chips are taller (36px) with the
    /// icon embedded in its own little circle to read the same way the web
    /// chips do, and the active state uses the same osu-pink gradient.
    ///
    /// Wiring preserved
    /// ----------------
    /// Same set of <c>[Resolved]</c> overlays as the previous version:
    /// rankings / beatmap-listing / settings / notifications / login /
    /// <see cref="OsuGame"/> for external link opens. Same bindables on
    /// <see cref="IAPIProvider.LocalUser"/> and the notification overlay's
    /// unread count. Toolbar.cs still mounts this with
    /// <c>RelativeSizeAxes = Axes.Both</c> so we just fill the reserved
    /// height the parent gives us.
    /// </summary>
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
        private IBindable<bool> compactRequested;

        private AlphaActionButton notificationButton;
        private AlphaActionButton settingsButton;
        private AlphaUserChip userChip;
        private Container subtitleContainer;
        private Drawable brandTextFlow;
        private Drawable navChipsFlow;
        private AlphaClockPill clockPill;

        // Tracks the last applied responsive state so Update() doesn't
        // re-trigger the fade/scale every frame while a transition is
        // mid-flight (sampling Alpha during a fade gives a misleading
        // mid-value and would cause oscillation).
        private bool? lastWideState;
        private bool? lastCompactState;

        // Single design — no density modes, no adaptive thresholds.
        // Tweak these if you want a different overall feel; everything
        // else (corner radius, chip sizes, etc.) is derived from them.
        private const float bar_height = 56f;
        private const float bar_corner_radius = bar_height / 2f;

        public ToriiAlphaToolbar(Action onHome)
        {
            this.onHome = onHome;
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours, IAPIProvider api)
        {
            localUser = api.LocalUser.GetBoundCopy();
            unreadCount = notificationOverlay?.UnreadCount.GetBoundCopy() ?? new BindableInt();

            // Endpoint URLs sometimes ship without scheme; OpenUrlExternally
            // wants a full URL, so normalise here once.
            string websiteUrl = api.Endpoints.WebsiteUrl ?? string.Empty;
            if (!string.IsNullOrEmpty(websiteUrl)
                && !websiteUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !websiteUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                websiteUrl = $"https://{websiteUrl}";

            InternalChild = new Container
            {
                // Centre horizontally on whatever width the parent toolbar
                // gives us. AutoSize on X means we do NOT sprawl: the pill
                // is exactly as wide as the brand + chips + actions need.
                // The reserved toolbar height (56px in Toolbar.cs) is
                // exactly the pill height, so Y=0 is flush with the
                // screen top. The drop shadow extends BELOW the pill
                // into song-select content territory — that's pure
                // visual halo (EdgeEffect doesn't capture input) so it
                // doesn't break interactivity, and lets us reclaim the
                // pixels the user was missing for the carousel.
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.X,
                Height = bar_height,
                Y = 0,
                Masking = true,
                CornerRadius = bar_corner_radius,
                CornerExponent = 2.4f,
                MaskingSmoothness = 1.6f,
                BorderThickness = 1f,
                BorderColour = new Color4(150, 168, 230, 90),
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Radius = 14,
                    Roundness = 12,
                    Colour = new Color4(0, 4, 24, 150),
                    Offset = new Vector2(0, 3),
                },
                Children = new Drawable[]
                {
                    // Glassmorphic base. The blurred buffered container
                    // gives the soft halo, the box on top is the dark
                    // tint, the gradient adds a pink-to-blue hint that
                    // matches the web frontend's `torii-nav-liquid`.
                    new BufferedContainer(cachedFrameBuffer: true)
                    {
                        RelativeSizeAxes = Axes.Both,
                        DrawOriginal = true,
                        BlurSigma = new Vector2(12),
                        EffectColour = new Color4(140, 168, 240, 80),
                        Child = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(12, 14, 32, 232),
                        }
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientHorizontal(
                            new Color4(120, 100, 220, 28),
                            new Color4(255, 130, 195, 32)),
                    },
                    // Top inside-edge highlight for that "lifted glass" feel.
                    new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 1,
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Colour = new Color4(225, 232, 255, 80),
                    },
                    // Single horizontal flow: brand | nav | actions.
                    // Spacing between sections is generous so the centre
                    // chips don't feel crammed against the brand block.
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.Y,
                        AutoSizeAxes = Axes.X,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(28, 0),
                        Padding = new MarginPadding { Horizontal = 14 },
                        Children = new Drawable[]
                        {
                            createBrandBlock(colours),
                            createNavChips(websiteUrl),
                            createActionBlock(),
                        }
                    },
                }
            };

            localUser.BindValueChanged(v => userChip?.UpdateUser(v.NewValue), true);
            unreadCount.BindValueChanged(v => notificationButton?.SetBadge(v.NewValue), true);

            // Compact-mode tracking: when the active screen is one of
            // the canvas screens (song select / player / results /
            // editor) the pill collapses to logo + bell + user so it
            // stops competing with screen-owned UI like song-select's
            // filter row. The bindable is owned by OsuGame and updated
            // in OsuGame.ScreenChanged. We default to a fresh, never-
            // changing bindable when game is null (test contexts) so
            // the toolbar always boots in full mode there.
            compactRequested = game?.CompactToolbarRequested?.GetBoundCopy() ?? new BindableBool();
            compactRequested.BindValueChanged(v => applyCompactMode(v.NewValue, animated: true), true);
        }

        protected override void Update()
        {
            base.Update();

            // Lightweight responsive trick: in tight viewports (small
            // windowed sessions) hide the long subtitle so the brand
            // block doesn't dominate the bar. The compact mode driven
            // by the active screen (see applyCompactMode) ALSO hides
            // the subtitle — but via the brandTextFlow wrapper — so
            // the two effects compose without either overriding the
            // other. The clock used to live here too; it's now driven
            // entirely by compact mode (clock disappears on canvas
            // screens, stays on menu).
            bool wide = DrawWidth >= 1180f;
            if (lastWideState == wide)
                return;

            lastWideState = wide;

            if (subtitleContainer != null)
            {
                subtitleContainer.ClearTransforms();
                subtitleContainer.FadeTo(wide ? 1 : 0, 180, Easing.OutQuint);
                subtitleContainer.ScaleTo(wide ? Vector2.One : new Vector2(0.92f, 1f), 180, Easing.OutQuint);
            }
        }

        /// <summary>
        /// Switch between full and compact pill layouts.
        ///
        /// FULL: brand text + nav chips + settings + bell + user + clock.
        /// COMPACT: logo + bell + user only — drops ~75% of the pill's
        /// horizontal footprint so the centred pill stops covering up
        /// screen-owned UI in song-select / player / results / editor.
        ///
        /// We collapse by animating each hideable group's Alpha + ScaleX
        /// to 0 in tandem. Once Alpha reaches 0 the drawable is no
        /// longer "Present", so the parent FillFlowContainer drops it
        /// from layout and the pill shrinks. Going back to full reverses
        /// the animation; the FillFlow re-includes the children once
        /// they become Present again. Animated=false jumps to the
        /// final state without easing — used for the initial apply
        /// before the first frame so the toolbar boots in the right
        /// shape instead of doing a visible collapse on screen change.
        /// </summary>
        private void applyCompactMode(bool compact, bool animated)
        {
            if (lastCompactState == compact)
                return;

            lastCompactState = compact;

            int duration = animated ? 260 : 0;
            const Easing easing = Easing.OutQuint;

            float targetAlpha = compact ? 0f : 1f;
            Vector2 targetScale = compact ? new Vector2(0f, 1f) : Vector2.One;

            foreach (var d in new[] { brandTextFlow, navChipsFlow, (Drawable)settingsButton, clockPill })
            {
                if (d == null)
                    continue;

                d.ClearTransforms();
                d.FadeTo(targetAlpha, duration, easing);
                d.ScaleTo(targetScale, duration, easing);
            }
        }

        private Drawable createBrandBlock(OsuColour colours)
        {
            // All three section containers (brand / nav / actions) use
            // the SAME Anchor (CentreLeft) inside the outer horizontal
            // FillFlow because FillFlowContainer requires every child
            // to share the X-component of its RelativeAnchorPosition
            // when FillDirection is Horizontal — mixing CentreLeft +
            // Centre + CentreRight throws "0 != 0.5". Their visual
            // left/centre/right positioning comes from the flow order
            // and spacing, not from individual anchors.
            return new FillFlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(10, 0),
                Children = new Drawable[]
                {
                    // Logo mark — circular, soft pink-to-purple gradient
                    // ring that mimics the web BrandMark component.
                    new CircularContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(36),
                        Masking = true,
                        MaskingSmoothness = 1.4f,
                        BorderThickness = 1.2f,
                        BorderColour = new Color4(160, 175, 230, 130),
                        EdgeEffect = new EdgeEffectParameters
                        {
                            Type = EdgeEffectType.Shadow,
                            Radius = 8,
                            Roundness = 6,
                            Colour = new Color4(255, 100, 190, 70),
                        },
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientVertical(
                                    new Color4(76, 52, 145, 235),
                                    new Color4(36, 26, 70, 235)),
                            },
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientHorizontal(
                                    new Color4(192, 132, 252, 60),
                                    new Color4(255, 122, 24, 30)),
                            },
                            new SpriteIcon
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Icon = FontAwesome.Solid.ToriiGate,
                                Size = new Vector2(17),
                                Colour = colours.Pink.Lighten(0.05f),
                            },
                        }
                    },
                    // Title + subtitle stacked vertically. The whole
                    // block collapses in compact mode (logo stays);
                    // the subtitle ALSO independently hides on narrow
                    // viewports via the wide-state check in Update().
                    brandTextFlow = new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 0),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = "Torii",
                                Font = OsuFont.GetFont(size: 17, weight: FontWeight.SemiBold),
                                Colour = Color4.White,
                            },
                            subtitleContainer = new Container
                            {
                                AutoSizeAxes = Axes.Both,
                                Margin = new MarginPadding { Top = -1 },
                                Child = new OsuSpriteText
                                {
                                    Text = "forged in Shikke's Dojo",
                                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.Regular),
                                    Colour = new Color4(190, 200, 230, 200),
                                }
                            }
                        }
                    }
                }
            };
        }

        private Drawable createNavChips(string websiteUrl)
        {
            // Home is "always active" — there's no overlay to bind it to
            // and the user is essentially always on the main menu when
            // the toolbar is showing, so we keep the pink fill on it as
            // an anchor visual.
            var homeChip = new AlphaNavChip("Home", FontAwesome.Solid.Home)
            {
                Action = () => onHome?.Invoke(),
            };
            homeChip.SetPersistentActive(true);

            return navChipsFlow = new FillFlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(6, 0),
                Children = new Drawable[]
                {
                    homeChip,
                    createOverlayChip("Rankings", FontAwesome.Solid.ChartLine, rankingsOverlay),
                    createOverlayChip("Beatmaps", FontAwesome.Solid.CompactDisc, beatmapListingOverlay),
                    new AlphaNavChip("Join Server", FontAwesome.Solid.Link)
                    {
                        Action = string.IsNullOrEmpty(websiteUrl)
                            ? null
                            : () => game?.OpenUrlExternally(websiteUrl),
                    },
                }
            };
        }

        private Drawable createActionBlock()
        {
            settingsButton = new AlphaActionButton(FontAwesome.Solid.Cog);
            if (settingsOverlay != null)
                settingsButton.BindOverlay(settingsOverlay);
            else
                settingsButton.Enabled.Value = false;

            notificationButton = new AlphaActionButton(FontAwesome.Regular.Bell);
            if (notificationOverlay != null)
                notificationButton.BindOverlay(notificationOverlay);
            else
                notificationButton.Enabled.Value = false;

            userChip = new AlphaUserChip
            {
                Action = () => loginOverlay?.ToggleVisibility(),
            };

            clockPill = new AlphaClockPill();

            return new FillFlowContainer
            {
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(8, 0),
                Children = new Drawable[]
                {
                    settingsButton,
                    notificationButton,
                    userChip,
                    clockPill,
                }
            };
        }

        private AlphaNavChip createOverlayChip(LocalisableString text, IconUsage icon, OverlayContainer overlay)
        {
            var chip = new AlphaNavChip(text, icon);
            if (overlay != null)
                chip.BindOverlay(overlay);
            else
                chip.Enabled.Value = false;
            return chip;
        }

        // ─── Sub-components ──────────────────────────────────────────

        /// <summary>
        /// Shared visual base for chips and circular action buttons.
        /// Owns the inactive / active / hover background layers and the
        /// content flow that subclasses fill with icon + label.
        /// </summary>
        private abstract partial class AlphaButtonBase : OsuClickableContainer
        {
            private readonly Box inactiveBackground;
            private readonly Box activeBackground;
            private readonly Box hoverLayer;
            private readonly Container backgroundContainer;

            private readonly float activeShadowAlpha;
            private IBindable<Visibility> boundOverlayState;

            private bool overlayVisible;
            private bool persistActive;

            protected readonly FillFlowContainer ContentFlow;

            protected AlphaButtonBase(bool iconOnly, float height, float horizontalPadding, float inactiveAlpha, float activeShadowAlpha = 0.45f)
            {
                this.activeShadowAlpha = activeShadowAlpha;

                Height = height;

                if (iconOnly)
                    Size = new Vector2(height);
                else
                    AutoSizeAxes = Axes.X;

                float cornerRadius = height / 2f;

                Children = new Drawable[]
                {
                    backgroundContainer = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = cornerRadius,
                        CornerExponent = 2.2f,
                        MaskingSmoothness = 1.6f,
                        Children = new Drawable[]
                        {
                            inactiveBackground = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(255, 255, 255, 22),
                                Alpha = inactiveAlpha,
                            },
                            activeBackground = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientHorizontal(
                                    new Color4(255, 91, 189, 255),
                                    new Color4(253, 164, 175, 255)),
                                Alpha = 0,
                            },
                            hoverLayer = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = Color4.White.Opacity(0.10f),
                                Alpha = 0,
                            },
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
                hoverLayer.FadeIn(140, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                hoverLayer.FadeOut(140, Easing.OutQuint);
                base.OnHoverLost(e);
            }

            protected override bool OnClick(ClickEvent e)
            {
                this.ScaleTo(0.96f, 80, Easing.OutQuint)
                    .Then()
                    .ScaleTo(1f, 220, Easing.OutElasticHalf);
                return base.OnClick(e);
            }

            private void updateVisualState()
            {
                bool active = overlayVisible || persistActive;

                activeBackground.FadeTo(active ? 1f : 0f, 200, Easing.OutQuint);
                inactiveBackground.FadeTo(active ? 0f : inactiveBackground.Alpha, 200, Easing.OutQuint);

                // Soft pink halo when active — matches the web design's
                // `shadow-[#ff5bbd]/25`. We toggle the EdgeEffect colour
                // alpha in place so we don't churn the parameter struct.
                backgroundContainer.TransformTo(nameof(EdgeEffect),
                    new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Shadow,
                        Radius = active ? 12 : 0,
                        Roundness = 8,
                        Colour = new Color4(255, 91, 189, (byte)(active ? activeShadowAlpha * 255 : 0)),
                    }, 220, Easing.OutQuint);

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

        /// <summary>
        /// Pill-shaped chip with an icon embedded in its own little circle
        /// (matching the web NavItem) plus a label. Used for the centre
        /// nav row.
        /// </summary>
        private partial class AlphaNavChip : AlphaButtonBase
        {
            private const float chip_height = 36f;
            private const float icon_circle_size = 24f;

            private readonly CircularContainer iconRing;
            private readonly Box iconRingFill;
            private readonly SpriteIcon iconSprite;
            private readonly OsuSpriteText label;

            public AlphaNavChip(LocalisableString text, IconUsage icon)
                : base(iconOnly: false, height: chip_height, horizontalPadding: 7, inactiveAlpha: 0)
            {
                ContentFlow.Spacing = new Vector2(8, 0);

                ContentFlow.AddRange(new Drawable[]
                {
                    iconRing = new CircularContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(icon_circle_size),
                        Masking = true,
                        MaskingSmoothness = 1.2f,
                        BorderThickness = 1f,
                        BorderColour = new Color4(255, 255, 255, 32),
                        Children = new Drawable[]
                        {
                            iconRingFill = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = Color4.White.Opacity(0.05f),
                            },
                            iconSprite = new SpriteIcon
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Icon = icon,
                                Size = new Vector2(12),
                                Colour = Color4.White.Opacity(0.92f),
                            },
                        }
                    },
                    label = new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = text,
                        Font = OsuFont.GetFont(size: 13.5f, weight: FontWeight.SemiBold),
                        Colour = new Color4(220, 226, 248, 200),
                        Margin = new MarginPadding { Right = 4, Top = -0.5f },
                    },
                });
            }

            protected override void updateActiveState(bool active)
            {
                iconRing.TransformTo(nameof(BorderColour),
                    (ColourInfo)(active ? new Color4(255, 255, 255, 90) : new Color4(255, 255, 255, 32)),
                    180, Easing.OutQuint);
                iconRingFill.FadeTo(active ? 0.18f : 0.05f, 180, Easing.OutQuint);
                iconSprite.FadeColour(active ? Color4.White : Color4.White.Opacity(0.92f), 180, Easing.OutQuint);
                label.FadeColour(active ? Color4.White : new Color4(220, 226, 248, 200), 180, Easing.OutQuint);
            }
        }

        /// <summary>
        /// Circular icon-only button — settings cog, notification bell, etc.
        /// Slightly bigger than a nav chip so the targets feel comfortable
        /// and the bell badge sits cleanly on the corner.
        /// </summary>
        private partial class AlphaActionButton : AlphaButtonBase
        {
            private const float action_size = 40f;

            private readonly CircularContainer ring;
            private readonly SpriteIcon icon;
            private readonly CircularContainer badgeContainer;
            private readonly OsuSpriteText badgeText;

            public AlphaActionButton(IconUsage icon)
                : base(iconOnly: true, height: action_size, horizontalPadding: 0, inactiveAlpha: 1f, activeShadowAlpha: 0.5f)
            {
                Add(ring = new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    MaskingSmoothness = 1.2f,
                    BorderThickness = 1f,
                    BorderColour = new Color4(255, 255, 255, 28),
                });

                ContentFlow.Child = this.icon = new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = icon,
                    Size = new Vector2(15),
                    Colour = Color4.White.Opacity(0.94f),
                };

                Add(badgeContainer = new CircularContainer
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.Centre,
                    Position = new Vector2(-2, 4),
                    AutoSizeAxes = Axes.X,
                    Height = 17,
                    Masking = true,
                    MaskingSmoothness = 1.2f,
                    BorderThickness = 1.4f,
                    BorderColour = new Color4(12, 14, 32, 255),
                    Alpha = 0,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(239, 68, 68, 255),
                        },
                        badgeText = new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                            UseFullGlyphHeight = true,
                            Padding = new MarginPadding { Horizontal = 5, Top = 0.5f },
                            Colour = Color4.White,
                        },
                    },
                });
            }

            public void SetBadge(int value)
            {
                if (value <= 0)
                {
                    badgeContainer.FadeOut(140, Easing.OutQuint);
                    return;
                }

                badgeContainer.FadeIn(140, Easing.OutQuint);
                badgeText.Text = value > 99 ? "99+" : value.ToString();
            }

            protected override void updateActiveState(bool active)
            {
                ring.TransformTo(nameof(BorderColour),
                    (ColourInfo)(active ? new Color4(255, 255, 255, 110) : new Color4(255, 255, 255, 28)),
                    180, Easing.OutQuint);
                icon.FadeColour(active ? Color4.White : Color4.White.Opacity(0.94f), 180, Easing.OutQuint);
            }
        }

        /// <summary>
        /// Avatar + username + chevron, shaped exactly like a nav chip so
        /// it visually balances against the centre row.
        /// </summary>
        private partial class AlphaUserChip : AlphaButtonBase
        {
            private const float chip_height = 40f;
            private const float avatar_size = 28f;

            private readonly OsuSpriteText usernameText;
            private readonly UpdateableAvatar avatar;
            private readonly CircularContainer avatarRing;

            public AlphaUserChip()
                : base(iconOnly: false, height: chip_height, horizontalPadding: 6, inactiveAlpha: 1f)
            {
                ContentFlow.Spacing = new Vector2(8, 0);

                ContentFlow.AddRange(new Drawable[]
                {
                    avatarRing = new CircularContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(avatar_size),
                        Masking = true,
                        MaskingSmoothness = 1.2f,
                        BorderThickness = 1.2f,
                        BorderColour = new Color4(255, 255, 255, 60),
                        Child = avatar = new UpdateableAvatar(isInteractive: false)
                        {
                            RelativeSizeAxes = Axes.Both,
                        }
                    },
                    usernameText = new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.GetFont(size: 13.5f, weight: FontWeight.SemiBold),
                        Colour = Color4.White,
                        Margin = new MarginPadding { Top = -0.5f },
                        Text = "Guest",
                    },
                    new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Icon = FontAwesome.Solid.ChevronDown,
                        Size = new Vector2(9),
                        Colour = Color4.White.Opacity(0.7f),
                        Margin = new MarginPadding { Right = 4 },
                    },
                });
            }

            public void UpdateUser(APIUser user)
            {
                usernameText.Text = string.IsNullOrWhiteSpace(user?.Username) ? "Guest" : user.Username;
                avatar.User = user;
            }

            protected override void updateActiveState(bool active)
            {
                avatarRing.TransformTo(nameof(BorderColour),
                    (ColourInfo)(active ? new Color4(255, 255, 255, 140) : new Color4(255, 255, 255, 60)),
                    180, Easing.OutQuint);
            }
        }

        /// <summary>
        /// The clock pill on the right edge — kept from the previous
        /// implementation but resized to match the new chip height and
        /// recoloured to match the new pill aesthetic.
        /// </summary>
        private partial class AlphaClockPill : CompositeDrawable
        {
            private Bindable<bool> prefer24HourTime;
            private DigitalClockDisplay digitalClock;

            public AlphaClockPill()
            {
                AutoSizeAxes = Axes.Both;

                InternalChild = new Container
                {
                    Height = 36,
                    AutoSizeAxes = Axes.X,
                    Masking = true,
                    CornerRadius = 18,
                    CornerExponent = 2.2f,
                    MaskingSmoothness = 1.6f,
                    BorderThickness = 1f,
                    BorderColour = new Color4(255, 255, 255, 28),
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(255, 255, 255, 22),
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.Y,
                            AutoSizeAxes = Axes.X,
                            Direction = FillDirection.Horizontal,
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Spacing = new Vector2(6, 0),
                            Padding = new MarginPadding { Horizontal = 11 },
                            Children = new Drawable[]
                            {
                                new SpriteIcon
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Icon = FontAwesome.Regular.Clock,
                                    Size = new Vector2(11),
                                    Colour = Color4.White.Opacity(0.78f),
                                },
                                digitalClock = new DigitalClockDisplay
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Scale = new Vector2(0.66f),
                                    ShowRuntime = true,
                                },
                            }
                        }
                    }
                };
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
        }
    }
}
