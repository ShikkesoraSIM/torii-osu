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
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>"Clear / none" tile placed first in an Inventory category. Click
    /// unequips whatever is active in that category (trail / name colour / aura)
    /// so the player can go back to plain. Highlights when nothing is equipped.</summary>
    public partial class NoneCard : OsuClickableContainer, IStoreCard
    {
        public string ItemId => "none:" + key;

        private readonly string key;
        private readonly string label;
        private readonly IconUsage icon;
        private readonly Func<bool> isActive;
        private readonly bool startSelected;

        private Container content;
        private Container selectionBorder;
        private Container activeBorder;
        private Container badgeHolder;
        private Box hoverHighlight;
        private SpriteIcon glyph;

        public NoneCard(string key, string label, IconUsage icon, Func<bool> isActive, bool selected = false)
        {
            this.key = key;
            this.label = label;
            this.icon = icon;
            this.isActive = isActive;
            startSelected = selected;
            Size = new Vector2(244, 168);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            bool active = isActive?.Invoke() ?? false;

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
                                new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(20, 21, 28, 255) },
                                glyph = new SpriteIcon
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Y = -10,
                                    Icon = icon,
                                    Size = new Vector2(34),
                                    Colour = Color4.White.Opacity(0.28f),
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
                                            Text = label,
                                            Font = OsuFont.GetFont(size: BriefingTheme.TypeHeadline, weight: FontWeight.SemiBold),
                                            RelativeSizeAxes = Axes.X,
                                        },
                                        new OsuSpriteText
                                        {
                                            Text = "Removes what's equipped",
                                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                                            Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
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
                        Child = active ? activePill() : Empty(),
                    },
                    // Green outline when this "none" state is the active one.
                    activeBorder = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = BriefingTheme.CornerSm,
                        BorderThickness = 2f,
                        BorderColour = BriefingTheme.AccentGain.Opacity(0.7f),
                        Alpha = active ? 1 : 0,
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

        private Drawable activePill() => new Container
        {
            Anchor = Anchor.TopLeft,
            Origin = Anchor.TopLeft,
            Margin = new MarginPadding(8),
            AutoSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 5f,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = BriefingTheme.AccentGain.Opacity(0.92f) },
                new OsuSpriteText
                {
                    Margin = new MarginPadding { Horizontal = 7, Vertical = 3 },
                    Text = "ACTIVE",
                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                    Colour = Color4.Black.Opacity(0.85f),
                },
            },
        };

        public void SetSelected(bool selected) => selectionBorder?.FadeTo(selected ? 1 : 0, 140, Easing.OutQuint);

        public void RefreshState()
        {
            if (badgeHolder == null)
                return;

            bool active = isActive?.Invoke() ?? false;
            badgeHolder.Child = active ? activePill() : Empty();
            activeBorder.FadeTo(active ? 1 : 0, 140, Easing.OutQuint);
            glyph.FadeColour(Color4.White.Opacity(active ? 0.5f : 0.28f), 140, Easing.OutQuint);
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
