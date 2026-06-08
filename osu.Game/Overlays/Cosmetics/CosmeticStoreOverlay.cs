// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
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
using osu.Game.Graphics.UserEffects;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
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

        [Resolved(canBeNull: true)]
        private osu.Game.Online.API.IAPIProvider api { get; set; }

        // Cached server aura catalog (authoritative list of the user's entitled
        // auras + their display names — includes auras granted by something
        // other than a group, e.g. Founder by id, which the local group-based
        // resolver can't see). Null until the first fetch lands; rebuildCards
        // falls back to the local list meanwhile.
        private APIAuraCatalog auraCatalog;

        private Sample buySample;
        private Sample equipSample;
        private Sample unequipSample;

        private BriefingGlass mainPanel;
        private CosmeticAdminOverlay adminOverlay;
        private Drawable adminGear;
        private OsuTabControl<StoreTab> tabs;
        private OsuScrollContainer cardScroll;
        private FillFlowContainer cardFlow;
        private Container detailContainer;
        private OsuSpriteText pointsText;
        private OsuSpriteText rotationText;
        private OsuSpriteText equippedText;

        private string selectedId = string.Empty;
        private IStoreCard selectedCard;
        private readonly List<IStoreCard> cards = new List<IStoreCard>();

        public CosmeticStoreOverlay()
        {
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            buySample = audio.Samples.Get(@"SongSelect/confirm-selection");
            equipSample = audio.Samples.Get(@"UI/check-on");
            unequipSample = audio.Samples.Get(@"UI/check-off");

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
                    Children = new Drawable[]
                    {
                        mainPanel = new BriefingGlass
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
                        createCloseButton(),
                    },
                },
                adminOverlay = new CosmeticAdminOverlay(),
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
                                        adminGear = createAdminGear(),
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
                    equippedText = new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                        Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
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
                cosmetics.EquippedTrailId.BindValueChanged(e => { refreshCards(); updateEquippedText(); playEquipSound(e.NewValue); });
                cosmetics.EquippedNameColourId.BindValueChanged(e => { refreshCards(); updateEquippedText(); playEquipSound(e.NewValue); });
                cosmetics.InventoryChanged += onInventoryChanged;
                // Admin pulled an item in/out of the store pool: rebuild so the
                // store list reflects it live, and persist the change server-side
                // so it applies for everyone (no-op / 403 for non-admins).
                cosmetics.StoreCurationChanged += () => Schedule(() =>
                {
                    rebuildCards();
                    pushStoreConfig();
                });

                int hours = (int)(cosmetics.SecondsUntilRotation() / 3600);
                rotationText.Text = $"Featured rotates in ~{hours}h";
                updateEquippedText();
            }

            // The admin cog only shows for admins (server-set IsAdmin). Bound so
            // it appears even if the local user payload arrives after load.
            if (api != null && adminGear != null)
                api.LocalUser.BindValueChanged(u => adminGear.Alpha = localUserIsAdmin(u.NewValue) ? 1 : 0, true);

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
        private void onInventoryChanged() => Schedule(() =>
        {
            refreshCards();
            // A buy is the only thing that grows the inventory while the store is
            // open, so play the purchase chime here (single chokepoint for trails,
            // name colours and auras alike).
            if (State.Value == Visibility.Visible)
                buySample?.Play();
        });

        // Equip / unequip feedback. An empty value (the aura "none" sentinel is
        // passed in as empty) means "cleared", so play the softer off sound.
        private void playEquipSound(string newValue)
        {
            if (State.Value != Visibility.Visible)
                return;

            (string.IsNullOrEmpty(newValue) ? unequipSample : equipSample)?.Play();
        }

        private void refreshCards()
        {
            foreach (var card in cards)
                card.RefreshState();
        }

        private void updateEquippedText()
        {
            if (equippedText == null || cosmetics == null)
                return;

            var trail = CosmeticCatalog.Trails.FirstOrDefault(t => t.Id == cosmetics.EquippedTrailId.Value);
            var colour = CosmeticNameColourCatalog.GetById(cosmetics.EquippedNameColourId.Value, api?.LocalUser.Value);

            equippedText.Text = $"Equipped trail: {trail?.Name ?? "none"}   ·   name colour: {colour?.Name ?? "none"}";
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
                : CosmeticCatalog.Trails.Where(t => cosmetics?.IsStoreEnabled(t.Id) ?? true);

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

                if (inventory && title == "Cursor Trails")
                    addNoneCard("trail", "No Cursor Trail", FontAwesome.Solid.Ban,
                        () => string.IsNullOrEmpty(cosmetics?.EquippedTrailId.Value),
                        () => cosmetics?.Unequip());

                foreach (var def in list)
                {
                    bool isSelected = def.Id == selectedId;
                    var card = new StoreItemCard(def, cosmetics, featured.Contains(def.Id), isSelected, cardScroll);
                    card.Action = () => onCardClicked(def, card);
                    if (isSelected)
                        selectedCard = card;
                    cards.Add(card);
                    cardFlow.Add(card);
                }
            }

            // ── Name colours (second category) ──────────────────────────────
            var localUser = api?.LocalUser.Value;
            var colours = new List<CosmeticNameColour>();
            // Buyable: all in Store, owned-only in Inventory.
            colours.AddRange(inventory
                ? CosmeticNameColourCatalog.Buyable.Where(c => cosmetics?.IsOwned(c.Id) ?? false)
                : CosmeticNameColourCatalog.Buyable.Where(c => cosmetics?.IsStoreEnabled(c.Id) ?? true));
            // Role (earned) colours are NEVER sold: Inventory only, where you
            // already have them by role.
            if (inventory)
                colours.AddRange(CosmeticNameColourCatalog.GetEntitledEarned(localUser));

            if (colours.Count > 0)
            {
                anyContent = true;
                cardFlow.Add(categoryHeader("Name Colours", FontAwesome.Solid.Palette));

                if (inventory)
                    addNoneCard("namecolour", "No Name Colour", FontAwesome.Solid.Ban,
                        () => string.IsNullOrEmpty(cosmetics?.EquippedNameColourId.Value),
                        () => cosmetics?.UnequipNameColour());

                foreach (var c in colours)
                {
                    bool isSelected = c.Id == selectedId;
                    var card = new NameColourCard(c, cosmetics, isSelected);
                    card.Action = () => onNameColourClicked(c, card);
                    if (isSelected)
                        selectedCard = card;
                    cards.Add(card);
                    cardFlow.Add(card);
                }
            }

            // ── Auras (third category) ──────────────────────────────────────
            // Earned auras come from a role/group (price null). Stardust is the
            // one points-buyable aura. Store shows the buyable one; Inventory
            // shows earned auras plus any buyable aura you already own.
            // Equipping any applies it everywhere your name shows.
            var auraEntries = new List<(AuraPreset preset, int? price, CosmeticTier tier, string name)>();
            var seenAuras = new HashSet<string>();

            if (inventory)
            {
                // Client-owned buyable auras first, so a bought aura always
                // shows as OWNED/buyable even if the server later lists it too.
                foreach (var e in BuyableAuraCatalog.All)
                {
                    if (e.Preset != null && (cosmetics?.IsOwned(e.Id) ?? false) && seenAuras.Add(e.Id))
                        auraEntries.Add((e.Preset, e.Price, e.Tier, null));
                }

                // TESTING: list EVERY registered aura (minus the buyable one) so
                // they can all be previewed in one place and we can pick which
                // to sell. Names come from the server catalog where known, else
                // derived from the id. (For shipping, swap this back to listing
                // catalog.Available only — the auras the user is entitled to.)
                var catalogNames = new Dictionary<string, string>();
                if (auraCatalog?.Available != null)
                {
                    foreach (var entry in auraCatalog.Available)
                        catalogNames[entry.Id] = entry.DisplayName;
                }

                foreach (var preset in AuraRegistry.AllPresets)
                {
                    // Skip buyable auras here; they surface via the buyable path
                    // (owned ones above, all of them in the Store tab).
                    if (BuyableAuraCatalog.GetById(preset.AuraId) != null)
                        continue;
                    if (!seenAuras.Add(preset.AuraId))
                        continue;

                    string nm = catalogNames.TryGetValue(preset.AuraId, out var dn) ? dn : null;
                    auraEntries.Add((preset, null, CosmeticTier.Premium, nm));
                }
            }
            else
            {
                foreach (var e in BuyableAuraCatalog.All)
                {
                    if (e.Preset != null && (cosmetics?.IsStoreEnabled(e.Id) ?? true) && seenAuras.Add(e.Id))
                        auraEntries.Add((e.Preset, e.Price, e.Tier, null));
                }
            }

            if (auraEntries.Count > 0)
            {
                anyContent = true;
                cardFlow.Add(categoryHeader("Auras", FontAwesome.Solid.Sun));

                if (inventory)
                    addNoneCard("aura", "No Aura", FontAwesome.Solid.Ban,
                        () => api?.LocalUser.Value == null || AuraRegistry.ResolveForUser(api.LocalUser.Value) == null,
                        unequipAura);

                foreach (var (preset, price, tier, name) in auraEntries)
                {
                    bool isSelected = preset.AuraId == selectedId;
                    var card = new AuraCard(preset, price, tier, cosmetics, name, isSelected);
                    card.Action = () => onAuraClicked(preset, price, tier, name, card);
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

        // A "clear / none" tile, placed first in an Inventory category. Click
        // unequips whatever is active in that category and selects the tile.
        private void addNoneCard(string key, string label, IconUsage icon, Func<bool> isActive, Action unequip)
        {
            string id = "none:" + key;
            bool isSelected = id == selectedId;

            var card = new NoneCard(key, label, icon, isActive, isSelected);
            card.Action = () =>
            {
                unequip?.Invoke();
                setSelectedCard(card, id);
                detailContainer.Clear();
                refreshCards();
                showToast($"Cleared: {label}");
            };

            if (isSelected)
                selectedCard = card;

            cards.Add(card);
            cardFlow.Add(card);
        }

        private void onCardClicked(CosmeticTrailDefinition def, StoreItemCard card)
        {
            // Inventory: a single click equips (fast skin-swap-style flow).
            if (tabs.Current.Value == StoreTab.Inventory && (cosmetics?.IsOwned(def.Id) ?? false))
            {
                cosmetics.Equip(def.Id);
                showToast($"Equipped {def.Name}");
            }

            setSelectedCard(card, def.Id);
            detailContainer.Clear();
            detailContainer.Add(new CosmeticDetailPanel(def, cosmetics, showToast));
        }

        private void onNameColourClicked(CosmeticNameColour colour, NameColourCard card)
        {
            if (tabs.Current.Value == StoreTab.Inventory && (cosmetics?.IsOwned(colour.Id) ?? false))
            {
                cosmetics.EquipNameColour(colour.Id);
                showToast($"Equipped {colour.Name}");
            }

            setSelectedCard(card, colour.Id);
            detailContainer.Clear();
            detailContainer.Add(new NameColourDetailPanel(colour, cosmetics, showToast));
        }

        private void onAuraClicked(AuraPreset preset, int? price, CosmeticTier tier, string name, AuraCard card)
        {
            bool owned = price == null || (cosmetics?.IsOwned(preset.AuraId) ?? false);
            string display = string.IsNullOrEmpty(name) ? AuraCard.DisplayNameFor(preset.AuraId) : name;

            // Inventory + owned/earned: single click equips (fast skin-swap flow,
            // same as trails / name colours). Buyable + unowned: just open the
            // detail panel so the user can buy it there.
            if (tabs.Current.Value == StoreTab.Inventory && owned)
            {
                equipAura(preset.AuraId);
                showToast($"Equipped {display} aura");
            }

            setSelectedCard(card, preset.AuraId);
            detailContainer.Clear();
            detailContainer.Add(new AuraDetailPanel(preset, price, tier, name, cosmetics, equipAura, unequipAura, showToast));
        }

        // Equip an aura the same way the settings dropdown does: update the
        // in-memory local user + fire the aura-changed channel (so every
        // surface re-resolves in place), then persist server-side. Finally
        // refresh card badges so the EQUIPPED state moves to the new aura.
        private void equipAura(string auraId)
        {
            if (api?.LocalUser.Value == null)
                return;

            api.LocalUser.Value.EquippedAura = auraId;
            UserAuraEvents.NotifyUserAuraChanged(api.LocalUser.Value.Id, auraId);

            var req = new UpdateEquippedAuraRequest(auraId);
            api.Queue(req);

            refreshCards();
            playEquipSound(auraId == "none" ? string.Empty : auraId);
        }

        // "none" is the server sentinel for "no aura" — clears any equipped or
        // group-default aura so the local user renders plain.
        private void unequipAura() => equipAura("none");

        private void setSelectedCard(IStoreCard card, string id)
        {
            selectedId = id;

            if (selectedCard != null && !ReferenceEquals(selectedCard, card))
                selectedCard.SetSelected(false);
            selectedCard = card;
            card?.SetSelected(true);
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

        private Drawable createCloseButton() => new CloseButton
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Margin = new MarginPadding(12),
            Action = Hide,
        };

        // Admin-only cog in the header that opens the store curation panel. Its
        // visibility tracks server-confirmed admin status (bound in LoadComplete).
        private Drawable createAdminGear() => new AdminGearButton
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Alpha = localUserIsAdmin(api?.LocalUser.Value) ? 1 : 0,
            Action = () => adminOverlay?.ToggleVisibility(),
        };

        // Server-confirmed admin: either the stock is_admin flag OR membership of
        // the torii-admin group (how g0v0 actually marks staff — same identifier
        // the role colours / auras key off). Either is set server-side, so it
        // can't be spoofed from the client.
        private static bool localUserIsAdmin(APIUser u) =>
            u != null && (u.IsAdmin || (u.Groups?.Any(g => g.Identifier == "torii-admin") ?? false));

        // Clicking anywhere outside the panel closes the store. Clicks that land
        // on the panel (even empty areas) are left to its own children.
        protected override bool OnClick(ClickEvent e)
        {
            if (mainPanel != null && !mainPanel.ReceivePositionalInputAt(e.ScreenSpaceMousePosition))
            {
                Hide();
                return true;
            }

            return base.OnClick(e);
        }

        protected override void PopIn()
        {
            this.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            mainPanel.ScaleTo(0.94f).ScaleTo(1f, BriefingTheme.EntranceDuration, Easing.OutBack)
                     .MoveToY(20).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
            fetchAuraCatalog();
            fetchStoreConfig();
            rebuildCards();
        }

        // Pull the authoritative aura list from the server (same source the
        // settings picker uses) so the Inventory shows every aura the user can
        // equip — including ones not derived from a group. Non-fatal on
        // failure: rebuildCards falls back to the local group-based list.
        private void fetchAuraCatalog()
        {
            if (api?.IsLoggedIn != true)
                return;

            var req = new GetAuraCatalogRequest();
            req.Success += catalog => Schedule(() =>
            {
                auraCatalog = catalog;
                rebuildCards();
            });
            api.Queue(req);
        }

        // Pull the admin-curated store pool config so the store hides items an
        // admin pulled from sale. The server is the source of truth (shared
        // across clients); falls back to the local cache if the request fails
        // (e.g. a server that hasn't shipped the endpoint yet).
        private void fetchStoreConfig()
        {
            if (api?.IsLoggedIn != true)
                return;

            var req = new GetStoreConfigRequest();
            req.Success += cfg => Schedule(() =>
            {
                cosmetics?.ApplyServerDisabled(cfg.Disabled ?? System.Array.Empty<string>());
                rebuildCards();
            });
            api.Queue(req);
        }

        // Persist the current disabled-id set server-side (admin only). The
        // server re-validates admin, so a non-admin call simply 403s.
        private void pushStoreConfig()
        {
            if (api?.IsLoggedIn != true || !localUserIsAdmin(api.LocalUser.Value))
                return;

            string[] ids = cosmetics?.StoreDisabledIds?.ToArray() ?? System.Array.Empty<string>();
            api.Queue(new UpdateStoreConfigRequest(ids));
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

        // Small cog button (admin only) that opens the store curation panel.
        private partial class AdminGearButton : OsuClickableContainer
        {
            private SpriteIcon icon;

            public AdminGearButton()
            {
                Size = new Vector2(26);
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Child = icon = new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Icon = FontAwesome.Solid.Cog,
                    Size = new Vector2(18),
                    Colour = Color4.White.Opacity(0.75f),
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                icon.FadeColour(BriefingTheme.AccentPink, 150, Easing.OutQuint);
                this.ScaleTo(1.12f, 150, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                icon.FadeColour(Color4.White.Opacity(0.75f), 200, Easing.OutQuint);
                this.ScaleTo(1f, 200, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }

        public enum StoreTab
        {
            Store,
            Inventory,
        }

        // Round corner "X" button on the panel; hover brightens to pink.
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
