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
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;

namespace osu.Game.Overlays.Settings.Sections.Audio
{
    public partial class AudioDevicesSettings : SettingsSubsection
    {
        protected override LocalisableString Header => AudioSettingsStrings.AudioDevicesHeader;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private AudioDeviceDropdown dropdown = null!;

        private LegacyAudioCheckbox? legacyAudio;

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
                Add(new SettingsItemV2(legacyAudio = new LegacyAudioCheckbox())
                {
                    Keywords = new[] { "wasapi", "latency", "exclusive", "legacy", "experimental" },
                });

                legacyAudio.Current.ValueChanged += _ => onDeviceChanged(string.Empty);
            }

            // Android-only: low-latency audio via Google's Oboe library.
            // Gated on platform so Desktop / iOS users never see this row.
            // Hot-swap supported — toggling starts/stops the bridge live; no
            // restart required (OsuGameBase binds the setting and dispatches
            // start/stop accordingly).
            if (RuntimeInfo.OS == RuntimeInfo.Platform.Android)
            {
                Add(new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Low-latency audio (Oboe)",
                    HintText = "Routes audio through Google's Oboe library for AAudio MMAP-exclusive output. "
                               + "Cuts latency from ~60–200 ms to ~15–30 ms on supported devices, with OpenSL ES fallback. "
                               + "Disable if your device misbehaves (Samsung security policies blocking dlopen, very old hardware). "
                               + "Toggling applies immediately.",
                    Current = config.GetBindable<bool>(OsuSetting.EnableOboeAudio),
                })
                {
                    Keywords = new[] { "oboe", "android", "latency", "aaudio", "mmap", "low latency" },
                });
            }

            audio.OnNewDevice += onDeviceChanged;
            audio.OnLostDevice += onDeviceChanged;
            dropdown.Current = audio.AudioDevice;

            onDeviceChanged(string.Empty);
        }

        private void onDeviceChanged(string _)
        {
            updateItems();
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

        public partial class LegacyAudioCheckbox : FormCheckBox
        {
            private Bindable<bool> configExperimentalAudio = null!;

            public LegacyAudioCheckbox()
            {
                Caption = AudioSettingsStrings.LegacyAudioLabel;
                HintText = AudioSettingsStrings.LegacyAudioTooltip;
            }

            [BackgroundDependencyLoader]
            private void load(AudioManager audio)
            {
                configExperimentalAudio = audio.UseExperimentalWasapi.GetBoundCopy();
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // Manual two-way binding because we invert what the framework exposes:
                // this checkbox means "use LEGACY audio", the opposite of "use experimental WASAPI".
                Current.ValueChanged += legacy => configExperimentalAudio.Value = !legacy.NewValue;
                configExperimentalAudio.BindValueChanged(experimental => Current.Value = !experimental.NewValue, true);
            }
        }
    }
}
