// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics.Containers;
using osu.Game.Users;

namespace osu.Game.Overlays.Profile.Sections.Medals
{
    /// <summary>
    /// Displays a medal as a small clickable icon with tooltip.
    /// Implements the achievement display pattern from m1-lazer-web-main.
    /// </summary>
    public partial class DrawableMedalDisplay : OsuHoverContainer, IHasTooltip
    {
        private readonly Medal medal;
        private readonly bool achieved;
        private readonly string? achievedAt;

        private Sprite medalSprite = null!;
        private Container spriteContainer = null!;

        public LocalisableString TooltipText
        {
            get
            {
                var text = $"{medal.Name}";
                if (!string.IsNullOrEmpty(achievedAt))
                    text += $"\n{achievedAt}";
                return text;
            }
        }

        public DrawableMedalDisplay(Medal medal, bool achieved = false, string? achievedAt = null)
        {
            this.medal = medal;
            this.achieved = achieved;
            this.achievedAt = achievedAt;
        }

        [BackgroundDependencyLoader]
        private void load(TextureStore textureStore, LargeTextureStore largeTextureStore)
        {
            RelativeSizeAxes = Axes.Both;
            Masking = true;
            CornerRadius = 4;

            spriteContainer = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Child = medalSprite = new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                }
            };

            if (achieved)
            {
                // Achieved: full color, hover effects enabled - load immediately
                Alpha = 1f;
                // Try local resource first (TextureStore), fallback to URL (LargeTextureStore) only if local not found
                medalSprite.Texture = textureStore.Get(medal.ImageUrl) ?? largeTextureStore.Get(medal.ImageUrlFallback);
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Radius = 8,
                    Colour = Colour4.Black.Opacity(0.3f),
                };
            }
            else
            {
                // Unachieved: grayscale and reduced opacity - defer texture loading
                Alpha = 0.4f;
                medalSprite.Colour = Colour4.Gray;
                // Defer texture loading for unachieved medals - try local first, fallback to URL only if local not found
                Schedule(() => medalSprite.Texture = textureStore.Get(medal.ImageUrl) ?? largeTextureStore.Get(medal.ImageUrlFallback));
            }

            Child = spriteContainer;
        }

        protected override bool OnHover(HoverEvent e)
        {
            if (achieved)
            {
                // Scale up and enhance shadow on hover for achieved medals
                this.ScaleTo(1.1f, 200, Easing.OutQuint);
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Radius = 12,
                    Colour = Colour4.Black.Opacity(0.4f),
                };
            }
            else
            {
                // Slight opacity increase for unachieved medals
                this.FadeTo(0.6f, 200, Easing.OutQuint);
            }

            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            if (achieved)
            {
                this.ScaleTo(1f, 200, Easing.OutQuint);
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Radius = 8,
                    Colour = Colour4.Black.Opacity(0.3f),
                };
            }
            else
            {
                this.FadeTo(0.4f, 200, Easing.OutQuint);
            }

            base.OnHoverLost(e);
        }
    }
}
