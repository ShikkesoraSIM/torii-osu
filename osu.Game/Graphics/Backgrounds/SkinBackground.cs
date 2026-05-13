// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Game.Skinning;

namespace osu.Game.Graphics.Backgrounds
{
    internal partial class SkinBackground : Background
    {
        private readonly Skin skin;
        private readonly string primaryLookupName;

        /// <summary>
        /// Create a background sprite that pulls its texture from <paramref name="skin"/>.
        /// The skin is queried for <paramref name="primaryLookupName"/> first; if that returns
        /// null, the legacy <c>menu-background</c> name is tried next (so a skin without the
        /// primary key still works exactly like before). If neither resolves, the sprite
        /// keeps the texture loaded from <paramref name="fallbackTextureName"/> at base-class
        /// construction time.
        /// </summary>
        /// <param name="skin">The skin to query.</param>
        /// <param name="fallbackTextureName">The on-disk fallback texture path passed through
        /// to the <see cref="Background"/> base — used when the skin provides neither
        /// <paramref name="primaryLookupName"/> nor <c>menu-background</c>.</param>
        /// <param name="primaryLookupName">The skin-resource name to try first. Defaults to
        /// <c>menu-background</c> for backwards-compat with the original constructor signature.
        /// Pass <c>layout-background</c> (or any other name) to let skinners ship a different
        /// texture for the layout-dim layer than for the main-menu background — see the
        /// "Skinning extras &gt; Per-layer backgrounds" entry in the README.</param>
        public SkinBackground(Skin skin, string fallbackTextureName, string primaryLookupName = "menu-background")
            : base(fallbackTextureName)
        {
            this.skin = skin;
            this.primaryLookupName = primaryLookupName;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            // Torii: try the caller's preferred lookup name first (e.g. layout-background),
            // then the legacy menu-background. If neither is shipped by the skin we leave the
            // sprite's texture at whatever the Background base loaded from the fallback path.
            // This makes the "different image for the dim layer" Mash asked for opt-in by
            // file presence alone — no skin.ini config required.
            var primary = skin.GetTexture(primaryLookupName);
            if (primary != null)
            {
                Sprite.Texture = primary;
                return;
            }

            if (primaryLookupName != "menu-background")
            {
                var legacy = skin.GetTexture("menu-background");
                if (legacy != null)
                {
                    Sprite.Texture = legacy;
                    return;
                }
            }

            // No-op: Sprite.Texture stays at the fallback the base class loaded.
        }

        public override bool Equals(Background? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;

            return other.GetType() == GetType()
                   && ((SkinBackground)other).skin.SkinInfo.Equals(skin.SkinInfo)
                   // Torii: include the primary lookup name in equality so a layout-background
                   // SkinBackground and a menu-background SkinBackground for the same skin
                   // aren't treated as equivalent — otherwise the background-stack reuse
                   // optimisation in BackgroundScreenDefault.createBackground would render
                   // the layout one when the menu wanted a fresh menu-background load (or
                   // vice versa).
                   && ((SkinBackground)other).primaryLookupName == primaryLookupName;
        }
    }
}
