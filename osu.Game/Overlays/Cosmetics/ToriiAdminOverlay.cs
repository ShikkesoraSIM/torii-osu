// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>
    /// The Torii staff hub: a single admin-only overlay that gathers staff tools
    /// behind one toolbar button. Opens to a grid of tool cards; each card opens
    /// its tool. Visibility is gated on server-confirmed admin status (the
    /// toolbar button hides for non-admins, and PopIn double-checks).
    ///
    /// Tools land here incrementally — cosmetics curation first, then access /
    /// gift codes, name-change approvals and restrictions.
    /// </summary>
    public partial class ToriiAdminOverlay : OsuFocusedOverlayContainer, INamedOverlayComponent
    {
        public IconUsage Icon => FontAwesome.Solid.UserShield;
        public LocalisableString Title => "torii admin";
        public LocalisableString Description => "staff tools";

        protected override string PopInSampleName => @"UI/overlay-big-pop-in";
        protected override string PopOutSampleName => @"UI/overlay-big-pop-out";
        public override bool BlockScreenWideMouse => true;

        [Resolved(canBeNull: true)]
        private osu.Game.Online.API.IAPIProvider api { get; set; }

        [Resolved(canBeNull: true)]
        private CosmeticAdminOverlay cosmeticAdmin { get; set; }

        private BriefingGlass mainPanel;
        private Container toastHost;

        public ToriiAdminOverlay()
        {
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;
        }

        /// <summary>Server-confirmed admin: is_admin flag OR torii-admin group.</summary>
        public static bool IsAdmin(APIUser u) =>
            u != null && (u.IsAdmin || (u.Groups?.Any(g => g.Identifier == "torii-admin") ?? false));

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0.6f), Color4.Black.Opacity(0.72f)),
                },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.62f, 0.7f),
                    Children = new Drawable[]
                    {
                        mainPanel = new BriefingGlass
                        {
                            RelativeSizeAxes = Axes.Both,
                            RelativeContentSize = Axes.Both,
                            CornerSize = BriefingTheme.CornerLg,
                            ShadowOpacity = 0.4f,
                            ShadowRadius = 30,
                            Child = new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding(BriefingTheme.SpacingLg),
                                RowDimensions = new[]
                                {
                                    new Dimension(GridSizeMode.AutoSize),
                                    new Dimension(GridSizeMode.Absolute, BriefingTheme.SpacingLg),
                                    new Dimension(),
                                },
                                Content = new[]
                                {
                                    new Drawable[] { createHeader() },
                                    new Drawable[] { Empty() },
                                    new Drawable[]
                                    {
                                        new OsuScrollContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            ScrollbarVisible = false,
                                            Child = createTools(),
                                        },
                                    },
                                },
                            },
                        },
                        new CloseButton
                        {
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            Margin = new MarginPadding(12),
                            Action = Hide,
                        },
                    },
                },
                toastHost = new Container { RelativeSizeAxes = Axes.Both },
            };
        }

        private Drawable createHeader() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(12, 0),
            Children = new Drawable[]
            {
                new SpriteIcon
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Icon = FontAwesome.Solid.UserShield,
                    Size = new Vector2(26),
                    Colour = BriefingTheme.AccentPink,
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 2),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = "Torii Admin",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeDisplay, weight: FontWeight.SemiBold),
                        },
                        new OsuSpriteText
                        {
                            Text = "Staff tools. Server-validated — actions are checked again on the server.",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                            Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                        },
                    },
                },
            },
        };

        private Drawable createTools() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Full,
            Spacing = new Vector2(12, 12),
            Children = new Drawable[]
            {
                new ToolCard(FontAwesome.Solid.Store, BriefingTheme.AccentPink, "Cosmetics",
                    "Curate the store pool — show or hide trails, name colours and auras.", true,
                    openCosmetics),
                new ToolCard(FontAwesome.Solid.TicketAlt, BriefingTheme.AccentAmber, "Access codes",
                    "Mint codes that grant points or specific cosmetics to chosen players.", false,
                    () => toast("Access codes — coming soon")),
                new ToolCard(FontAwesome.Solid.Signature, BriefingTheme.AccentSky, "Name changes",
                    "Review and approve or reject pending username change requests.", false,
                    () => toast("Name changes — coming soon")),
                new ToolCard(FontAwesome.Solid.Gavel, BriefingTheme.AccentLoss, "Restrictions",
                    "Restrict or ban users, and review restriction history.", false,
                    () => toast("Restrictions — coming soon")),
            },
        };

        private void openCosmetics()
        {
            if (cosmeticAdmin == null)
            {
                toast("Cosmetics panel unavailable");
                return;
            }

            // Open the cosmetics tool over the hub; closing it returns here.
            cosmeticAdmin.Show();
        }

        private void toast(string message)
        {
            var t = new Container
            {
                Anchor = Anchor.BottomCentre,
                Origin = Anchor.BottomCentre,
                Margin = new MarginPadding { Bottom = 40 },
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 8,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(20, 21, 28, 235) },
                    new OsuSpriteText
                    {
                        Margin = new MarginPadding { Horizontal = 18, Vertical = 10 },
                        Text = message,
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                    },
                },
            };

            toastHost.Add(t);
            t.FadeInFromZero(150, Easing.OutQuint);
            t.ScaleTo(0.9f).ScaleTo(1f, 360, Easing.OutBack);
            t.Delay(1500).FadeOut(320, Easing.OutQuint).Expire();
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (mainPanel != null && !mainPanel.ReceivePositionalInputAt(e.ScreenSpaceMousePosition))
            {
                Hide();
                return true;
            }

            return base.OnClick(e);
        }

        public override bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (!e.Repeat && e.Action == GlobalAction.Back && State.Value == Visibility.Visible)
            {
                Hide();
                return true;
            }

            return base.OnPressed(e);
        }

        protected override void PopIn()
        {
            // Defence in depth: never show for a non-admin even if something
            // managed to toggle us (the toolbar button already hides for them).
            if (!IsAdmin(api?.LocalUser.Value))
            {
                Hide();
                return;
            }

            this.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            mainPanel.ScaleTo(0.94f).ScaleTo(1f, BriefingTheme.EntranceDuration, Easing.OutBack)
                     .MoveToY(20).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.FadeOut(BriefingTheme.DismissDuration, Easing.OutQuint);
            mainPanel.ScaleTo(0.97f, BriefingTheme.DismissDuration, Easing.OutQuint);
        }

        // A single staff tool: icon, title, description, READY / SOON pill.
        private partial class ToolCard : OsuClickableContainer
        {
            private readonly IconUsage icon;
            private readonly Color4 accent;
            private readonly string title;
            private readonly string description;
            private readonly bool ready;

            private Container content;
            private Box hover;

            public ToolCard(IconUsage icon, Color4 accent, string title, string description, bool ready, Action action)
            {
                this.icon = icon;
                this.accent = accent;
                this.title = title;
                this.description = description;
                this.ready = ready;
                Action = action;
                Size = new Vector2(258, 132);
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Child = content = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        new BriefingGlass
                        {
                            RelativeSizeAxes = Axes.Both,
                            RelativeContentSize = Axes.Both,
                            CornerSize = BriefingTheme.CornerSm,
                            SurfaceLift = 1.2f,
                            ShadowOpacity = 0.2f,
                            ShadowRadius = 10f,
                            Child = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Masking = true,
                                CornerRadius = BriefingTheme.CornerSm,
                                Children = new Drawable[]
                                {
                                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(18, 19, 26, 255) },
                                    new SpriteIcon
                                    {
                                        Anchor = Anchor.TopLeft,
                                        Origin = Anchor.TopLeft,
                                        Margin = new MarginPadding { Left = 14, Top = 14 },
                                        Icon = icon,
                                        Size = new Vector2(22),
                                        Colour = accent,
                                    },
                                    statusPill(),
                                    new FillFlowContainer
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Anchor = Anchor.BottomLeft,
                                        Origin = Anchor.BottomLeft,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, 3),
                                        Padding = new MarginPadding { Horizontal = 14, Bottom = 12 },
                                        Children = new Drawable[]
                                        {
                                            new OsuSpriteText
                                            {
                                                Text = title,
                                                Font = OsuFont.GetFont(size: BriefingTheme.TypeHeadline, weight: FontWeight.SemiBold),
                                            },
                                            new TextFlowContainer(t => t.Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption))
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                                Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                                                Text = description,
                                            },
                                        },
                                    },
                                    hover = new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = Color4.White.Opacity(0.06f),
                                        Blending = BlendingParameters.Additive,
                                        Alpha = 0,
                                    },
                                },
                            },
                        },
                    },
                };

                if (!ready)
                    content.Alpha = 0.82f;
            }

            private Drawable statusPill() => new Container
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Margin = new MarginPadding { Right = 12, Top = 12 },
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 5f,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = (ready ? BriefingTheme.AccentGain : new Color4(150, 155, 165, 255)).Opacity(0.9f),
                    },
                    new OsuSpriteText
                    {
                        Margin = new MarginPadding { Horizontal = 7, Vertical = 3 },
                        Text = ready ? "READY" : "SOON",
                        Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                        Colour = Color4.Black.Opacity(0.85f),
                    },
                },
            };

            protected override bool OnHover(HoverEvent e)
            {
                content.ScaleTo(1.03f, 220, Easing.OutQuint);
                hover.FadeTo(1, 160, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                content.ScaleTo(1f, 260, Easing.OutQuint);
                hover.FadeTo(0, 220, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }

        // Round corner "X" close button (mirrors the store overlay's).
        private partial class CloseButton : OsuClickableContainer
        {
            private Box bg;

            public CloseButton()
            {
                Size = new Vector2(30);
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Child = new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Children = new Drawable[]
                    {
                        bg = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black.Opacity(0.4f) },
                        new SpriteIcon
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Icon = FontAwesome.Solid.Times,
                            Size = new Vector2(13),
                            Colour = Color4.White,
                        },
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                bg.FadeColour(BriefingTheme.AccentPink, 150, Easing.OutQuint);
                this.ScaleTo(1.1f, 150, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                bg.FadeColour(Color4.Black.Opacity(0.4f), 200, Easing.OutQuint);
                this.ScaleTo(1f, 200, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
