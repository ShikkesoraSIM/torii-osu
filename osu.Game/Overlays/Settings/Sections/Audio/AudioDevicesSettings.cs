// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;
using osu.Framework;
using osu.Framework.Bindables;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Configuration;
using osu.Game.Localisation;

namespace osu.Game.Overlays.Settings.Sections.Audio
{
    public partial class AudioDevicesSettings : SettingsSubsection
    {
        protected override LocalisableString Header => AudioSettingsStrings.AudioDevicesHeader;

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        private AudioDeviceDropdown dropdown = null!;

        private FormCheckBox? legacyAudio;
        private FormCheckBox? exclusiveAudio;

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

                Add(new SettingsItemV2(exclusiveAudio = new ExclusiveAudioCheckbox())
                {
                    Keywords = new[] { "wasapi", "latency", "exclusive" },
                });

                legacyAudio.Current.ValueChanged += _ => onDeviceChanged(string.Empty);

                // el modo exclusivo va sobre wasapi: con el motor legacy prendido no hay
                // nada que hacer, asi que se muestra deshabilitado en vez de mentir.
                legacyAudio.Current.BindValueChanged(legacy => exclusiveAudio.Current.Disabled = legacy.NewValue, true);
            }

            audio.OnNewDevice += onDeviceChanged;
            audio.OnLostDevice += onDeviceChanged;
            dropdown.Current = audio.AudioDevice;

            onDeviceChanged(string.Empty);
        }

        private void onDeviceChanged(string _) => Scheduler.AddOnce(updateItems);

        private void updateItems()
        {
            var deviceItems = new List<string> { string.Empty };
            deviceItems.AddRange(audio.AudioDeviceNames);

            string preferredDeviceName = audio.AudioDevice.Value;
            if (deviceItems.All(kv => kv != preferredDeviceName))
                deviceItems.Add(preferredDeviceName);

            // The option dropdown for audio device selection lists all audio
            // device names. Dropdowns, however, may not have multiple identical
            // keys. Thus, we remove duplicate audio device names from
            // the dropdown. BASS does not give us a simple mechanism to select
            // specific audio devices in such a case anyways. Such
            // functionality would require involved OS-specific code.
            dropdown.Items = deviceItems
                             // Dropdown doesn't like null items. Somehow we are seeing some arrive here (see https://github.com/ppy/osu/issues/21271)
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

    /// <summary>
    /// torii: exclusive WASAPI. Enabling it warns first, because it silences every other
    /// application on the device and that surprises people mid-call.
    /// </summary>
    public partial class ExclusiveAudioCheckbox : FormCheckBox
    {
        private Bindable<bool> configExclusiveAudio = null!;

        [Resolved]
        private IDialogOverlay? dialogOverlay { get; set; }

        public ExclusiveAudioCheckbox()
        {
            Caption = AudioSettingsStrings.ExclusiveAudioLabel;
            HintText = AudioSettingsStrings.ExclusiveAudioTooltip;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            configExclusiveAudio = audio.UseExclusiveWasapi.GetBoundCopy();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Current.Value = configExclusiveAudio.Value;

            Current.BindValueChanged(enabled =>
            {
                if (!enabled.NewValue)
                {
                    configExclusiveAudio.Value = false;
                    return;
                }

                if (dialogOverlay == null)
                {
                    configExclusiveAudio.Value = true;
                    return;
                }

                dialogOverlay.Push(new ExclusiveAudioWarningDialog(
                    () => configExclusiveAudio.Value = true,
                    // volver el visto atras sin re-disparar el dialogo
                    () => Schedule(() => Current.Value = false)));
            });

            // el framework puede apagarlo solo si el dispositivo no lo acepta.
            configExclusiveAudio.BindValueChanged(exclusive =>
            {
                if (!exclusive.NewValue)
                    Current.Value = false;
            });
        }
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

            // Manual two-way binding because we're inverting what the framework exposes.
            Current.ValueChanged += legacy =>
            {
                configExperimentalAudio.Value = !legacy.NewValue;
            };

            configExperimentalAudio.BindValueChanged(experimental =>
            {
                Current.Value = !experimental.NewValue;
            }, true);
        }
    }
}
