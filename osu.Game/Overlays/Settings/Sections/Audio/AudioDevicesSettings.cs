// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;

namespace osu.Game.Overlays.Settings.Sections.Audio
{
    public partial class AudioDevicesSettings : SettingsSubsection
    {
        protected override LocalisableString Header => AudioSettingsStrings.AudioDevicesHeader;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        private AudioDeviceDropdown dropdown = null!;

        private FormCheckBox? wasapiExperimental;

        private readonly Bindable<SettingsNote.Data?> wasapiExperimentalNote = new Bindable<SettingsNote.Data?>();

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                new SettingsItemV2(dropdown = new AudioDeviceDropdown
                {
                    Caption = AudioSettingsStrings.OutputDevice,
                })
                {
                    Keywords = new[] { "speaker", "headphone", "output" }
                },
            };

            if (RuntimeInfo.OS == RuntimeInfo.Platform.Windows)
            {
                Add(new SettingsItemV2(wasapiExperimental = new FormCheckBox
                {
                    Caption = AudioSettingsStrings.WasapiLabel,
                    HintText = AudioSettingsStrings.WasapiTooltip,
                    Current = audio.UseExperimentalWasapi,
                })
                {
                    Keywords = new[] { "wasapi", "latency", "exclusive" },
                    Note = { BindTarget = wasapiExperimentalNote },
                });

                wasapiExperimental.Current.ValueChanged += _ => onDeviceChanged(string.Empty);
            }

            audio.OnNewDevice += onDeviceChanged;
            audio.OnLostDevice += onDeviceChanged;
            dropdown.Current = audio.AudioDevice;

            onDeviceChanged(string.Empty);
        }

        private void onDeviceChanged(string _)
        {
            updateItems();

            if (wasapiExperimental != null)
                wasapiExperimentalNote.Value = wasapiExperimental.Current.Value
                    ? new SettingsNote.Data(AudioSettingsStrings.WasapiNotice, SettingsNote.Type.Warning)
                    : null;
        }

        private void updateItems()
        {
            var deviceItems = new List<string> { string.Empty };
            deviceItems.AddRange(audio.AudioDeviceNames);

            string preferredDeviceName = audio.AudioDevice.Value;

            // If a previous Torii session saved a WASAPI-prefixed device name
            // (e.g. "WASAPI Shared: Headphones") and that device is not present
            // in the current enumeration, reset to the system default.
            // This keeps shared configs compatible after moving back to the
            // upstream experimental-WASAPI behaviour.
            if (!string.IsNullOrEmpty(preferredDeviceName) &&
                (preferredDeviceName.StartsWith("WASAPI Shared:", System.StringComparison.OrdinalIgnoreCase) ||
                 preferredDeviceName.StartsWith("WASAPI Exclusive:", System.StringComparison.OrdinalIgnoreCase)) &&
                deviceItems.All(kv => kv != preferredDeviceName))
            {
                audio.AudioDevice.Value = string.Empty;
                preferredDeviceName = string.Empty;
            }

            if (deviceItems.All(kv => kv != preferredDeviceName))
                deviceItems.Add(preferredDeviceName);

            dropdown.Items = deviceItems
                             .Where(i => i.IsNotNull())
                             .Distinct()
                             .ToList();
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (audio.IsNotNull())
            {
                audio.OnNewDevice -= onDeviceChanged;
                audio.OnLostDevice -= onDeviceChanged;
            }
        }

        private partial class AudioDeviceDropdown : FormDropdown<string>
        {
            protected override LocalisableString GenerateItemText(string item)
                => string.IsNullOrEmpty(item) ? CommonStrings.Default : base.GenerateItemText(item);
        }
    }
}
