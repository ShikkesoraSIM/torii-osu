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
using osu.Framework.Input.Events;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>Store tile for a username colour: shows your name painted in the
    /// colour, with a name/price/owned/equipped footer. Click selects (or equips
    /// in Inventory) via the overlay.</summary>
    public partial class NameColourCard : OsuClickableContainer, IStoreCard
    {
        public string ItemId => colour.Id;

        private readonly CosmeticNameColour colour;
        private readonly ToriiCosmeticsManager cosmetics;
        private readonly bool startSelected;

        private Container content;
        private Container selectionBorder;
        private Container footerHolder;
        private Container badgeHolder;
        private Box hoverHighlight;

        public NameColourCard(CosmeticNameColour colour, ToriiCosmeticsManager cosmetics, bool selected = false)
        {
            this.colour = colour;
            this.cosmetics = cosmetics;
            startSelected = selected;
            Size = new Vector2(244, 168);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            bool owned = cosmetics?.IsOwned(colour.Id) ?? false;
            bool equipped = cosmetics != null && cosmetics.EquippedNameColourId.Value == colour.Id;

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
                                // Your name in the colour, centred (the preview).
                                new NameColourText(colour, 26f)
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Y = -10,
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
                                            Text = colour.Name,
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
                        Child = owned ? ownedPill(equipped) : Empty(),
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
            Color4 footerColour = equipped ? BriefingTheme.AccentGain
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
                        Text = colour.Tier.ToString().ToUpperInvariant(),
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                        Colour = tierColour(colour.Tier),
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
                    Text = equipped ? "EQUIPPED" : "OWNED",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                    Colour = footerColour,
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
                    Colour = footerColour,
                });
                row.Add(new OsuSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = $"{colour.Price:N0}",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                    Colour = footerColour,
                });
            }

            return row;
        }

        private Drawable ownedPill(bool equipped) => new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Margin = new MarginPadding(8),
            AutoSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 5f,
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = (equipped ? BriefingTheme.AccentGain : BriefingTheme.AccentSky).Opacity(0.92f),
                },
                new OsuSpriteText
                {
                    Margin = new MarginPadding { Horizontal = 7, Vertical = 3 },
                    Text = equipped ? "EQUIPPED" : "OWNED",
                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                    Colour = Color4.Black.Opacity(0.85f),
                },
            },
        };

        public void SetSelected(bool selected) => selectionBorder?.FadeTo(selected ? 1 : 0, 140, Easing.OutQuint);

        public void RefreshState()
        {
            if (footerHolder == null)
                return;

            bool owned = cosmetics?.IsOwned(colour.Id) ?? false;
            bool equipped = cosmetics != null && cosmetics.EquippedNameColourId.Value == colour.Id;

            footerHolder.Child = createFooter(owned, equipped);
            badgeHolder.Child = owned ? ownedPill(equipped) : Empty();
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

        private static Color4 tierColour(CosmeticTier tier) => tier switch
        {
            CosmeticTier.Basic => new Color4(150, 160, 175, 255),
            CosmeticTier.Special => BriefingTheme.AccentSky,
            CosmeticTier.Premium => BriefingTheme.AccentAmber,
            _ => Color4.White,
        };
    }
}
