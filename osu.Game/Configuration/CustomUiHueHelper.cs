// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Configuration
{
    public enum CustomUiHueScope
    {
        Menu,
        Overlays,
        SettingsPanel,
    }

    public static class CustomUiHueHelper
    {
        public static int ResolveHue(OsuConfigManager config, int fallbackHue, CustomUiHueScope scope)
        {
            if (!config.Get<bool>(OsuSetting.CustomUIHueEnabled))
                return fallbackHue;

            bool scopeEnabled = scope switch
            {
                CustomUiHueScope.Menu => config.Get<bool>(OsuSetting.CustomUIHueApplyToMenu),
                CustomUiHueScope.Overlays => config.Get<bool>(OsuSetting.CustomUIHueApplyToOverlays),
                CustomUiHueScope.SettingsPanel => config.Get<bool>(OsuSetting.CustomUIHueApplyToSettingsPanel),
                _ => false,
            };

            if (!scopeEnabled)
                return fallbackHue;

            return normaliseHue(config.Get<float>(OsuSetting.CustomUIHue));
        }

        private static int normaliseHue(float hue)
        {
            int rounded = (int)MathF.Round(hue);
            int normalised = rounded % 360;

            if (normalised < 0)
                normalised += 360;

            return normalised;
        }
    }
}
