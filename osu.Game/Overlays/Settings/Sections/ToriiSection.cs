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

        // Same torii-gate glyph the client badge uses, rendered like every other
        // settings-section icon (plain monochrome, tinted by the settings panel).
        public override Drawable CreateIcon() => new SpriteIcon
        {
            Icon = FontAwesome.Solid.ToriiGate
        };

        public ToriiSection()
        {
            Add(new ToriiInterfaceSettings());
            Add(new ToriiServerSettings());
            Add(new ToriiBriefingSettings());
        }
    }
}
