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
using osu.Framework.Platform;
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
    /// Staff tool: mint a redeemable points access code. The server auto-makes
    /// the code string and re-validates admin. Admin-gated by the hub (and a
    /// PopIn double-check).
    /// </summary>
    public partial class AccessCodeAdminOverlay : OsuFocusedOverlayContainer
    {
        protected override string PopInSampleName => @"UI/overlay-big-pop-in";
        protected override string PopOutSampleName => @"UI/overlay-big-pop-out";
        public override bool BlockScreenWideMouse => true;

        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        [Resolved(canBeNull: true)]
        private Clipboard clipboard { get; set; }

        private BriefingGlass mainPanel;
        private OsuNumberBox amountBox;
        private OsuNumberBox usesBox;
        private OsuTextBox noteBox;
        private RoundedButton generateButton;
        private OsuSpriteText errorText;
        private Container resultArea;

        public AccessCodeAdminOverlay()
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
                    Size = new Vector2(0.5f, 0.72f),
                    Children = new Drawable[]
                    {
                        mainPanel = new BriefingGlass
                        {
                            RelativeSizeAxes = Axes.Both,
                            RelativeContentSize = Axes.Both,
                            CornerSize = BriefingTheme.CornerLg,
                            ShadowOpacity = 0.4f,
                            ShadowRadius = 30,
                            Child = new OsuScrollContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                ScrollbarVisible = false,
                                Child = new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, BriefingTheme.SpacingMd),
                                    Padding = new MarginPadding(BriefingTheme.SpacingLg),
                                    Children = new Drawable[]
                                    {
                                        new OsuSpriteText
                                        {
                                            Text = "Access codes",
                                            Font = OsuFont.GetFont(size: BriefingTheme.TypeDisplay, weight: FontWeight.SemiBold),
                                        },
                                        new OsuSpriteText
                                        {
                                            Text = "Mint a code players redeem for points. Cosmetic-granting codes come next.",
                                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                                            Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                                        },
                                        field("Points", amountBox = new OsuNumberBox { RelativeSizeAxes = Axes.X, PlaceholderText = "e.g. 5000" }),
                                        field("Max uses", usesBox = new OsuNumberBox { RelativeSizeAxes = Axes.X, Text = "1" }),
                                        field("Note (optional)", noteBox = new OsuTextBox { RelativeSizeAxes = Axes.X, PlaceholderText = "why this code exists" }),
                                        generateButton = new RoundedButton
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            Height = 42,
                                            Text = "Generate code",
                                            BackgroundColour = BriefingTheme.AccentPink,
                                            Action = generate,
                                        },
                                        errorText = new OsuSpriteText
                                        {
                                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                                            Colour = BriefingTheme.AccentLoss,
                                            Alpha = 0,
                                        },
                                        resultArea = new Container
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
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

        private void generate()
        {
            errorText.FadeOut(80);

            if (api?.IsLoggedIn != true)
            {
                setError("Not logged in.");
                return;
            }

            if (!int.TryParse(amountBox.Current.Value, out int amount) || amount <= 0)
            {
                setError("Enter a points amount.");
                return;
            }

            if (!int.TryParse(usesBox.Current.Value, out int uses) || uses <= 0)
                uses = 1;

            generateButton.Enabled.Value = false;
            generateButton.Text = "Generating…";

            var req = new CreateAccessCodeRequest(amount, uses, noteBox.Current.Value);
            req.Success += code => Schedule(() =>
            {
                resetButton();
                showResult(code);
            });
            req.Failure += e => Schedule(() =>
            {
                resetButton();
                setError(e?.Message ?? "Failed to create code (are you an admin on this server?).");
            });
            api.Queue(req);
        }

        private void resetButton()
        {
            generateButton.Enabled.Value = true;
            generateButton.Text = "Generate code";
        }

        private void setError(string message)
        {
            errorText.Text = message;
            errorText.FadeIn(120);
        }

        private void showResult(APIAccessCode code)
        {
            resultArea.Child = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Masking = true,
                CornerRadius = BriefingTheme.CornerSm,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(16, 16, 24, 255) },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 8),
                        Padding = new MarginPadding(14),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = $"Code created — {code.Amount:N0} pts · {code.MaxUses} use(s)",
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                                Colour = BriefingTheme.AccentGain,
                            },
                            new OsuSpriteText
                            {
                                Text = code.Code,
                                Font = OsuFont.GetFont(size: 30, weight: FontWeight.Bold),
                            },
                            new RoundedButton
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 36,
                                Text = "Copy code",
                                BackgroundColour = BriefingTheme.AccentSky,
                                Action = () =>
                                {
                                    clipboard?.SetText(code.Code);
                                    setError(string.Empty);
                                    errorText.Text = "Copied to clipboard.";
                                    errorText.Colour = BriefingTheme.AccentGain;
                                    errorText.FadeIn(120);
                                },
                            },
                        },
                    },
                },
            };
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
