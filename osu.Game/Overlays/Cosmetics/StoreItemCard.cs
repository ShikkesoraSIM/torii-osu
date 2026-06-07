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
    public partial class StoreItemCard : OsuClickableContainer, IStoreCard
    {
        public string ItemId => def.Id;

        private readonly CosmeticTrailDefinition def;
        private readonly ToriiCosmeticsManager cosmetics;
        private readonly bool featured;
        private readonly bool startSelected;
        private readonly Drawable viewport;

        private Container content;
        private Container selectionBorder;
        private Box hoverHighlight;
        private CosmeticTrailPreview preview;
        private Container footerHolder;
        private Container badgeHolder;
        private OsuClickableContainer cycleButton;
        private OsuSpriteText cycleLabel;
        private int cycleIndex = -1;

        // Style presets the bottom-right button steps through (length scale,
        // density mult, size mult). Ribbons skip the density steps.
        private static readonly (string name, float l, float d, float s)[] full_cycle =
        {
            ("Shorter", 0.45f, 1f, 1f),
            ("Longer", 1.3f, 1f, 1f),
            ("Denser", 1f, 1.35f, 1f),
            ("Sparser", 1f, 0.7f, 1f),
            ("Bigger", 1f, 1f, 1.4f),
            ("Smaller", 1f, 1f, 0.75f),
            ("Default", 1f, 1f, 1f),
        };

        private static readonly (string name, float l, float d, float s)[] ribbon_cycle =
        {
            ("Shorter", 0.45f, 1f, 1f),
            ("Longer", 1.3f, 1f, 1f),
            ("Bigger", 1f, 1f, 1.4f),
            ("Smaller", 1f, 1f, 0.75f),
            ("Default", 1f, 1f, 1f),
        };

        private (string name, float l, float d, float s)[] cyclePresets
            => def.Family == CosmeticTrailFamily.Ribbon ? ribbon_cycle : full_cycle;

        private bool canCustomise => (cosmetics?.IsOwned(def.Id) ?? false) && (cosmetics?.AdjustUnlocked ?? false);

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
                // The star of the card: a live preview of the actual trail (a
                // lively sweep speed so the hover animation doesn't feel sluggish).
                preview = new CosmeticTrailPreview(def, 2f) { RelativeSizeAxes = Axes.Both },
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
                        // Footer rebuilt in place on buy/equip (no preview reload).
                        footerHolder = new Container
                        {
                            AutoSizeAxes = Axes.Both,
                            Child = createFooter(owned, equipped, footerColour),
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
                    // Ownership badge (top-left pill), rebuilt in place on refresh.
                    badgeHolder = new Container
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        AutoSizeAxes = Axes.Both,
                        Child = owned ? ownedPill(equipped) : Empty(),
                    },
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
                    // Brief flash of the style name when cycling.
                    cycleLabel = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.Bold),
                        Alpha = 0,
                    },
                    // Bottom-right "next style" button: tap to cycle length /
                    // density / size presets for this trail.
                    cycleButton = new OsuClickableContainer
                    {
                        Anchor = Anchor.BottomRight,
                        Origin = Anchor.BottomRight,
                        Margin = new MarginPadding(8),
                        Size = new Vector2(28),
                        Masking = true,
                        CornerRadius = 14f,
                        Alpha = 0,
                        Action = cycle,
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black.Opacity(0.55f) },
                            new SpriteIcon
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Icon = FontAwesome.Solid.AngleRight,
                                Size = new Vector2(13),
                                Colour = Color4.White,
                            },
                        },
                    },
                },
            };

            if (startSelected)
                selectionBorder.Alpha = 1;

            preview.AnimationViewport = viewport;

            // Reflect the saved style + show the cycle button only when
            // customisation is available (owned + the account-wide unlock).
            if (canCustomise)
            {
                preview.InitialCustomisation = cosmetics.GetCustomisation(def.Id);
                cycleButton.Alpha = 1;
            }
        }

        /// <summary>Step to the next length/density/size preset for this trail.</summary>
        private void cycle()
        {
            if (cosmetics == null)
                return;

            var presets = cyclePresets;
            cycleIndex = (cycleIndex + 1) % presets.Length;
            var p = presets[cycleIndex];

            preview?.ApplyCustomisation(p.l, p.d, p.s);
            cosmetics.SetCustomisation(def.Id, p.l, p.d, p.s);

            cycleLabel.Text = p.name;
            cycleLabel.ClearTransforms();
            cycleLabel.FadeTo(0.95f).FadeOut(850, Easing.OutQuint);
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

        /// <summary>Update the owned / equipped badge + footer in place (cheap),
        /// WITHOUT recreating the preview. Used on buy / equip so we don't rebuild
        /// the whole grid (35 trails) and lag.</summary>
        public void RefreshState()
        {
            if (footerHolder == null)
                return;

            bool owned = cosmetics?.IsOwned(def.Id) ?? false;
            bool equipped = cosmetics != null && cosmetics.EquippedTrailId.Value == def.Id;

            Color4 footerColour = equipped ? BriefingTheme.AccentGain
                : owned ? BriefingTheme.AccentSky
                : BriefingTheme.AccentAmber;

            footerHolder.Child = createFooter(owned, equipped, footerColour);
            badgeHolder.Child = owned ? ownedPill(equipped) : Empty();
            cycleButton.Alpha = canCustomise ? 1 : 0;
        }

        protected override bool OnHover(HoverEvent e)
        {
            preview?.SetHovered(true); // animate this card live while hovered
            content.ScaleTo(1.035f, 220, Easing.OutQuint);
            hoverHighlight.FadeTo(1, 160, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            preview?.SetHovered(false); // back to the still snapshot
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
