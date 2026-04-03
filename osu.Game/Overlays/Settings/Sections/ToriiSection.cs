// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Overlays.Settings.Sections.Torii;
using FontAwesome = osu.Framework.Graphics.Sprites.FontAwesome;

namespace osu.Game.Overlays.Settings.Sections
{
    public partial class ToriiSection : SettingsSection
    {
        public override LocalisableString Header => "Torii";

        public override Drawable CreateIcon() => new SpriteIcon
        {
            Icon = FontAwesome.Solid.Server,
        };

        public ToriiSection()
        {
            Children = new Drawable[]
            {
                new ToriiStorageSettings(),
                new ToriiExperimentalSettings(),
            };
        }
    }
}
