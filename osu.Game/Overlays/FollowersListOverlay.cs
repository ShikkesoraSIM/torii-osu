// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.ToriiBriefing;
using osu.Game.Users.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays
{
    /// <summary>
    /// Torii-briefing-styled glass panel listing the local user's incoming
    /// followers (people who friended them). Opened from the small followers
    /// button on the local user's own profile header. Each follower row opens
    /// that user's profile on click.
    /// </summary>
    public partial class FollowersListOverlay : VisibilityContainer, IKeyBindingHandler<GlobalAction>
    {
        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved(canBeNull: true)]
        private UserProfileOverlay? profileOverlay { get; set; }

        private Container panel = null!;
        private FillFlowContainer listFlow = null!;
        private OsuSpriteText countText = null!;
        private Container placeholder = null!;
        private OsuSpriteText placeholderText = null!;

        private GetFollowersRequest? currentRequest;

        public FollowersListOverlay()
        {
            RelativeSizeAxes = Axes.Both;
        }

        // Only swallow input while actually visible — this overlay lives in the
        // always-present topmost layer, so blocking input when hidden would
        // freeze the whole game.
        public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => State.Value == Visibility.Visible;

        [BackgroundDependencyLoader]
        private void load()
        {
            FillFlowContainer content;

            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                    Alpha = 0.6f,
                },
                panel = new BriefingGlass
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Width = 540,
                    AutoSizeAxes = Axes.Y,
                    CornerSize = BriefingTheme.CornerLg,
                    SpecularStrength = 0.18f,
                    SpecularHeight = 70,
                    ShadowOpacity = 0.4f,
                    ShadowRadius = 30,
                    RelativeContentSize = Axes.X,
                    Child = content = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, BriefingTheme.SpacingMd),
                        Padding = new MarginPadding(BriefingTheme.SpacingXl),
                    },
                },
            };

            content.AddRange(new Drawable[]
            {
                new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(BriefingTheme.SpacingSm, 0),
                    Children = new Drawable[]
                    {
                        new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = FontAwesome.Solid.ToriiGate,
                            Size = new Vector2(BriefingTheme.TypeBody),
                            Colour = BriefingTheme.AccentPink,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = "TORII",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                            Colour = BriefingTheme.AccentPink,
                        },
                    },
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(BriefingTheme.SpacingSm, 0),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            Text = "Followers",
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
                        },
                        countText = new OsuSpriteText
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            Margin = new MarginPadding { Bottom = 3 },
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                            Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                        },
                    },
                },
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 420,
                    Children = new Drawable[]
                    {
                        new OsuScrollContainer
                        {
                            RelativeSizeAxes = Axes.Both,
                            ScrollbarVisible = false,
                            Child = listFlow = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, BriefingTheme.SpacingSm),
                            },
                        },
                        placeholder = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Child = placeholderText = new OsuSpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Text = "Loading...",
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeBody),
                                Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                            },
                        },
                    },
                },
            });
        }

        public void ShowFollowers()
        {
            Show();
            refresh();
        }

        private void refresh()
        {
            currentRequest?.Cancel();
            listFlow.Clear();
            placeholderText.Text = "Loading...";
            placeholder.Show();
            countText.Text = string.Empty;

            var req = currentRequest = new GetFollowersRequest();
            req.Success += relations => Schedule(() => populate(relations));
            req.Failure += _ => Schedule(() =>
            {
                placeholderText.Text = "Couldn't load followers.";
                placeholder.Show();
            });
            api.Queue(req);
        }

        private void populate(List<APIRelation> relations)
        {
            countText.Text = relations.Count == 1 ? "1 follower" : $"{relations.Count} followers";

            if (relations.Count == 0)
            {
                placeholderText.Text = "Nobody follows you yet.";
                placeholder.Show();
                return;
            }

            placeholder.Hide();

            foreach (var relation in relations)
            {
                if (relation.TargetUser == null)
                    continue;

                listFlow.Add(new FollowerRow(relation.TargetUser, relation.Mutual)
                {
                    ClickAction = user =>
                    {
                        Hide();
                        profileOverlay?.ShowUser(user);
                    },
                });
            }
        }

        protected override void PopIn()
        {
            this.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            panel.ScaleTo(0.94f).ScaleTo(1f, BriefingTheme.EntranceDuration, Easing.OutBack)
                 .MoveToY(20).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.FadeOut(BriefingTheme.DismissDuration, Easing.OutQuint);
            panel.ScaleTo(0.97f, BriefingTheme.DismissDuration, Easing.OutQuint);
        }

        protected override bool OnClick(ClickEvent e)
        {
            // Click on the scrim (outside the panel) closes the overlay.
            if (!panel.ReceivePositionalInputAt(e.ScreenSpaceMousePosition))
                Hide();

            return true;
        }

        public bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (e.Repeat)
                return false;

            if (e.Action == GlobalAction.Back && State.Value == Visibility.Visible)
            {
                Hide();
                return true;
            }

            return false;
        }

        public void OnReleased(KeyBindingReleaseEvent<GlobalAction> e)
        {
        }

        private partial class FollowerRow : OsuClickableContainer
        {
            public Action<APIUser>? ClickAction;

            private readonly APIUser user;
            private readonly bool mutual;

            private Box hover = null!;

            public FollowerRow(APIUser user, bool mutual)
            {
                this.user = user;
                this.mutual = mutual;

                RelativeSizeAxes = Axes.X;
                Height = 48;
                Masking = true;
                CornerRadius = BriefingTheme.CornerSm;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    hover = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White.Opacity(0.08f),
                        Alpha = 0,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.X,
                        RelativeSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(BriefingTheme.SpacingMd, 0),
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Margin = new MarginPadding { Left = BriefingTheme.SpacingSm },
                        Children = new Drawable[]
                        {
                            new UpdateableAvatar(user, false)
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(36),
                                CornerRadius = BriefingTheme.CornerSm,
                                Masking = true,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = user.Username,
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                            },
                        },
                    },
                };

                if (mutual)
                {
                    Add(new OsuSpriteText
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Margin = new MarginPadding { Right = BriefingTheme.SpacingMd },
                        Text = "mutual",
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                        Colour = BriefingTheme.AccentCyan,
                    });
                }

                Action = () => ClickAction?.Invoke(user);
            }

            protected override bool OnHover(HoverEvent e)
            {
                hover.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                hover.FadeOut(BriefingTheme.HoverDuration, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
