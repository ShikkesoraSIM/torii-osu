// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;
using osu.Game.Localisation;

namespace osu.Game.Configuration
{
    public enum ToolbarDensityMode
    {
        [LocalisableDescription(typeof(UserInterfaceStrings), nameof(UserInterfaceStrings.ToolbarDensityAuto))]
        Auto,

        [LocalisableDescription(typeof(UserInterfaceStrings), nameof(UserInterfaceStrings.ToolbarDensityCompact))]
        Compact,

        [LocalisableDescription(typeof(UserInterfaceStrings), nameof(UserInterfaceStrings.ToolbarDensityComfortable))]
        Comfortable
    }
}
