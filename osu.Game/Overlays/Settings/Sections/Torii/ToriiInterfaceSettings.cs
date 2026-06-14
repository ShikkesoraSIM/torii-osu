// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    public partial class ToriiInterfaceSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Interface";

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                new UIThemeDropdownAndRestart(),
                new PotatoModeToggleAndRestart(),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Custom UI hue",
                    HintText = "Tint the UI (menus, overlays, settings) to a custom hue instead of the theme default.",
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIHueEnabled),
                })
                {
                    Keywords = new[] { @"hue", @"colour", @"color", @"accent", @"tint", @"theme" },
                },
                new SettingsItemV2(new FormHuePicker
                {
                    Caption = "UI hue",
                    HintText = "Base hue applied across the UI when custom hue is enabled.",
                    Current = config.GetBindable<float>(OsuSetting.CustomUIHue),
                })
                {
                    Keywords = new[] { @"hue", @"colour", @"color", @"tint" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Separate accent hue",
                    HintText = "Use a different hue for highlights, hovers and accents.",
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIAccentEnabled),
                })
                {
                    Keywords = new[] { @"accent", @"hue", @"highlight", @"hover" },
                },
                new SettingsItemV2(new FormHuePicker
                {
                    Caption = "Accent hue",
                    HintText = "Hue applied to highlights, hovers and accent colours.",
                    Current = config.GetBindable<float>(OsuSetting.CustomUIAccentHue),
                })
                {
                    Keywords = new[] { @"accent", @"hue", @"highlight" },
                },
            };
        }
    }
}
