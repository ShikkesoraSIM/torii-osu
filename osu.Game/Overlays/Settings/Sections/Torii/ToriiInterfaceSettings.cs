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
                new SettingsItemV2(new FormEnumDropdown<ToriiInputAudioHzMode>
                {
                    Caption = "Input/audio thread rate",
                    HintText = "How fast the input, audio and update threads run. Higher rates suit high-polling-rate mice (e.g. 8000 Hz) but cost more CPU. 2000 Hz is a safe default. Applies instantly.",
                    Current = config.GetBindable<ToriiInputAudioHzMode>(OsuSetting.ToriiInputAudioHz),
                })
                {
                    Keywords = new[] { @"hz", @"polling", @"rate", @"input", @"audio", @"thread", @"latency", @"8000", @"performance" },
                },
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

            // Master "Custom UI hue" toggle drives all three per-scope apply
            // flags (menu / overlays / settings panel) in lock-step: master ON
            // forces all three ON, master OFF forces all three OFF. Without this
            // the menu/toolbar never tints (its flag defaults off) and stale
            // configs can end up inconsistent. Schedule() defers off the BDL
            // thread because the cascade animates hue-tinted drawables, which
            // the framework only allows to mutate from the load/update threads.
            var customUiHueEnabled = config.GetBindable<bool>(OsuSetting.CustomUIHueEnabled);
            var applyToMenu = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToMenu);
            var applyToOverlays = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToOverlays);
            var applyToSettings = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToSettingsPanel);

            customUiHueEnabled.BindValueChanged(e => Schedule(() =>
            {
                applyToMenu.Value = e.NewValue;
                applyToOverlays.Value = e.NewValue;
                applyToSettings.Value = e.NewValue;
            }), true);
        }
    }
}
