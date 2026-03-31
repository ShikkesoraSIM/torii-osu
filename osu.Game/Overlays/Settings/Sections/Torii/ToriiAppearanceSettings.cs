// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    public partial class ToriiAppearanceSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Appearance";

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.EnableCustomUIHue,
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIHueEnabled),
                })
                {
                    Keywords = new[] { "torii", "colour", "color", "hue", "theme", "accent", "ui" },
                },
                new SettingsItemV2(new FormHuePicker
                {
                    Caption = UserInterfaceStrings.CustomUIHue,
                    Current = config.GetBindable<float>(OsuSetting.CustomUIHue),
                    HintText = UserInterfaceStrings.CustomUIHue,
                })
                {
                    Keywords = new[] { "torii", "colour", "color", "hue", "theme", "accent", "ui" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.ApplyHueToMenu,
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToMenu),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.ApplyHueToOverlays,
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToOverlays),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.ApplyHueToSettingsPanel,
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToSettingsPanel),
                }),
            };
        }
    }
}
