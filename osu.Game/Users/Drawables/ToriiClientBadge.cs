// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Localisation;
using osu.Game.Graphics.Sprites;
using osuTK;

namespace osu.Game.Users.Drawables
{
    /// <summary>
    /// A small badge shown next to online users who are playing via the Torii client.
    /// Populated from <see cref="UserPresence.ClientName"/> == "torii".
    /// </summary>
    public partial class ToriiClientBadge : CompositeDrawable, IHasTooltip
    {
        public LocalisableString TooltipText => "Playing in Torii client";

        public ToriiClientBadge()
        {
            Size = new Vector2(16);
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load(TextureStore textures)
        {
            var texture = textures.Get(@"Torii/logo");

            if (texture != null)
            {
                InternalChild = new Sprite
                {
                    RelativeSizeAxes = Axes.Both,
                    Texture = texture,
                    FillMode = FillMode.Fit,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };
            }
            else
            {
                // Keep the fallback simple and project-compliant if resources are missing.
                InternalChild = new OsuSpriteText
                {
                    Text = "T",
                    Font = new FontUsage(size: 14),
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };
            }
        }

        /// <summary>
        /// Shows or hides the badge depending on whether the user is using the Torii client.
        /// </summary>
        public void UpdateClientName(string? clientName)
        {
            bool isTorii = clientName == "torii";
            this.FadeTo(isTorii ? 1f : 0f, 200, Easing.OutQuint);
        }
    }
}
