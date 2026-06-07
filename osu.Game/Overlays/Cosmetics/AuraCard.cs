// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Globalization;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>Inventory/store tile for a user aura: shows a sample username with
    /// the aura's glow + particles playing behind it, plus a name / earned /
    /// owned / price footer. Earned auras equip on click; buyable ones open the
    /// detail panel to purchase.</summary>
    public partial class AuraCard : OsuClickableContainer, IStoreCard
    {
        public string ItemId => preset.AuraId;

        private readonly AuraPreset preset;

        // Null = earned (granted by a group). Set = a buyable aura (points price).
        private readonly int? price;
        private readonly CosmeticTier tier;
        private readonly ToriiCosmeticsManager cosmetics;
        private readonly string displayNameOverride;
        private readonly bool startSelected;

        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        private Container content;
        private Container selectionBorder;
        private Container footerHolder;
        private Container badgeHolder;
        private Box hoverHighlight;

        public AuraCard(AuraPreset preset, int? price, CosmeticTier tier, ToriiCosmeticsManager cosmetics, string displayNameOverride = null, bool selected = false)
        {
            this.preset = preset;
            this.price = price;
            this.tier = tier;
            this.cosmetics = cosmetics;
            this.displayNameOverride = displayNameOverride;
            startSelected = selected;
            Size = new Vector2(244, 168);
        }

        private bool earned => price == null;
        private bool isOwned => earned || (cosmetics?.IsOwned(preset.AuraId) ?? false);

        private bool isEquipped => api?.LocalUser.Value != null
                                   && AuraRegistry.ResolveForUser(api.LocalUser.Value)?.AuraId == preset.AuraId;

        [BackgroundDependencyLoader]
        private void load()
        {
            bool equipped = isEquipped;
            bool owned = isOwned;
            string username = api?.LocalUser.Value?.Username ?? "Aura";

            // A throwaway user with this aura equipped, so the shared
            // UserAuraContainer renders the real glow + particles in the preview.
            var sampleUser = new APIUser { Id = -1, Username = username, EquippedAura = preset.AuraId };

            Child = content = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Children = new Drawable[]
                {
                    new BriefingGlass
                    {
                        RelativeSizeAxes = Axes.Both,
                        RelativeContentSize = Axes.Both,
                        CornerSize = BriefingTheme.CornerSm,
                        SurfaceLift = 1.2f,
                        ShadowOpacity = 0.2f,
                        ShadowRadius = 10f,
                        Child = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Masking = true,
                            CornerRadius = BriefingTheme.CornerSm,
                            Children = new Drawable[]
                            {
                                new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(16, 16, 24, 255) },
                                // Sample name with the aura playing behind it.
                                new Container
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    AutoSizeAxes = Axes.Both,
                                    Y = -10,
                                    Child = UserAuraContainer.Wrap(sampleUser, new OsuSpriteText
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Text = username,
                                        Font = OsuFont.GetFont(size: 22, weight: FontWeight.SemiBold),
                                    }),
                                },
                                new Box
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Height = 70,
                                    Anchor = Anchor.BottomLeft,
                                    Origin = Anchor.BottomLeft,
                                    Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0f), Color4.Black.Opacity(0.82f)),
                                },
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Anchor = Anchor.BottomLeft,
                                    Origin = Anchor.BottomLeft,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, 3),
                                    Padding = new MarginPadding { Horizontal = 12, Bottom = 10 },
                                    Children = new Drawable[]
                                    {
                                        new TruncatingSpriteText
                                        {
                                            Text = displayName,
                                            Font = OsuFont.GetFont(size: BriefingTheme.TypeHeadline, weight: FontWeight.SemiBold),
                                            RelativeSizeAxes = Axes.X,
                                        },
                                        footerHolder = new Container
                                        {
                                            AutoSizeAxes = Axes.Both,
                                            Child = createFooter(owned, equipped),
                                        },
                                    },
                                },
                                hoverHighlight = new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = Color4.White.Opacity(0.06f),
                                    Blending = BlendingParameters.Additive,
                                    Alpha = 0,
                                },
                            },
                        },
                    },
                    badgeHolder = new Container
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        AutoSizeAxes = Axes.Both,
                        Child = owned ? badge(equipped) : Empty(),
                    },
                    // Persistent rarity border: amber for earned auras, tier
                    // colour for buyable ones. Pink selection border draws over.
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = BriefingTheme.CornerSm,
                        BorderThickness = earned ? 2.5f : rarityBorderThickness(tier),
                        BorderColour = (earned ? BriefingTheme.AccentAmber : tierColour(tier)).Opacity(0.65f),
                        Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent, AlwaysPresent = true },
                    },
                    selectionBorder = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = BriefingTheme.CornerSm,
                        BorderThickness = 2.5f,
                        BorderColour = BriefingTheme.AccentPink,
                        Alpha = 0,
                        Child = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent, AlwaysPresent = true },
                    },
                },
            };

            if (startSelected)
                selectionBorder.Alpha = 1;
        }

        private Drawable createFooter(bool owned, bool equipped)
        {
            Color4 stateColour = equipped ? BriefingTheme.AccentGain
                : earned ? BriefingTheme.AccentAmber
                : owned ? BriefingTheme.AccentSky
                : BriefingTheme.AccentAmber;

            var row = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(6, 0),
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = earned ? "AURA" : tier.ToString().ToUpperInvariant(),
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                        Colour = earned ? BriefingTheme.AccentAmber : tierColour(tier),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = "·",
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                        Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                    },
                },
            };

            if (owned)
            {
                row.Add(new OsuSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = equipped ? "EQUIPPED" : earned ? "EARNED" : "OWNED",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                    Colour = stateColour,
                });
            }
            else
            {
                row.Add(new SpriteIcon
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Icon = FontAwesome.Solid.Coins,
                    Size = new Vector2(11),
                    Colour = stateColour,
                });
                row.Add(new OsuSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = $"{price:N0}",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                    Colour = stateColour,
                });
            }

            return row;
        }

        private Drawable badge(bool equipped)
        {
            Color4 pill = equipped ? BriefingTheme.AccentGain
                : earned ? BriefingTheme.AccentAmber
                : BriefingTheme.AccentSky;

            return new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Margin = new MarginPadding(8),
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 5f,
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = pill.Opacity(0.92f) },
                    new OsuSpriteText
                    {
                        Margin = new MarginPadding { Horizontal = 7, Vertical = 3 },
                        Text = equipped ? "EQUIPPED" : earned ? "EARNED" : "OWNED",
                        Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                        Colour = Color4.Black.Opacity(0.85f),
                    },
                },
            };
        }

        public void SetSelected(bool selected) => selectionBorder?.FadeTo(selected ? 1 : 0, 140, Easing.OutQuint);

        public void RefreshState()
        {
            if (footerHolder == null)
                return;

            bool equipped = isEquipped;
            bool owned = isOwned;
            footerHolder.Child = createFooter(owned, equipped);
            badgeHolder.Child = owned ? badge(equipped) : Empty();
        }

        protected override bool OnHover(HoverEvent e)
        {
            content.ScaleTo(1.035f, 220, Easing.OutQuint);
            hoverHighlight.FadeTo(1, 160, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            content.ScaleTo(1f, 260, Easing.OutQuint);
            hoverHighlight.FadeTo(0, 220, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        private string displayName => string.IsNullOrEmpty(displayNameOverride) ? DisplayNameFor(preset.AuraId) : displayNameOverride;

        // Server catalog owns the canonical names; offline we derive a friendly
        // label from the aura id ("admin-embers" -> "Admin Embers").
        public static string DisplayNameFor(string auraId)
        {
            string[] parts = (auraId ?? string.Empty).Split('-', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts.Select(p => p.Length == 0
                ? p
                : char.ToUpper(p[0], CultureInfo.InvariantCulture) + p.Substring(1)));
        }

        private static Color4 tierColour(CosmeticTier t) => t switch
        {
            CosmeticTier.Basic => new Color4(150, 160, 175, 255),
            CosmeticTier.Special => BriefingTheme.AccentSky,
            CosmeticTier.Premium => BriefingTheme.AccentAmber,
            _ => Color4.White,
        };

        private static float rarityBorderThickness(CosmeticTier t) => t switch
        {
            CosmeticTier.Special => 1.5f,
            CosmeticTier.Premium => 2.5f,
            _ => 1f,
        };
    }
}
