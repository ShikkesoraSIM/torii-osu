// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
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
    /// <summary>A store tile for the account-wide "custom UI accent hue" unlock.
    /// Unlike trail / name-colour cards this isn't a previewable cosmetic — it's
    /// a one-time capability bought with points, and it replaces the old
    /// supporter gate on the accent. Clicking buys it (when affordable and not
    /// already owned); a confirmation toast is raised via the supplied callback.</summary>
    public partial class AccentHueUnlockCard : OsuClickableContainer, IStoreCard
    {
        /// <summary>Stable id so the overlay can track selection / scroll to it.</summary>
        public const string UNLOCK_ID = "unlock:custom-accent-hue";

        public string ItemId => UNLOCK_ID;

        private readonly ToriiCosmeticsManager cosmetics;
        private readonly Action<string> showToast;
        private readonly bool startSelected;

        private Container content;
        private Container selectionBorder;
        private Box hoverHighlight;
        private Container footerHolder;

        public AccentHueUnlockCard(ToriiCosmeticsManager cosmetics, Action<string> showToast, bool selected = false)
        {
            this.cosmetics = cosmetics;
            this.showToast = showToast;
            startSelected = selected;
            Size = new Vector2(244, 168);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Action = onClick;

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
                                new SpriteIcon
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Y = -16,
                                    Icon = FontAwesome.Solid.Palette,
                                    Size = new Vector2(42),
                                    Colour = BriefingTheme.AccentPink,
                                    Alpha = 0.92f,
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
                                        new OsuSpriteText
                                        {
                                            Text = "Custom Accent Hue",
                                            Font = OsuFont.GetFont(size: BriefingTheme.TypeHeadline, weight: FontWeight.SemiBold),
                                        },
                                        footerHolder = new Container
                                        {
                                            AutoSizeAxes = Axes.Both,
                                            Child = createFooter(),
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

        private Drawable createFooter()
        {
            bool owned = cosmetics?.AccentUnlocked ?? false;

            if (owned)
            {
                return new OsuSpriteText
                {
                    Text = "OWNED",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                    Colour = BriefingTheme.AccentSky,
                };
            }

            return new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(6, 0),
                Children = new Drawable[]
                {
                    new SpriteIcon
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Icon = FontAwesome.Solid.Coins,
                        Size = new Vector2(11),
                        Colour = BriefingTheme.AccentAmber,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = $"{CosmeticEconomy.CustomAccentHueUnlock:N0}",
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                        Colour = BriefingTheme.AccentAmber,
                    },
                },
            };
        }

        private void onClick()
        {
            if (cosmetics == null)
                return;

            if (cosmetics.AccentUnlocked)
            {
                showToast?.Invoke("Custom accent hue already unlocked");
                return;
            }

            if (cosmetics.BuyAccentUnlock(CosmeticEconomy.CustomAccentHueUnlock))
            {
                showToast?.Invoke("Custom accent hue unlocked!");
                RefreshState();
            }
            else
            {
                showToast?.Invoke("Not enough points");
            }
        }

        /// <summary>Highlight (or clear) this card's selection border.</summary>
        public void SetSelected(bool selected) => selectionBorder?.FadeTo(selected ? 1 : 0, 140, Easing.OutQuint);

        /// <summary>Swap the footer between price and OWNED in place after a buy.</summary>
        public void RefreshState()
        {
            if (footerHolder != null)
                footerHolder.Child = createFooter();
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
    }
}
