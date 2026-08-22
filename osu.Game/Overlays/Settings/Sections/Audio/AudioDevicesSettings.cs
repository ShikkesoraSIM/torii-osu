// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Graphics;
using System.Collections.Generic;
using System.Linq;
using osu.Framework;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Configuration;
using osu.Game.Localisation;
using osu.Game.Overlays.Notifications;

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
                // nada que hacer, asi que se muestra deshabilitado en vez de mentir. y si
                // alguien vuelve al motor legacy teniendolo prendido, se apaga: dejarlo
                // marcado sin efecto es peor que sacarlo.
                legacyAudio.Current.BindValueChanged(legacy =>
                {
                    if (legacy.NewValue)
                        audio.UseExclusiveWasapi.Value = false;

                    exclusiveAudio.Current.Disabled = legacy.NewValue;
                }, true);
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

        [Resolved(canBeNull: true)]
        private INotificationOverlay? notifications { get; set; }

        /// <summary>Set while we're waiting to see whether the device actually took it.</summary>
        private bool awaitingResult;

        public ExclusiveAudioCheckbox()
        {
            Caption = AudioSettingsStrings.ExclusiveAudioLabel;
            HintText = AudioSettingsStrings.ExclusiveAudioTooltip;
            NewFeatureId = NewFeatureRegistry.WasapiExclusive;
        }

        [BackgroundDependencyLoader]
        private void load(AudioManager audio)
        {
            configExclusiveAudio = audio.UseExclusiveWasapi.GetBoundCopy();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            writeState(configExclusiveAudio.Value);

            Current.BindValueChanged(enabled =>
            {
                if (!enabled.NewValue)
                {
                    configExclusiveAudio.Value = false;
                    return;
                }

                if (dialogOverlay == null)
                {
                    enable();
                    return;
                }

                dialogOverlay.Push(new ExclusiveAudioWarningDialog(
                    enable,
                    // volver el visto atras sin re-disparar el dialogo
                    () => Schedule(() => writeState(false))));
            });

            // el framework lo apaga solo cuando el dispositivo no lo acepta o cuando otra
            // aplicacion ya lo tiene agarrado. ese aviso llega desde el audio thread, asi
            // que sin el Schedule tocar el visto desde ahi revienta con
            // InvalidThreadForMutation.
            configExclusiveAudio.BindValueChanged(exclusive =>
            {
                if (exclusive.NewValue)
                    return;

                Schedule(() =>
                {
                    writeState(false);

                    if (!awaitingResult)
                        return;

                    awaitingResult = false;
                    notifications?.Post(new SimpleErrorNotification
                    {
                        Text = "Exclusive mode didn't work with this audio device, so it stayed off. "
                               + "Some devices don't support it, and it also fails if another app already grabbed the device.",
                    });
                });
            });

            void enable()
            {
                // si el dispositivo lo rechaza, el framework devuelve esto a false y ahi
                // recien sabemos que no anduvo.
                awaitingResult = true;
                configExclusiveAudio.Value = true;

                Scheduler.AddDelayed(() => awaitingResult = false, 3000);
            }
        }

        /// <summary>
        /// Writes the checkbox state from code. Goes around <see cref="Bindable{T}.Disabled"/>
        /// on purpose: the item is disabled while the legacy engine is on, but we still have
        /// to reflect what the config actually says instead of throwing.
        /// </summary>
        private void writeState(bool value)
        {
            bool wasDisabled = Current.Disabled;

            Current.Disabled = false;
            Current.Value = value;
            Current.Disabled = wasDisabled;
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
                // torii: el framework apaga esto solo cuando no puede inicializar el
                // dispositivo, y avisa desde el audio thread. tocar el visto ahi mismo
                // revienta con InvalidThreadForMutation, o sea que justo cuando el audio
                // falla se cae el juego entero. hay que volver al update thread.
                if (ThreadSafety.IsUpdateThread)
                    Current.Value = !experimental.NewValue;
                else
                    Schedule(() => Current.Value = !experimental.NewValue);
            }, true);
        }
    }
}
