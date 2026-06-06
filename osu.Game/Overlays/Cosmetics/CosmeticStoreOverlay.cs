// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
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
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Input.Bindings;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>
    /// The Torii cosmetic store: a Fortnite-style shop for cursor-trail
    /// cosmetics. A daily-rotating Store tab to buy with points, an Inventory
    /// tab for fast equipping, and a detail panel with a live preview, price /
    /// buy / equip, and (once unlocked) length + density sliders.
    /// Styled with the Torii BriefingGlass material.
    /// </summary>
    public partial class CosmeticStoreOverlay : OsuFocusedOverlayContainer, INamedOverlayComponent
    {
        public IconUsage Icon => FontAwesome.Solid.Store;
        public LocalisableString Title => "cosmetic store";
        public LocalisableString Description => "buy and equip cursor trails";

        protected override string PopInSampleName => @"UI/overlay-big-pop-in";
        protected override string PopOutSampleName => @"UI/overlay-big-pop-out";
        public override bool BlockScreenWideMouse => true;

        [Resolved(canBeNull: true)]
        private ToriiCosmeticsManager cosmetics { get; set; }

        private BriefingGlass mainPanel;
        private OsuTabControl<StoreTab> tabs;
        private OsuScrollContainer cardScroll;
        private FillFlowContainer cardFlow;
        private Container detailContainer;
        private OsuSpriteText pointsText;
        private OsuSpriteText rotationText;

        private CosmeticTrailDefinition selected;
        private StoreItemCard selectedCard;
        private readonly List<StoreItemCard> cards = new List<StoreItemCard>();

        public CosmeticStoreOverlay()
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
                    Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0.62f), Color4.Black.Opacity(0.74f)),
                },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.9f, 0.86f),
                    Child = mainPanel = new BriefingGlass
                    {
                        RelativeSizeAxes = Axes.Both,
                        RelativeContentSize = Axes.Both,
                        CornerSize = BriefingTheme.CornerLg,
                        SpecularStrength = 0.18f,
                        SpecularHeight = 80f,
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
                                new Drawable[] { createBody() },
                            },
                        },
                    },
                },
            };
        }

        private Drawable createHeader()
        {
            return new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, BriefingTheme.SpacingSm),
                Children = new Drawable[]
                {
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        ColumnDimensions = new[] { new Dimension(), new Dimension(GridSizeMode.AutoSize) },
                        RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                        Content = new[]
                        {
                            new[]
                            {
                                new OsuSpriteText
                                {
                                    Text = "Cosmetic Store",
                                    Font = OsuFont.GetFont(size: BriefingTheme.TypeDisplay, weight: FontWeight.SemiBold),
                                },
                                (Drawable)new FillFlowContainer
                                {
                                    Anchor = Anchor.CentreRight,
                                    Origin = Anchor.CentreRight,
                                    AutoSizeAxes = Axes.Both,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(6, 0),
                                    Children = new Drawable[]
                                    {
                                        new SpriteIcon
                                        {
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre,
                                            Icon = FontAwesome.Solid.Coins,
                                            Size = new Vector2(18),
                                            Colour = BriefingTheme.AccentAmber,
                                        },
                                        pointsText = new OsuSpriteText
                                        {
                                            Anchor = Anchor.Centre,
                                            Origin = Anchor.Centre,
                                            Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
                                            Colour = BriefingTheme.AccentAmber,
                                        },
                                    },
                                },
                            },
                        },
                    },
                    tabs = new OsuTabControl<StoreTab>
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 30,
                        AccentColour = BriefingTheme.AccentPink,
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Children = new Drawable[]
                        {
                            rotationText = new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                                Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                            },
                            createPotatoToggle(),
                        },
                    },
                },
            };
        }

        /// <summary>A small toggle pill for "Potato PC" mode (frozen-snapshot
        /// previews). Bound to the shared setting so previews react instantly.</summary>
        private Drawable createPotatoToggle()
        {
            if (cosmetics == null)
                return Empty();

            Box bg;
            SpriteIcon icon;
            OsuSpriteText label;

            var toggle = new OsuClickableContainer
            {
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 6f,
                Action = () => cosmetics.StorePotatoMode.Value = !cosmetics.StorePotatoMode.Value,
                Children = new Drawable[]
                {
                    bg = new Box { RelativeSizeAxes = Axes.Both },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(6, 0),
                        Padding = new MarginPadding { Horizontal = 10, Vertical = 5 },
                        Children = new Drawable[]
                        {
                            icon = new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = FontAwesome.Solid.Bolt,
                                Size = new Vector2(11),
                            },
                            label = new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = "Potato PC mode",
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                            },
                        },
                    },
                },
            };

            cosmetics.StorePotatoMode.BindValueChanged(v =>
            {
                bool on = v.NewValue;
                bg.Colour = on ? BriefingTheme.AccentGain : Color4.White.Opacity(0.10f);
                Color4 fg = on ? Color4.Black.Opacity(0.85f) : Color4.White.Opacity(BriefingTheme.InkSecondary);
                icon.Colour = fg;
                label.Colour = fg;
            }, true);

            return toggle;
        }

        private Drawable createBody()
        {
            return new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                ColumnDimensions = new[]
                {
                    new Dimension(),
                    new Dimension(GridSizeMode.Absolute, BriefingTheme.SpacingMd),
                    new Dimension(GridSizeMode.Absolute, 320),
                },
                Content = new[]
                {
                    new[]
                    {
                        // Rounded mask so partially-scrolled cards clip to the
                        // panel's shape instead of poking over its border, with
                        // a little bottom clearance so the last row isn't flush.
                        (Drawable)new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = BriefingTheme.CornerMd,
                            Child = cardScroll = new OsuScrollContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                ScrollbarVisible = false,
                                Child = cardFlow = new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Full,
                                    Spacing = new Vector2(BriefingTheme.SpacingSm),
                                    Padding = new MarginPadding { Top = 2, Bottom = BriefingTheme.SpacingMd },
                                },
                            },
                        },
                        Empty(),
                        new BriefingGlass
                        {
                            RelativeSizeAxes = Axes.Both,
                            RelativeContentSize = Axes.Both,
                            CornerSize = BriefingTheme.CornerMd,
                            SurfaceLift = 1.3f,
                            Child = detailContainer = new Container { RelativeSizeAxes = Axes.Both },
                        },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (cosmetics != null)
            {
                cosmetics.PointsBalance.BindValueChanged(v =>
                {
                    pointsText.Text = $"{v.NewValue:N0}";
                    pointsText.ScaleTo(1.3f).ScaleTo(1f, 450, Easing.OutBack);
                }, true);
                // Buy / equip only flip badges; refresh them in place instead of
                // rebuilding the whole grid (35 trail previews) which lagged hard.
                cosmetics.EquippedTrailId.BindValueChanged(_ => refreshCards());
                cosmetics.InventoryChanged += onInventoryChanged;

                int hours = (int)(cosmetics.SecondsUntilRotation() / 3600);
                rotationText.Text = $"Featured rotates in ~{hours}h  (placeholder)";
            }

            tabs.Current.BindValueChanged(_ => rebuildCards(), true);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (cosmetics != null)
                cosmetics.InventoryChanged -= onInventoryChanged;
            base.Dispose(isDisposing);
        }

        // Buying only flips an OWNED badge (the whole catalog is always shown in
        // Store, and you can't buy from Inventory), so just refresh badges. A
        // genuine add/remove only happens on a tab switch, which still rebuilds.
        private void onInventoryChanged() => Schedule(refreshCards);

        private void refreshCards()
        {
            foreach (var card in cards)
                card.RefreshState();
        }

        private void rebuildCards()
        {
            if (cardFlow == null)
                return;

            cardFlow.Clear();
            cards.Clear();
            selectedCard = null;

            bool inventory = tabs.Current.Value == StoreTab.Inventory;

            HashSet<string> featured = inventory
                ? new HashSet<string>()
                : (cosmetics?.GetDailyStore() ?? new List<CosmeticTrailDefinition>()).Select(d => d.Id).ToHashSet();

            IEnumerable<CosmeticTrailDefinition> trails = inventory
                ? CosmeticCatalog.Trails.Where(t => cosmetics?.IsOwned(t.Id) ?? false)
                : CosmeticCatalog.Trails;

            // Category groups. Only a category that actually has items gets a
            // header (no empty "Name Colours:" rows). New cosmetic kinds (name
            // colours, user auras, ...) slot in here as additional groups.
            var groups = new (string title, IconUsage icon, List<CosmeticTrailDefinition> items)[]
            {
                ("Cursor Trails", FontAwesome.Solid.PaintBrush, trails.ToList()),
            };

            bool anyContent = false;

            foreach (var (title, icon, list) in groups)
            {
                if (list.Count == 0)
                    continue;

                anyContent = true;
                cardFlow.Add(categoryHeader(title, icon));

                foreach (var def in list)
                {
                    bool isSelected = selected != null && def.Id == selected.Id;
                    var card = new StoreItemCard(def, cosmetics, featured.Contains(def.Id), isSelected, cardScroll);
                    card.Action = () => onCardClicked(def, card);
                    if (isSelected)
                        selectedCard = card;
                    cards.Add(card);
                    cardFlow.Add(card);
                }
            }

            if (!anyContent)
            {
                cardFlow.Add(new OsuSpriteText
                {
                    Text = "Nothing owned yet. Buy a trail in the Store tab!",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeBody),
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                });
            }
        }

        /// <summary>A full-width section header that forces a new row in the
        /// card flow, so cards group visually under their category.</summary>
        private Drawable categoryHeader(string title, IconUsage icon) => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(8, 0),
            Margin = new MarginPadding { Top = 2, Bottom = 2, Left = 2 },
            Children = new Drawable[]
            {
                new SpriteIcon
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Icon = icon,
                    Size = new Vector2(14),
                    Colour = BriefingTheme.AccentPink,
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = title,
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
                },
            },
        };

        private void onCardClicked(CosmeticTrailDefinition def, StoreItemCard card)
        {
            // Inventory: a single click equips (fast skin-swap-style flow).
            if (tabs.Current.Value == StoreTab.Inventory && (cosmetics?.IsOwned(def.Id) ?? false))
            {
                cosmetics.Equip(def.Id);
                showToast($"Equipped {def.Name}");
            }

            selectItem(def, card);
        }

        private void selectItem(CosmeticTrailDefinition def, StoreItemCard card)
        {
            selected = def;

            if (selectedCard != null && selectedCard != card)
                selectedCard.SetSelected(false);
            selectedCard = card;
            card?.SetSelected(true);

            detailContainer.Clear();
            detailContainer.Add(new CosmeticDetailPanel(def, cosmetics, showToast));
        }

        private void showToast(string message)
        {
            var toast = new Container
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Y = 80,
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = BriefingTheme.CornerSm,
                EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                {
                    Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                    Colour = Color4.Black.Opacity(0.4f),
                    Radius = 14,
                },
                Alpha = 0,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(18, 20, 32, 240) },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Padding = new MarginPadding { Horizontal = 18, Vertical = 11 },
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Icon = FontAwesome.Solid.Check,
                                Size = new Vector2(15),
                                Colour = BriefingTheme.AccentGain,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Text = message,
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                            },
                        },
                    },
                },
            };

            AddInternal(toast);

            toast.FadeInFromZero(150, Easing.OutQuint);
            toast.ScaleTo(0.85f).ScaleTo(1f, 380, Easing.OutBack);
            toast.Delay(1550).FadeOut(350, Easing.OutQuint).Expire();
        }

        protected override void PopIn()
        {
            this.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            mainPanel.ScaleTo(0.94f).ScaleTo(1f, BriefingTheme.EntranceDuration, Easing.OutBack)
                     .MoveToY(20).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
            rebuildCards();
        }

        protected override void PopOut()
        {
            this.FadeOut(BriefingTheme.DismissDuration, Easing.OutQuint);
            mainPanel.ScaleTo(0.97f, BriefingTheme.DismissDuration, Easing.OutQuint);
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

        public enum StoreTab
        {
            Store,
            Inventory,
        }
    }
}
