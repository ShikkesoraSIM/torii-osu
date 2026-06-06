// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>A shop tile: name, tier, and either a price or an owned/equipped
    /// badge. Clicking it selects (Store) or equips (Inventory) — wired by the
    /// overlay.</summary>
    public partial class StoreItemCard : OsuClickableContainer
    {
        private readonly CosmeticTrailDefinition def;
        private readonly ToriiCosmeticsManager cosmetics;

        private readonly bool featured;

        public StoreItemCard(CosmeticTrailDefinition def, ToriiCosmeticsManager cosmetics, bool featured = false)
        {
            this.def = def;
            this.cosmetics = cosmetics;
            this.featured = featured;
            Size = new Vector2(150, 92);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            bool owned = cosmetics?.IsOwned(def.Id) ?? false;
            bool equipped = cosmetics != null && cosmetics.EquippedTrailId.Value == def.Id;

            Color4 footerColour = equipped ? BriefingTheme.AccentGain
                : owned ? BriefingTheme.AccentSky
                : BriefingTheme.AccentAmber;

            string footer = equipped ? "EQUIPPED" : owned ? "OWNED" : $"{def.Price:N0} pts";

            Child = new BriefingGlass
            {
                RelativeSizeAxes = Axes.Both,
                RelativeContentSize = Axes.Both,
                CornerSize = BriefingTheme.CornerSm,
                SurfaceLift = 1.35f,
                ShadowOpacity = 0.18f,
                ShadowRadius = 8f,
                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding(BriefingTheme.SpacingSm),
                    Children = new Drawable[]
                    {
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 2),
                            Children = new Drawable[]
                            {
                                new TruncatingSpriteText
                                {
                                    Text = def.Name,
                                    Font = OsuFont.GetFont(size: BriefingTheme.TypeHeadline, weight: FontWeight.SemiBold),
                                    RelativeSizeAxes = Axes.X,
                                },
                                new OsuSpriteText
                                {
                                    Text = def.Tier.ToString().ToUpperInvariant(),
                                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                                    Colour = tierColour(def.Tier),
                                },
                            },
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            Text = footer,
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                            Colour = footerColour,
                        },
                    },
                },
            };

            if (featured)
            {
                Add(new SpriteIcon
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding(7),
                    Icon = FontAwesome.Solid.Star,
                    Size = new Vector2(12),
                    Colour = BriefingTheme.AccentAmber,
                });
            }
        }

        private static Color4 tierColour(CosmeticTier tier) => tier switch
        {
            CosmeticTier.Basic => new Color4(150, 160, 175, 255),
            CosmeticTier.Special => BriefingTheme.AccentSky,
            CosmeticTier.Premium => BriefingTheme.AccentAmber,
            _ => Color4.White,
        };
    }
}
