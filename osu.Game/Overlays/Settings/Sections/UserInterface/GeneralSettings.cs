// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;

namespace osu.Game.Overlays.Settings.Sections.UserInterface
{
    public partial class GeneralSettings : SettingsSubsection
    {
        protected override LocalisableString Header => CommonStrings.General;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.CursorRotation,
                    Current = config.GetBindable<bool>(OsuSetting.CursorRotation)
                })
                {
                    Keywords = [@"spin"],
                },
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = UserInterfaceStrings.MenuCursorSize,
                    Current = config.GetBindable<float>(OsuSetting.MenuCursorSize),
                    KeyboardStep = 0.01f,
                    LabelFormat = v => $"{v:0.##}x"
                }),
                // Torii: three-way menu cursor style.
                //   LazerDefault → upstream menu cursor (the textured
                //                  arrow with the additive flash).
                //   SkinCursor   → user's skin gameplay cursor
                //                  (cursor.png + cursormiddle.png),
                //                  same pipeline as the playfield —
                //                  switches live when skin changes,
                //                  scales with GameplayCursorSize.
                //   ToriiCursor  → translucent pink ring with white
                //                  centre dot, scaled with
                //                  GameplayCursorSize. Used regardless
                //                  of skin (overrides skin cursor).
                // All three honour the click-feel hotkey
                // (Ctrl+Shift+wheel) and the GameplayCursorSize
                // bindable end-to-end.
                new SettingsItemV2(new FormEnumDropdown<osu.Game.Graphics.Cursor.MenuCursorStyle>
                {
                    Caption = UserInterfaceStrings.MenuCursorStyle,
                    Current = config.GetBindable<osu.Game.Graphics.Cursor.MenuCursorStyle>(OsuSetting.MenuCursorStyle),
                })
                {
                    Keywords = [@"cursor", @"gameplay", @"skin", @"torii"],
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.Parallax,
                    Current = config.GetBindable<bool>(OsuSetting.MenuParallax)
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.EnableCustomUIHue,
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIHueEnabled),
                })
                {
                    Keywords = [@"colour", @"color", @"hue", @"theme", @"accent"],
                },
                new SettingsItemV2(new FormHuePicker
                {
                    Caption = UserInterfaceStrings.CustomUIHue,
                    Current = config.GetBindable<float>(OsuSetting.CustomUIHue),
                    HintText = UserInterfaceStrings.CustomUIHue,
                })
                {
                    Keywords = [@"colour", @"color", @"hue", @"theme", @"accent"],
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
                new SettingsItemV2(new FormSliderBar<double>
                {
                    Caption = UserInterfaceStrings.HoldToConfirmActivationTime,
                    Current = config.GetBindable<double>(OsuSetting.UIHoldActivationDelay),
                    KeyboardStep = 50,
                    LabelFormat = v => $"{v:N0} ms",
                })
                {
                    Keywords = [@"delay"],
                    ApplyClassicDefault = c => ((IHasCurrentValue<double>)c).Current.Value = 0,
                },
            };
        }
    }
}
