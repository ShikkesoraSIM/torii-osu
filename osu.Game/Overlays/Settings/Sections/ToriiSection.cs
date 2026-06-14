// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Settings.Sections.Torii;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Settings.Sections
{
    /// <summary>
    /// Settings section grouping Torii-specific options. Subsections are added as
    /// features are ported over.
    /// </summary>
    public partial class ToriiSection : SettingsSection
    {
        public override LocalisableString Header => "Torii";

        // The Torii gate glyph in the red -> yellow gradient used on the dashboard
        // platform indicators ("Torii Windows" red / "Nova Mac" yellow).
        public override Drawable CreateIcon() => new ToriiGateGlyph
        {
            Size = new Vector2(18),
            Colour = ColourInfo.GradientVertical(new Color4(255, 64, 64, 255), new Color4(255, 204, 0, 255)),
        };

        public ToriiSection()
        {
            Add(new ToriiInterfaceSettings());
            Add(new ToriiServerSettings());
        }
    }
}
