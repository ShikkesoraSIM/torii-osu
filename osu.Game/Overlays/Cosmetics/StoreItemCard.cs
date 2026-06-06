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
    /// <summary>A shop tile: a big live trail preview with the name, tier and a
    /// price / owned / equipped footer over it. Clicking it selects (Store) or
    /// equips (Inventory) — wired by the overlay. Hover lifts it; the overlay
    /// can mark it selected for a highlighted border.</summary>
    public partial class StoreItemCard : OsuClickableContainer
    {
        private readonly CosmeticTrailDefinition def;
        private readonly ToriiCosmeticsManager cosmetics;
        private readonly bool featured;
        private readonly bool startSelected;
        private readonly Drawable viewport;

        private Container content;
        private Container selectionBorder;
        private Box hoverHighlight;
        private CosmeticTrailPreview preview;

        /// <param name="viewport">The scroll viewport; the preview only animates
        /// while the card overlaps it, so off-screen cards stay cheap.</param>
        public StoreItemCard(CosmeticTrailDefinition def, ToriiCosmeticsManager cosmetics, bool featured = false, bool selected = false, Drawable viewport = null)
        {
            this.def = def;
            this.cosmetics = cosmetics;
            this.featured = featured;
            startSelected = selected;
            this.viewport = viewport;
            Size = new Vector2(244, 168);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            bool owned = cosmetics?.IsOwned(def.Id) ?? false;
            bool equipped = cosmetics != null && cosmetics.EquippedTrailId.Value == def.Id;

            Color4 footerColour = equipped ? BriefingTheme.AccentGain
                : owned ? BriefingTheme.AccentSky
                : BriefingTheme.AccentAmber;

            var clipChildren = new Drawable[]
            {
                // The star of the card: a calm live preview of the actual trail.
                preview = new CosmeticTrailPreview(def, 0.85f) { RelativeSizeAxes = Axes.Both },
                // Scrim so the name/price stay legible over a bright trail.
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 78,
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
                            Text = def.Name,
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeHeadline, weight: FontWeight.SemiBold),
                            RelativeSizeAxes = Axes.X,
                        },
                        createFooter(owned, equipped, footerColour),
                    },
                },
                hoverHighlight = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.White.Opacity(0.06f),
                    Blending = BlendingParameters.Additive,
                    Alpha = 0,
                },
            };

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
                            Children = clipChildren,
                        },
                    },
                    // Featured star (daily-rotation flag).
                    featured
                        ? new SpriteIcon
                        {
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            Margin = new MarginPadding(9),
                            Icon = FontAwesome.Solid.Star,
                            Size = new Vector2(13),
                            Colour = BriefingTheme.AccentAmber,
                        }
                        : Empty(),
                    // Ownership badge (top-left pill).
                    owned ? ownedPill(equipped) : Empty(),
                    // Selection border, faded in by the overlay when picked.
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

            preview.AnimationViewport = viewport;
        }

        private Drawable createFooter(bool owned, bool equipped, Color4 footerColour)
        {
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
                        Text = def.Tier.ToString().ToUpperInvariant(),
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                        Colour = tierColour(def.Tier),
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
                    Text = $"{def.Price:N0}",
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

        /// <summary>Highlight (or clear) this card's selection border.</summary>
        public void SetSelected(bool selected) => selectionBorder?.FadeTo(selected ? 1 : 0, 140, Easing.OutQuint);

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
