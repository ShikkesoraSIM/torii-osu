// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Overlays.Settings.Sections.Torii;

namespace osu.Game.Overlays.Settings.Sections
{
    /// <summary>
    /// Settings section grouping Torii-specific options. Subsections are added as
    /// features are ported over.
    /// </summary>
    public partial class ToriiSection : SettingsSection
    {
        public override LocalisableString Header => "Torii";

        public override Drawable CreateIcon() => new SpriteIcon
        {
            Icon = FontAwesome.Solid.Bolt
        };

        public ToriiSection()
        {
            Add(new ToriiInterfaceSettings());
            Add(new ToriiServerSettings());
        }
    }
}
