// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    public partial class ToriiInterfaceSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Interface";

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                new UIThemeDropdownAndRestart(),
                new PotatoModeToggleAndRestart(),
            };
        }
    }
}
