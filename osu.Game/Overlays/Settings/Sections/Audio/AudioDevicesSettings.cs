// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
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
        private FrameworkConfigManager frameworkConfig { get; set; } = null!;

        private SettingsDropdown<string> dropdown = null!;
        private Bindable<bool>? useExperimentalWasapi;

        private const string wasapi_exclusive_prefix = "WASAPI Exclusive: ";
        private const string wasapi_shared_prefix = "WASAPI Shared: ";

        [BackgroundDependencyLoader]
        private void load()
        {
            useExperimentalWasapi = frameworkConfig.GetBindable<bool>(FrameworkSetting.AudioUseExperimentalWasapi);

            Children = new Drawable[]
            {
                dropdown = new AudioDeviceSettingsDropdown
                {
                    LabelText = AudioSettingsStrings.OutputDevice,
                    Keywords = new[] { "speaker", "headphone", "output" }
                },
                new SettingsItemV2(new WasapiExperimentalControl
                {
                    Caption = AudioSettingsStrings.WasapiLabel,
                    HintText = AudioSettingsStrings.WasapiTooltip,
                    NoticeText = AudioSettingsStrings.WasapiNotice,
                    Current = useExperimentalWasapi,
                })
                {
                    Keywords = new[] { "wasapi", "low latency", "experimental", "audio mode" },
                    CanBeShown = { Value = RuntimeInfo.OS == RuntimeInfo.Platform.Windows },
                },
            };

            audio.OnNewDevice += onDeviceChanged;
            audio.OnLostDevice += onDeviceChanged;
            dropdown.Current = audio.AudioDevice;

            if (useExperimentalWasapi != null)
                useExperimentalWasapi.BindValueChanged(_ => onExperimentalModeChanged(), true);

            onDeviceChanged(string.Empty);
        }

        private void onDeviceChanged(string _)
        {
            updateItems();
        }

        private void updateItems()
        {
            var deviceItems = new List<string> { string.Empty };
            var baseDeviceNames = audio.AudioDeviceNames.Where(name => name.IsNotNull()).Distinct().ToList();
            deviceItems.AddRange(baseDeviceNames);

            if (isWasapiModeAvailable())
            {
                foreach (string deviceName in baseDeviceNames.Where(canCreateWasapiVariant))
                {
                    deviceItems.Add($"{wasapi_shared_prefix}{deviceName}");
                    deviceItems.Add($"{wasapi_exclusive_prefix}{deviceName}");
                }
            }

            string preferredDeviceName = audio.AudioDevice.Value;
            if (deviceItems.All(kv => kv != preferredDeviceName))
                deviceItems.Add(preferredDeviceName);

            dropdown.Items = deviceItems
                             .Where(i => i.IsNotNull())
                             .Distinct()
                             .ToList();
        }

        private void onExperimentalModeChanged()
        {
            normalizeCurrentDeviceForMode();
            updateItems();
        }

        private void normalizeCurrentDeviceForMode()
        {
            string currentDevice = audio.AudioDevice.Value;

            if (!isWasapiModeAvailable())
            {
                if (string.IsNullOrEmpty(currentDevice))
                    return;

                if (tryStripWasapiPrefix(currentDevice, out string stripped))
                    dropdown.Current.Value = stripped;
                return;
            }

            if (string.IsNullOrEmpty(currentDevice))
            {
                string fallbackDevice = audio.AudioDeviceNames
                                             .Where(canCreateWasapiVariant)
                                             .FirstOrDefault();

                if (!string.IsNullOrEmpty(fallbackDevice))
                    dropdown.Current.Value = $"{wasapi_shared_prefix}{fallbackDevice}";

                return;
            }

            if (isWasapiDevice(currentDevice))
                return;

            if (canCreateWasapiVariant(currentDevice))
                dropdown.Current.Value = $"{wasapi_shared_prefix}{currentDevice}";
        }

        private bool isWasapiModeAvailable() =>
            RuntimeInfo.OS == RuntimeInfo.Platform.Windows && useExperimentalWasapi?.Value == true;

        private static bool canCreateWasapiVariant(string deviceName) =>
            !string.IsNullOrEmpty(deviceName)
            && !deviceName.StartsWith("ASIO: ", StringComparison.Ordinal)
            && !isWasapiDevice(deviceName);

        private static bool isWasapiDevice(string deviceName) =>
            deviceName.StartsWith(wasapi_shared_prefix, StringComparison.Ordinal)
            || deviceName.StartsWith(wasapi_exclusive_prefix, StringComparison.Ordinal);

        private static bool tryStripWasapiPrefix(string deviceName, out string stripped)
        {
            if (deviceName.StartsWith(wasapi_shared_prefix, StringComparison.Ordinal))
            {
                stripped = deviceName[wasapi_shared_prefix.Length..];
                return true;
            }

            if (deviceName.StartsWith(wasapi_exclusive_prefix, StringComparison.Ordinal))
            {
                stripped = deviceName[wasapi_exclusive_prefix.Length..];
                return true;
            }

            stripped = deviceName;
            return false;
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

        private partial class AudioDeviceSettingsDropdown : SettingsDropdown<string>
        {
            protected override OsuDropdown<string> CreateDropdown() => new AudioDeviceDropdownControl();

            private partial class AudioDeviceDropdownControl : DropdownControl
            {
                protected override LocalisableString GenerateItemText(string item)
                    => string.IsNullOrEmpty(item) ? CommonStrings.Default : base.GenerateItemText(item);
            }
        }

        private partial class WasapiExperimentalControl : CompositeDrawable, IHasCurrentValue<bool>, IFormControl
        {
            public Bindable<bool> Current
            {
                get => current.Current;
                set => current.Current = value;
            }

            private readonly BindableWithCurrent<bool> current = new BindableWithCurrent<bool>();

            public LocalisableString Caption { get; init; }

            public LocalisableString HintText { get; init; }

            public LocalisableString NoticeText { get; init; }

            private FormCheckBox checkbox = null!;
            private OsuTextFlowContainer noticeTextFlow = null!;

            [Resolved]
            private OsuColour colours { get; set; } = null!;

            [BackgroundDependencyLoader]
            private void load()
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;

                InternalChild = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Children = new Drawable[]
                    {
                        checkbox = new FormCheckBox
                        {
                            Caption = Caption,
                            HintText = HintText,
                            Current = Current,
                        },
                        noticeTextFlow = new OsuTextFlowContainer(t =>
                        {
                            t.Font = OsuFont.Style.Caption1;
                            t.Colour = colours.Yellow;
                        })
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Padding = new MarginPadding
                            {
                                Left = 12,
                                Right = 12,
                                Top = 6,
                            },
                        },
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                current.BindValueChanged(_ =>
                {
                    updateNotice();
                    ValueChanged?.Invoke();
                }, true);
            }

            private void updateNotice()
            {
                noticeTextFlow.Text = current.Value ? NoticeText : string.Empty;
            }

            public IEnumerable<LocalisableString> FilterTerms => new[] { Caption };

            public event Action? ValueChanged;

            public bool IsDefault => checkbox.IsDefault;

            public void SetDefault() => checkbox.SetDefault();

            public bool IsDisabled => checkbox.IsDisabled;

            public float MainDrawHeight => checkbox.MainDrawHeight;
        }
    }
}
