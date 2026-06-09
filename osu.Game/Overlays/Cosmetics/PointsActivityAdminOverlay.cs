// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

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
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>
    /// Staff anti-abuse view: top points earners in a window plus recent large
    /// awards, read from the admin activity endpoint. Opened from the staff hub.
    /// </summary>
    public partial class PointsActivityAdminOverlay : OsuFocusedOverlayContainer, INamedOverlayComponent
    {
        public IconUsage Icon => FontAwesome.Solid.ChartLine;
        public LocalisableString Title => "points activity";
        public LocalisableString Description => "staff anti-abuse";

        protected override string PopInSampleName => @"UI/overlay-big-pop-in";
        protected override string PopOutSampleName => @"UI/overlay-big-pop-out";
        public override bool BlockScreenWideMouse => true;

        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        private BriefingGlass mainPanel;
        private FillFlowContainer earnersFlow;
        private FillFlowContainer awardsFlow;
        private OsuSpriteText statusText;

        public PointsActivityAdminOverlay()
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
                    Size = new Vector2(0.6f, 0.8f),
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
                                                    sectionHeader("Top earners (last 7 days)"),
                                                    earnersFlow = column(),
                                                    sectionHeader("Recent large awards"),
                                                    awardsFlow = column(),
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
                new SpriteIcon
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Icon = FontAwesome.Solid.ChartLine,
                    Size = new Vector2(24),
                    Colour = BriefingTheme.AccentSky,
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
                            Text = "Points activity",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
                        },
                        statusText = new OsuSpriteText
                        {
                            Text = "Loading...",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                            Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                        },
                    },
                },
            },
        };

        private Drawable sectionHeader(string text) => new OsuSpriteText
        {
            Text = text,
            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
            Colour = BriefingTheme.AccentSky,
            Margin = new MarginPadding { Top = BriefingTheme.SpacingSm },
        };

        private FillFlowContainer column() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 4),
        };

        private Drawable row(string left, string right, Color4 rightColour) => new Container
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
                    Margin = new MarginPadding { Horizontal = 14, Vertical = 9 },
                    ColumnDimensions = new[] { new Dimension(), new Dimension(GridSizeMode.AutoSize) },
                    RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                    Content = new[]
                    {
                        new[]
                        {
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = left,
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeBody),
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                Text = right,
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                                Colour = rightColour,
                            },
                        },
                    },
                },
            },
        };

        private void refresh()
        {
            if (api?.IsLoggedIn != true)
                return;

            statusText.Text = "Loading...";
            earnersFlow.Clear();
            awardsFlow.Clear();

            var req = new GetPointsActivityRequest();
            req.Success += res => Schedule(() => populate(res));
            req.Failure += _ => Schedule(() =>
            {
                if (statusText != null)
                    statusText.Text = "Could not load activity (admin only).";
            });
            api.Queue(req);
        }

        private void populate(APIPointsActivity res)
        {
            statusText.Text = $"Earning over the last {res.Days} days.";

            earnersFlow.Clear();
            int rank = 1;
            foreach (var e in res.TopEarners ?? System.Array.Empty<APIPointEarner>())
            {
                earnersFlow.Add(row($"#{rank}  {e.Username}", $"+{e.Earned:N0}  (bal {e.Balance:N0})", BriefingTheme.AccentGain));
                rank++;
            }
            if (!earnersFlow.Any())
                earnersFlow.Add(row("No earning yet in this window.", "", Color4.White));

            awardsFlow.Clear();
            foreach (var a in res.RecentLarge ?? System.Array.Empty<APIPointAward>())
            {
                awardsFlow.Add(row($"{a.Username}  ·  {prettyReason(a.Reason)}", $"+{a.Amount:N0}", BriefingTheme.AccentAmber));
            }
            if (!awardsFlow.Any())
                awardsFlow.Add(row("No large awards in this window.", "", Color4.White));
        }

        private static string prettyReason(string reason) =>
            string.IsNullOrEmpty(reason) ? "?" : reason.Replace("_", " ");

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
