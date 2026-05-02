// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    public partial class ToriiInterfaceSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Interface";

        [Resolved(CanBeNull = true)]
        private IAPIProvider? api { get; set; }

        // The accent picker + its enable toggle are inserted at LoadComplete
        // when the local user is a current/past supporter. Keeping refs lets
        // us flip visibility live in case the user re-logs into a different
        // account inside the same session.
        private SettingsItemV2? accentEnableItem;
        private SettingsItemV2? accentPickerItem;
        private IBindable<APIUser>? localUser;

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
                    Keywords = new[] { @"colour", @"color", @"hue", @"theme", @"accent", @"torii" },
                },
                new SettingsItemV2(new FormHuePicker
                {
                    Caption = UserInterfaceStrings.CustomUIHue,
                    Current = config.GetBindable<float>(OsuSetting.CustomUIHue),
                    HintText = UserInterfaceStrings.CustomUIHue,
                })
                {
                    Keywords = new[] { @"colour", @"color", @"hue", @"theme", @"accent", @"torii" },
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

            // Donator-only accent picker. Built but kept hidden when the
            // user isn't a supporter — that way logging in mid-session
            // reveals it without needing a settings panel reopen, and we
            // don't have to deal with rebuilding the Children collection.
            accentEnableItem = new SettingsItemV2(new FormCheckBox
            {
                Caption = "Use a separate accent hue (Supporter)",
                Current = config.GetBindable<bool>(OsuSetting.CustomUIAccentEnabled),
            })
            {
                Alpha = 0,
                Keywords = new[] { @"colour", @"color", @"hue", @"accent", @"supporter", @"donator" },
            };
            accentPickerItem = new SettingsItemV2(new FormHuePicker
            {
                Caption = "Accent hue (Supporter)",
                Current = config.GetBindable<float>(OsuSetting.CustomUIAccentHue),
                HintText = "Hue applied to highlights, hovers and accent colours",
            })
            {
                Alpha = 0,
                Keywords = new[] { @"colour", @"color", @"hue", @"accent", @"supporter", @"donator" },
            };
            Add(accentEnableItem);
            Add(accentPickerItem);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (api == null)
            {
                hideAccentControls();
                return;
            }

            localUser = api.LocalUser.GetBoundCopy();
            localUser.BindValueChanged(_ => updateSupporterVisibility(), true);
        }

        private void updateSupporterVisibility()
        {
            // "Donator" tier = current OR past supporter. The server exposes
            // both via APIUser.IsSupporter (current tag) and HasSupported
            // (true if they ever subscribed). Either grants access to the
            // cosmetic accent picker; otherwise we hide it.
            var user = localUser?.Value;
            bool donator = user != null && (user.IsSupporter || user.HasSupported);

            if (donator)
                showAccentControls();
            else
                hideAccentControls();
        }

        private void showAccentControls()
        {
            if (accentEnableItem != null)
            {
                accentEnableItem.Alpha = 1;
                accentEnableItem.AlwaysPresent = false;
            }

            if (accentPickerItem != null)
            {
                accentPickerItem.Alpha = 1;
                accentPickerItem.AlwaysPresent = false;
            }
        }

        private void hideAccentControls()
        {
            if (accentEnableItem != null)
            {
                accentEnableItem.Alpha = 0;
                accentEnableItem.AlwaysPresent = false;
            }

            if (accentPickerItem != null)
            {
                accentPickerItem.Alpha = 0;
                accentPickerItem.AlwaysPresent = false;
            }
        }

        public override IEnumerable<LocalisableString> FilterTerms => base.FilterTerms;
    }
}
