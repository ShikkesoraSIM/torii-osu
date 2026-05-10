// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Graphics;
using osu.Framework.Localisation;
using osu.Game.Overlays.Settings.Sections.Torii;
using FontAwesome = osu.Framework.Graphics.Sprites.FontAwesome;

namespace osu.Game.Overlays.Settings.Sections
{
    public partial class ToriiSection : SettingsSection
    {
        public override LocalisableString Header => "Torii";

        public override Drawable CreateIcon() => new ToriiSectionIcon();

        public ToriiSection()
        {
            // Build subsections in a list so the Android-only Torii subsection
            // can be conditionally appended without leaking an empty section
            // header onto Desktop / iOS users. Identical to upstream's pattern
            // for OS-specific settings (see UpdateSettings, GeneralSection).
            var subsections = new List<Drawable>
            {
                new ToriiBriefingSettings(),
                new ToriiInterfaceSettings(),
                // Gameplay-flow tweaks (long-attempt confirm prompt, etc).
                // Lives next to Interface because users browsing the Torii
                // section for "make the game feel a bit more careful" prefs
                // expect both kinds of toggles together.
                new ToriiGameplaySettings(),
                // User-aura cosmetics (toggle + equipped picker + live preview).
                // Placed between Interface and Server so users find it next to
                // the visual prefs rather than buried after networking stuff.
                new ToriiAuraSettings(),
                new ToriiServerSettings(),
                new ToriiStorageSettings(),
                new ToriiExperimentalSettings(),
            };

            // Android-only subsection. Skipped entirely on Desktop / iOS so
            // the section header doesn't render at all — the user told us
            // they don't want to see the row on Desktop ("solo si es la build
            // de android"), so we don't even construct it there.
            if (RuntimeInfo.OS == RuntimeInfo.Platform.Android)
                subsections.Add(new ToriiAndroidSettings());

            Children = subsections.ToArray();
        }

        private partial class ToriiSectionIcon : CompositeDrawable
        {
            public ToriiSectionIcon()
            {
                Size = new osuTK.Vector2(18);
            }

            [BackgroundDependencyLoader]
            private void load(TextureStore textures)
            {
                var texture = textures.Get(@"Torii/logo");

                InternalChild = texture != null
                    ? new Sprite
                    {
                        RelativeSizeAxes = Axes.Both,
                        Texture = texture,
                        FillMode = FillMode.Fit,
                    }
                    : new SpriteIcon
                    {
                        RelativeSizeAxes = Axes.Both,
                        Icon = FontAwesome.Solid.Server,
                        Colour = Color4Extensions.FromHex("ff66b3"),
                    };
            }
        }
    }
}
