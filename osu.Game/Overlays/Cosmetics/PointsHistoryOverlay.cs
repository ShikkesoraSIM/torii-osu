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
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>
    /// The player's own points ledger: a scrollable list of every earn and spend,
    /// newest first, with running balance. Opened from the toolbar currency pill.
    /// Reads <c>GET /torii/points/me/history</c> a page at a time (Load more appends).
    /// </summary>
    public partial class PointsHistoryOverlay : OsuFocusedOverlayContainer, INamedOverlayComponent
    {
        public IconUsage Icon => FontAwesome.Solid.Coins;
        public LocalisableString Title => "points history";
        public LocalisableString Description => "your earnings and spends";

        protected override string PopInSampleName => @"UI/overlay-big-pop-in";
        protected override string PopOutSampleName => @"UI/overlay-big-pop-out";
        public override bool BlockScreenWideMouse => true;

        private const int page_size = 50;

        // Brand-consistent: gains in the green accent, spends in a soft red so the
        // two read apart at a glance without either screaming.
        private static readonly Color4 spend_colour = new Color4(0.94f, 0.45f, 0.45f, 1f);

        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        [Resolved(canBeNull: true)]
        private ToriiCosmeticsManager cosmetics { get; set; }

        private BriefingGlass mainPanel;
        private FillFlowContainer rowsFlow;
        private OsuSpriteText balanceText;
        private OsuSpriteText statusText;
        private LoadMoreButton loadMore;

        private int offset;
        private bool loading;

        public PointsHistoryOverlay()
        {
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;
        }

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
                    Size = new Vector2(0.52f, 0.8f),
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
                                    new Dimension(GridSizeMode.Absolute, BriefingTheme.SpacingMd),
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
                                            Child = new FillFlowContainer
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                                Direction = FillDirection.Vertical,
                                                Spacing = new Vector2(0, BriefingTheme.SpacingMd),
                                                Children = new Drawable[]
                                                {
                                                    rowsFlow = new FillFlowContainer
                                                    {
                                                        RelativeSizeAxes = Axes.X,
                                                        AutoSizeAxes = Axes.Y,
                                                        Direction = FillDirection.Vertical,
                                                        Spacing = new Vector2(0, 4),
                                                    },
                                                    new Container
                                                    {
                                                        RelativeSizeAxes = Axes.X,
                                                        AutoSizeAxes = Axes.Y,
                                                        Child = loadMore = new LoadMoreButton
                                                        {
                                                            Anchor = Anchor.TopCentre,
                                                            Origin = Anchor.TopCentre,
                                                            Action = fetchNextPage,
                                                            Alpha = 0,
                                                        },
                                                    },
                                                },
                                            },
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
                new Container
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Size = new Vector2(40),
                    Children = new Drawable[]
                    {
                        new Circle { RelativeSizeAxes = Axes.Both, Colour = BriefingTheme.AccentAmber.Opacity(0.16f) },
                        new SpriteIcon
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Icon = FontAwesome.Solid.Coins,
                            Size = new Vector2(19),
                            Colour = BriefingTheme.AccentAmber,
                        },
                    },
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
                        balanceText = new OsuSpriteText
                        {
                            Text = "-- points",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
                        },
                        statusText = new OsuSpriteText
                        {
                            Text = "Points history",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                            Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                        },
                    },
                },
            },
        };

        private Drawable transactionRow(APIPointTransaction t)
        {
            bool gain = t.Amount >= 0;
            string amountStr = $"{(gain ? "+" : "-")}{Math.Abs(t.Amount):N0}";

            return new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = BriefingTheme.CornerSm,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White.Opacity(0.04f) },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        // Padding (not Margin): Margin on a RelativeSizeAxes.X drawable overflows
                        // the parent and clips the right-hand content.
                        Padding = new MarginPadding { Horizontal = 14, Vertical = 9 },
                        ColumnDimensions = new[]
                        {
                            new Dimension(),
                            new Dimension(GridSizeMode.AutoSize),
                        },
                        RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                        Content = new[]
                        {
                            new[]
                            {
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, 1),
                                    Children = new Drawable[]
                                    {
                                        new OsuSpriteText
                                        {
                                            Text = prettyReason(t.Reason),
                                            Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                                        },
                                        new OsuSpriteText
                                        {
                                            Text = relative(t.CreatedAtUtc),
                                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                                            Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                                        },
                                    },
                                },
                                new FillFlowContainer
                                {
                                    Anchor = Anchor.CentreRight,
                                    Origin = Anchor.CentreRight,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, 1),
                                    Children = new Drawable[]
                                    {
                                        new OsuSpriteText
                                        {
                                            Anchor = Anchor.CentreRight,
                                            Origin = Anchor.CentreRight,
                                            Text = amountStr,
                                            Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.Bold),
                                            Colour = gain ? BriefingTheme.AccentGain : spend_colour,
                                        },
                                        new OsuSpriteText
                                        {
                                            Anchor = Anchor.CentreRight,
                                            Origin = Anchor.CentreRight,
                                            Text = $"{t.BalanceAfter:N0}",
                                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                                            Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        private void refresh()
        {
            if (api?.IsLoggedIn != true)
            {
                statusText.Text = "Sign in to see your points history.";
                return;
            }

            offset = 0;
            rowsFlow.Clear();
            loadMore.Alpha = 0;
            statusText.Text = "Loading...";
            fetch(append: false);
        }

        private void fetchNextPage() => fetch(append: true);

        private void fetch(bool append)
        {
            if (loading || api?.IsLoggedIn != true)
                return;

            loading = true;
            loadMore.SetLoading(true);

            var req = new GetPointsHistoryRequest(page_size, offset);
            req.Success += res => Schedule(() => populate(res, append));
            req.Failure += _ => Schedule(() =>
            {
                loading = false;
                loadMore.SetLoading(false);
                if (offset == 0)
                    statusText.Text = "Couldn't load your history.";
            });
            api.Queue(req);
        }

        private void populate(APIPointsHistory res, bool append)
        {
            loading = false;
            loadMore.SetLoading(false);

            if (res == null)
                return;

            balanceText.Text = $"{res.Balance:N0} points";
            statusText.Text = "Newest first";

            if (cosmetics != null)
                cosmetics.PointsBalance.Value = res.Balance;

            var rows = res.Transactions ?? Array.Empty<APIPointTransaction>();

            if (!append && rows.Length == 0)
            {
                statusText.Text = "No points activity yet — play a map to start earning.";
                return;
            }

            foreach (var t in rows)
                rowsFlow.Add(transactionRow(t));

            offset += rows.Length;

            // A full page back means there may be more; a short page means we hit the end.
            loadMore.Alpha = rows.Length >= page_size ? 1 : 0;
        }

        private static string prettyReason(string reason)
        {
            switch (reason)
            {
                case "top_play": return "Top play";
                case "daily_play": return "Daily play";
                case "milestone": return "pp milestone";
                case "gift": return "Gift";
                case "access_code": return "Code redeemed";
                case "store_purchase": return "Store purchase";
                case "medal": return "Medal";
                case "admin_adjust": return "Staff adjustment";
                case "refund": return "Refund";
                default: return string.IsNullOrEmpty(reason) ? "Points" : reason.Replace("_", " ");
            }
        }

        private static string relative(DateTime? utc)
        {
            if (utc == null)
                return "";

            var span = DateTime.UtcNow - utc.Value;
            if (span < TimeSpan.Zero)
                span = TimeSpan.Zero;

            if (span.TotalSeconds < 60) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            if (span.TotalDays < 30) return $"{(int)(span.TotalDays / 7)}w ago";

            return utc.Value.ToLocalTime().ToString("d MMM yyyy");
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
            this.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            mainPanel.ScaleTo(0.94f).ScaleTo(1f, BriefingTheme.EntranceDuration, Easing.OutBack)
                     .MoveToY(20).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
            refresh();
        }

        protected override void PopOut()
        {
            this.FadeOut(BriefingTheme.DismissDuration, Easing.OutQuint);
            mainPanel.ScaleTo(0.97f, BriefingTheme.DismissDuration, Easing.OutQuint);
        }

        private partial class LoadMoreButton : OsuClickableContainer
        {
            private Box bg;
            private OsuSpriteText label;
            private bool isLoading;

            public LoadMoreButton()
            {
                AutoSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Child = new CircularContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    Children = new Drawable[]
                    {
                        bg = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White.Opacity(0.06f) },
                        label = new OsuSpriteText
                        {
                            Margin = new MarginPadding { Horizontal = 18, Vertical = 8 },
                            Text = "Load more",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                            Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                        },
                    },
                };
            }

            public void SetLoading(bool loading)
            {
                isLoading = loading;
                if (label != null)
                    label.Text = loading ? "Loading..." : "Load more";
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!isLoading)
                    bg.FadeColour(Color4.White.Opacity(0.12f), 150, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                bg.FadeColour(Color4.White.Opacity(0.06f), 200, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }

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
