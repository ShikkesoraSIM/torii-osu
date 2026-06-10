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
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input.Bindings;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>
    /// Staff tool: compose and send a gift (points and/or cosmetics) to a player.
    /// Preview pops the real gift reveal so you see exactly what they'll get.
    /// </summary>
    public partial class GiftAdminOverlay : OsuFocusedOverlayContainer
    {
        protected override string PopInSampleName => @"UI/overlay-big-pop-in";
        protected override string PopOutSampleName => @"UI/overlay-big-pop-out";
        public override bool BlockScreenWideMouse => true;

        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        [Resolved(canBeNull: true)]
        private ToriiGiftOverlay giftOverlay { get; set; }

        private BriefingGlass mainPanel;
        private OsuTextBox recipientBox;
        private OsuNumberBox pointsBox;
        private OsuTextBox messageBox;
        private CosmeticGrantPicker grantPicker;
        private RoundedButton sendButton;
        private OsuSpriteText resultText;

        public GiftAdminOverlay()
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
                    Size = new Vector2(0.5f, 0.78f),
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
                                    new Dimension(),
                                    new Dimension(GridSizeMode.AutoSize),
                                },
                                Content = new[]
                                {
                                    new Drawable[]
                                    {
                                        new FillFlowContainer
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Vertical,
                                            Spacing = new Vector2(0, 2),
                                            Margin = new MarginPadding { Bottom = BriefingTheme.SpacingMd },
                                            Children = new Drawable[]
                                            {
                                                new OsuSpriteText
                                                {
                                                    Text = "Send a gift",
                                                    Font = OsuFont.GetFont(size: BriefingTheme.TypeDisplay, weight: FontWeight.SemiBold),
                                                },
                                                new OsuSpriteText
                                                {
                                                    Text = "Points and/or cosmetics, delivered after the player's next map.",
                                                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                                                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                                                },
                                            },
                                        },
                                    },
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
                                                    field("Recipient (username or id)", recipientBox = new OsuTextBox { RelativeSizeAxes = Axes.X, PlaceholderText = "who gets it" }),
                                                    field("Points", pointsBox = new OsuNumberBox { RelativeSizeAxes = Axes.X, Text = "0" }),
                                                    field("Message (optional)", messageBox = new OsuTextBox { RelativeSizeAxes = Axes.X, PlaceholderText = "a little note" }),
                                                    field("Cosmetics (optional)", grantPicker = new CosmeticGrantPicker()),
                                                },
                                            },
                                        },
                                    },
                                    new Drawable[]
                                    {
                                        new FillFlowContainer
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Vertical,
                                            Spacing = new Vector2(0, BriefingTheme.SpacingSm),
                                            Margin = new MarginPadding { Top = BriefingTheme.SpacingMd },
                                            Children = new Drawable[]
                                            {
                                                resultText = new OsuSpriteText
                                                {
                                                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                                                    Alpha = 0,
                                                },
                                                sendButton = new RoundedButton
                                                {
                                                    RelativeSizeAxes = Axes.X,
                                                    Height = 42,
                                                    Text = "Send gift",
                                                    BackgroundColour = BriefingTheme.AccentPink,
                                                    Action = send,
                                                },
                                                new RoundedButton
                                                {
                                                    RelativeSizeAxes = Axes.X,
                                                    Height = 38,
                                                    Text = "Preview gift",
                                                    BackgroundColour = BriefingTheme.AccentSky,
                                                    Action = preview,
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                        new ToriiCloseButton
                        {
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            Margin = new MarginPadding(14),
                            Action = Hide,
                        },
                    },
                },
            };
        }

        private Drawable field(string label, Drawable control) => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 4),
            Children = new[]
            {
                new OsuSpriteText
                {
                    Text = label,
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                },
                control,
            },
        };

        private void preview()
        {
            int.TryParse(pointsBox.Current.Value, out int pts);
            giftOverlay?.Display("Torii Halo", messageBox.Current.Value, Math.Max(0, pts), grantPicker?.Selected?.ToArray());
        }

        private void send()
        {
            setResult(string.Empty, Color4.White);

            string recipient = recipientBox.Current.Value?.Trim();
            if (string.IsNullOrEmpty(recipient))
            {
                setResult("Enter a recipient.", BriefingTheme.AccentLoss);
                return;
            }

            int.TryParse(pointsBox.Current.Value, out int pts);
            pts = Math.Max(0, pts);
            string[] grant = grantPicker?.Selected?.ToArray() ?? Array.Empty<string>();

            if (pts <= 0 && grant.Length == 0)
            {
                setResult("Add points or pick a cosmetic to gift.", BriefingTheme.AccentLoss);
                return;
            }

            if (api?.IsLoggedIn != true)
            {
                setResult("Not logged in.", BriefingTheme.AccentLoss);
                return;
            }

            sendButton.Enabled.Value = false;
            sendButton.Text = "Sending…";

            var req = new CreateGiftRequest(recipient, pts, grant, messageBox.Current.Value);
            req.Success += () => Schedule(() =>
            {
                resetButton();
                setResult($"Gift sent to {recipient}!", BriefingTheme.AccentGain);
            });
            req.Failure += e => Schedule(() =>
            {
                resetButton();
                setResult(e?.Message ?? "Couldn't send (the gift endpoint ships with the server).", BriefingTheme.AccentLoss);
            });
            api.Queue(req);
        }

        private void resetButton()
        {
            sendButton.Enabled.Value = true;
            sendButton.Text = "Send gift";
        }

        private void setResult(string message, Color4 colour)
        {
            resultText.Text = message;
            resultText.Colour = colour;
            resultText.FadeTo(string.IsNullOrEmpty(message) ? 0 : 1, 120);
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
            if (!ToriiAdminOverlay.IsAdmin(api?.LocalUser.Value))
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
    }
}
