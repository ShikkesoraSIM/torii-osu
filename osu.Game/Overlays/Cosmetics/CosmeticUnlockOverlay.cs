// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

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
    /// The celebration shown when a player unlocks a cosmetic (e.g. by redeeming
    /// an access code): "You unlocked X" with a live preview, an Equip button and
    /// a shortcut to the inventory. Call <see cref="Display"/> with the catalog id.
    /// </summary>
    public partial class CosmeticUnlockOverlay : OsuFocusedOverlayContainer
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
        private Container previewHost;
        private OsuSpriteText kindText;
        private OsuSpriteText nameText;
        private RoundedButton equipButton;

        private string currentId;

        public CosmeticUnlockOverlay()
        {
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;
        }

        /// <summary>Show the celebration for a freshly-unlocked cosmetic id.</summary>
        public void Display(string id)
        {
            currentId = id;

            var user = api?.LocalUser.Value;
            nameText.Text = CosmeticUnlock.DisplayName(id, user);
            kindText.Text = CosmeticUnlock.KindLabel(id, user).ToUpperInvariant() + " UNLOCKED";

            previewHost.Clear();
            previewHost.Add(CosmeticUnlock.CreatePreview(id, user));

            equipButton.Enabled.Value = true;
            equipButton.Text = "Equip";

            Show();
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0.6f), Color4.Black.Opacity(0.78f)),
                },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.46f, 0.66f),
                    Children = new Drawable[]
                    {
                        mainPanel = new BriefingGlass
                        {
                            RelativeSizeAxes = Axes.Both,
                            RelativeContentSize = Axes.Both,
                            CornerSize = BriefingTheme.CornerLg,
                            ShadowOpacity = 0.45f,
                            ShadowRadius = 34,
                            Child = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, BriefingTheme.SpacingMd),
                                Padding = new MarginPadding(BriefingTheme.SpacingLg),
                                Children = new Drawable[]
                                {
                                    new FillFlowContainer
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        AutoSizeAxes = Axes.Both,
                                        Direction = FillDirection.Horizontal,
                                        Spacing = new Vector2(8, 0),
                                        Children = new Drawable[]
                                        {
                                            new SpriteIcon
                                            {
                                                Anchor = Anchor.Centre,
                                                Origin = Anchor.Centre,
                                                Icon = FontAwesome.Solid.Gift,
                                                Size = new Vector2(16),
                                                Colour = BriefingTheme.AccentPink,
                                            },
                                            kindText = new OsuSpriteText
                                            {
                                                Anchor = Anchor.Centre,
                                                Origin = Anchor.Centre,
                                                Text = "UNLOCKED",
                                                Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                                                Colour = BriefingTheme.AccentPink,
                                            },
                                        },
                                    },
                                    nameText = new OsuSpriteText
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        Text = "Cosmetic",
                                        Font = OsuFont.GetFont(size: BriefingTheme.TypeDisplay, weight: FontWeight.SemiBold),
                                    },
                                    new Container
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        RelativeSizeAxes = Axes.X,
                                        Height = 150,
                                        Masking = true,
                                        CornerRadius = BriefingTheme.CornerSm,
                                        Children = new Drawable[]
                                        {
                                            new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(14, 14, 22, 255) },
                                            previewHost = new Container
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                Masking = true,
                                                CornerRadius = BriefingTheme.CornerSm,
                                            },
                                        },
                                    },
                                    equipButton = new RoundedButton
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        RelativeSizeAxes = Axes.X,
                                        Height = 44,
                                        Text = "Equip",
                                        BackgroundColour = BriefingTheme.AccentPink,
                                        Action = equip,
                                    },
                                    new RoundedButton
                                    {
                                        Anchor = Anchor.TopCentre,
                                        Origin = Anchor.TopCentre,
                                        RelativeSizeAxes = Axes.X,
                                        Height = 40,
                                        Text = "Go to inventory",
                                        BackgroundColour = BriefingTheme.AccentSky,
                                        Action = goToInventory,
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

        private void equip()
        {
            var user = api?.LocalUser.Value;

            switch (CosmeticUnlock.ResolveKind(currentId, user))
            {
                case CosmeticUnlock.Kind.Trail:
                    cosmetics?.Equip(currentId);
                    break;

                case CosmeticUnlock.Kind.NameColour:
                    cosmetics?.EquipNameColour(currentId);
                    break;

                case CosmeticUnlock.Kind.Aura:
                    equipAura(currentId);
                    break;
            }

            equipButton.Enabled.Value = false;
            equipButton.Text = "Equipped ✓";
        }

        private void equipAura(string id)
        {
            if (api?.LocalUser.Value == null)
                return;

            api.LocalUser.Value.EquippedAura = id;
            UserAuraEvents.NotifyUserAuraChanged(api.LocalUser.Value.Id, id);
            api.Queue(new UpdateEquippedAuraRequest(id));
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
            // A celebratory pop with a little overshoot.
            mainPanel.ScaleTo(0.85f).ScaleTo(1f, 520, Easing.OutBack)
                     .MoveToY(24).MoveToY(0, 520, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            this.FadeOut(BriefingTheme.DismissDuration, Easing.OutQuint);
            mainPanel.ScaleTo(0.95f, BriefingTheme.DismissDuration, Easing.OutQuint);
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
