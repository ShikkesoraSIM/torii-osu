// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
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
    /// The Torii gift reveal: a wrapped present that wiggles, waiting to be
    /// opened, then bursts into the reward (cosmetic preview + equip / inventory,
    /// or points). Shown after a play so it never nags on login. Display only —
    /// the claim flow (or admin preview) applies the actual grant beforehand.
    /// </summary>
    public partial class ToriiGiftOverlay : OsuFocusedOverlayContainer
    {
        protected override string PopInSampleName => @"UI/overlay-big-pop-in";
        protected override string PopOutSampleName => @"UI/overlay-big-pop-out";
        public override bool BlockScreenWideMouse => true;

        [Resolved(canBeNull: true)]
        private ToriiCosmeticsManager cosmetics { get; set; }

        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        [Resolved(canBeNull: true)]
        private CosmeticStoreOverlay store { get; set; }

        private BriefingGlass mainPanel;
        private Container closedView;
        private Container revealView;
        private OsuClickableContainer giftBox;
        private SpriteIcon giftIcon;
        private Container giftGlow;
        private TextFlowContainer fromText;

        private string sender = "Torii Halo";
        private string message;
        private int points;
        private string[] giftCosmetics = System.Array.Empty<string>();

        public ToriiGiftOverlay()
        {
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;
        }

        /// <summary>Show a gift. The grant should already be applied (by the claim
        /// flow); this just celebrates it. Pass cosmetics and/or points.</summary>
        public void Display(string sender, string message, int points, string[] cosmetics)
        {
            this.sender = string.IsNullOrWhiteSpace(sender) ? "Torii Halo" : sender;
            this.message = message;
            this.points = points;
            giftCosmetics = cosmetics ?? System.Array.Empty<string>();

            fromText.Text = $"You received a gift from {this.sender}!";

            revealView.Clear();
            revealView.Alpha = 0;
            closedView.Alpha = 1;
            closedView.ScaleTo(1);

            Show();
            startIdleAnimation();
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0.62f), Color4.Black.Opacity(0.8f)),
                },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.46f, 0.68f),
                    Children = new Drawable[]
                    {
                        mainPanel = new BriefingGlass
                        {
                            RelativeSizeAxes = Axes.Both,
                            RelativeContentSize = Axes.Both,
                            CornerSize = BriefingTheme.CornerLg,
                            ShadowOpacity = 0.45f,
                            ShadowRadius = 34,
                            Child = new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding(BriefingTheme.SpacingLg),
                                Children = new Drawable[]
                                {
                                    closedView = createClosedView(),
                                    revealView = new Container
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Alpha = 0,
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

        private Container createClosedView() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Child = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, BriefingTheme.SpacingMd),
                Children = new Drawable[]
                {
                    fromText = new TextFlowContainer(t => t.Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold))
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        TextAnchor = Anchor.TopCentre,
                        Text = "You received a gift from Torii Halo!",
                    },
                    giftBox = new OsuClickableContainer
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Size = new Vector2(220),
                        Action = open,
                        Children = new Drawable[]
                        {
                            // Soft round halo (a circular glow), not a square box.
                            giftGlow = new CircularContainer
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Size = new Vector2(100),
                                Masking = true,
                                EdgeEffect = new EdgeEffectParameters
                                {
                                    Type = EdgeEffectType.Glow,
                                    Colour = BriefingTheme.AccentPink.Opacity(0.55f),
                                    Radius = 85,
                                },
                                Child = new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = BriefingTheme.AccentPink.Opacity(0.22f),
                                },
                            },
                            giftIcon = new SpriteIcon
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Icon = FontAwesome.Solid.Gift,
                                Size = new Vector2(132),
                                Colour = BriefingTheme.AccentPink,
                            },
                        },
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Text = "tap the gift to open it",
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, italics: true),
                        Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                    },
                    new TextFlowContainer(t => t.Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption))
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        TextAnchor = Anchor.TopCentre,
                        Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                        Text = "we waited until after a map so we didn't pester you the second you logged in  :)",
                    },
                },
            },
        };

        private void startIdleAnimation()
        {
            // A present that can't sit still — gentle wiggle + breathing glow.
            giftIcon.ClearTransforms();
            giftIcon.Alpha = 1;
            giftIcon.RotateTo(-6)
                    .Then().RotateTo(6, 480, Easing.InOutSine)
                    .Then().RotateTo(-6, 480, Easing.InOutSine)
                    .Loop();
            giftIcon.ScaleTo(1f)
                    .Then().ScaleTo(1.06f, 680, Easing.InOutSine)
                    .Then().ScaleTo(1f, 680, Easing.InOutSine)
                    .Loop();

            giftGlow.ClearTransforms();
            giftGlow.Alpha = 1;
            giftGlow.ScaleTo(1f)
                    .Then().ScaleTo(1.15f, 680, Easing.InOutSine)
                    .Then().ScaleTo(1f, 680, Easing.InOutSine)
                    .Loop();
        }

        private void open()
        {
            giftIcon.ClearTransforms();
            giftGlow.ClearTransforms();

            // Burst: the present pops, spins and shrinks away.
            giftIcon.ScaleTo(1.35f, 180, Easing.OutBack).Then().ScaleTo(0.15f, 240, Easing.InQuad);
            giftIcon.RotateTo(35, 420, Easing.OutQuad);
            giftIcon.Delay(180).FadeOut(260, Easing.OutQuad);
            giftGlow.ScaleTo(1.5f, 280, Easing.OutQuad);
            giftGlow.FadeOut(300, Easing.OutQuad);

            closedView.Delay(220).FadeOut(160, Easing.OutQuint);

            buildReveal();
            revealView.ScaleTo(0.92f).Delay(360).ScaleTo(1f, 360, Easing.OutBack);
            revealView.Delay(360).FadeIn(260, Easing.OutQuint);
        }

        private void buildReveal()
        {
            var user = api?.LocalUser.Value;
            string primary = giftCosmetics.FirstOrDefault();

            var flow = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, BriefingTheme.SpacingMd),
            };

            if (primary != null)
            {
                flow.Add(new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Text = CosmeticUnlock.KindLabel(primary, user).ToUpperInvariant() + " UNLOCKED",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                    Colour = BriefingTheme.AccentPink,
                });
                flow.Add(new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Text = CosmeticUnlock.DisplayName(primary, user),
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeDisplay, weight: FontWeight.SemiBold),
                });
                flow.Add(new Container
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    RelativeSizeAxes = Axes.X,
                    Height = 140,
                    Masking = true,
                    CornerRadius = BriefingTheme.CornerSm,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(14, 14, 22, 255) },
                        new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = BriefingTheme.CornerSm,
                            Child = CosmeticUnlock.CreatePreview(primary, user),
                        },
                    },
                });

                if (points > 0 || giftCosmetics.Length > 1)
                {
                    string extra = points > 0 ? $"plus {points:N0} points" : string.Empty;
                    if (giftCosmetics.Length > 1)
                        extra = (extra.Length > 0 ? extra + " · " : string.Empty) + $"and {giftCosmetics.Length - 1} more in your inventory";
                    flow.Add(new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Text = extra,
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                        Colour = BriefingTheme.AccentAmber,
                    });
                }

                flow.Add(new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 44,
                    Text = "Equip",
                    BackgroundColour = BriefingTheme.AccentPink,
                    Action = () => equip(primary),
                });
                flow.Add(new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 40,
                    Text = "Go to inventory",
                    BackgroundColour = BriefingTheme.AccentSky,
                    Action = goToInventory,
                });
            }
            else
            {
                // Points-only gift.
                flow.Add(new SpriteIcon
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Icon = FontAwesome.Solid.Coins,
                    Size = new Vector2(48),
                    Colour = BriefingTheme.AccentAmber,
                });
                flow.Add(new OsuSpriteText
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Text = $"{points:N0} points!",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeDisplay, weight: FontWeight.SemiBold),
                });
                flow.Add(new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 44,
                    Text = "Nice!",
                    BackgroundColour = BriefingTheme.AccentPink,
                    Action = Hide,
                });
            }

            if (!string.IsNullOrWhiteSpace(message))
            {
                flow.Add(new TextFlowContainer(t => t.Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, italics: true))
                {
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    TextAnchor = Anchor.TopCentre,
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                    Text = $"“{message}”",
                });
            }

            revealView.Add(flow);
        }

        private void equip(string id)
        {
            var user = api?.LocalUser.Value;

            switch (CosmeticUnlock.ResolveKind(id, user))
            {
                case CosmeticUnlock.Kind.Trail:
                    cosmetics?.Equip(id);
                    break;

                case CosmeticUnlock.Kind.NameColour:
                    cosmetics?.EquipNameColour(id);
                    break;

                case CosmeticUnlock.Kind.Aura:
                    if (api?.LocalUser.Value != null)
                    {
                        api.LocalUser.Value.EquippedAura = id;
                        UserAuraEvents.NotifyUserAuraChanged(api.LocalUser.Value.Id, id);
                        api.Queue(new UpdateEquippedAuraRequest(id));
                    }
                    break;
            }
        }

        private void goToInventory()
        {
            Hide();
            store?.OpenInventory();
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
            mainPanel.ScaleTo(0.85f).ScaleTo(1f, 520, Easing.OutBack)
                     .MoveToY(24).MoveToY(0, 520, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.FadeOut(BriefingTheme.DismissDuration, Easing.OutQuint);
            mainPanel.ScaleTo(0.95f, BriefingTheme.DismissDuration, Easing.OutQuint);
        }
    }
}
