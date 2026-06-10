// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>Shared squircle "X" close button matching the Torii Briefing's
    /// CloseTile (subtle white tile, hover scale), so every Torii cosmetics /
    /// admin overlay closes with the same control.</summary>
    public partial class ToriiCloseButton : OsuClickableContainer
    {
        private Box hoverBox;
        private Container tile;

        public ToriiCloseButton()
        {
            Size = new Vector2(34);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = tile = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = BriefingTheme.CornerSm,
                CornerExponent = BriefingTheme.SquircleExponent,
                MaskingSmoothness = 1.2f,
                BorderThickness = 1f,
                BorderColour = Color4.White.Opacity(0.10f),
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White.Opacity(0.06f) },
                    hoverBox = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, Colour = Color4.White.Opacity(0.10f) },
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(13),
                        Icon = FontAwesome.Solid.Times,
                        Colour = Color4.White.Opacity(0.78f),
                    },
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            hoverBox.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            tile.ScaleTo(1.06f, BriefingTheme.HoverDuration, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hoverBox.FadeOut(BriefingTheme.HoverDuration, Easing.OutQuint);
            tile.ScaleTo(1f, BriefingTheme.HoverDuration, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            tile.ScaleTo(0.94f, 80, Easing.OutQuint);
            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            tile.ScaleTo(1.06f, 200, Easing.OutQuint);
            base.OnMouseUp(e);
        }
    }
}
