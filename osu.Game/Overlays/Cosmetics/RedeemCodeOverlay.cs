// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
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
    /// Player-facing "redeem a code" prompt. Sends the code to the server, then
    /// awards points and unlocks any granted cosmetics — popping the unlock
    /// celebration for the first one (the rest land in the inventory).
    /// </summary>
    public partial class RedeemCodeOverlay : OsuFocusedOverlayContainer
    {
        protected override string PopInSampleName => @"UI/overlay-pop-in";
        protected override string PopOutSampleName => @"UI/overlay-pop-out";
        public override bool BlockScreenWideMouse => true;

        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        [Resolved(canBeNull: true)]
        private ToriiCosmeticsManager cosmetics { get; set; }

        [Resolved(canBeNull: true)]
        private CosmeticUnlockOverlay unlockPopup { get; set; }

        private BriefingGlass mainPanel;
        private OsuTextBox codeBox;
        private RoundedButton redeemButton;
        private OsuSpriteText resultText;

        public RedeemCodeOverlay()
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
                    Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0.55f), Color4.Black.Opacity(0.7f)),
                },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.42f, 0.44f),
                    Children = new Drawable[]
                    {
                        mainPanel = new BriefingGlass
                        {
                            RelativeSizeAxes = Axes.Both,
                            RelativeContentSize = Axes.Both,
                            CornerSize = BriefingTheme.CornerLg,
                            ShadowOpacity = 0.4f,
                            ShadowRadius = 28,
                            Child = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, BriefingTheme.SpacingMd),
                                Padding = new MarginPadding(BriefingTheme.SpacingLg),
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Text = "Redeem a code",
                                        Font = OsuFont.GetFont(size: BriefingTheme.TypeDisplay, weight: FontWeight.SemiBold),
                                    },
                                    new OsuSpriteText
                                    {
                                        Text = "Enter a Torii code to claim points or a cosmetic.",
                                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                                        Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                                    },
                                    codeBox = new OsuTextBox
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Height = 42,
                                        PlaceholderText = "TORII-XXXXXX",
                                    },
                                    redeemButton = new RoundedButton
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Height = 44,
                                        Text = "Redeem",
                                        BackgroundColour = BriefingTheme.AccentPink,
                                        Action = redeem,
                                    },
                                    resultText = new OsuSpriteText
                                    {
                                        Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                                        Alpha = 0,
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

            codeBox.OnCommit += (_, _) => redeem();
        }

        protected override void PopIn()
        {
            this.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            mainPanel.ScaleTo(0.94f).ScaleTo(1f, BriefingTheme.EntranceDuration, Easing.OutBack);
            resultText.FadeOut();
            codeBox.Text = string.Empty;
        }

        protected override void PopOut()
        {
            this.FadeOut(BriefingTheme.DismissDuration, Easing.OutQuint);
            mainPanel.ScaleTo(0.97f, BriefingTheme.DismissDuration, Easing.OutQuint);
        }

        private void redeem()
        {
            string code = codeBox.Text?.Trim();
            if (string.IsNullOrEmpty(code))
                return;

            if (api?.IsLoggedIn != true)
            {
                setResult("Log in to redeem a code.", BriefingTheme.AccentLoss);
                return;
            }

            redeemButton.Enabled.Value = false;
            redeemButton.Text = "Redeeming…";

            var req = new RedeemCodeRequest(code);
            req.Success += res => Schedule(() =>
            {
                resetButton();
                onRedeemed(res);
            });
            req.Failure += e => Schedule(() =>
            {
                resetButton();
                setResult(e?.Message ?? "Couldn't redeem that code.", BriefingTheme.AccentLoss);
            });
            api.Queue(req);
        }

        private void onRedeemed(APIRedeemResult res)
        {
            if (cosmetics != null)
            {
                cosmetics.PointsBalance.Value = res.Balance;
                foreach (string id in res.GrantedCosmetics ?? Array.Empty<string>())
                    cosmetics.Grant(id);
            }

            string[] granted = res.GrantedCosmetics ?? Array.Empty<string>();
            if (granted.Length > 0 && unlockPopup != null)
            {
                // Celebrate the first unlock; any extras are already in the
                // inventory.
                Hide();
                unlockPopup.Display(granted[0]);
                return;
            }

            setResult(res.Awarded > 0 ? $"Redeemed {res.Awarded:N0} points!" : "Code redeemed!", BriefingTheme.AccentGain);
        }

        private void resetButton()
        {
            redeemButton.Enabled.Value = true;
            redeemButton.Text = "Redeem";
        }

        private void setResult(string message, Color4 colour)
        {
            resultText.Text = message;
            resultText.Colour = colour;
            resultText.FadeIn(120);
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

        // Round corner "X" close button (mirrors the other Torii overlays).
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
