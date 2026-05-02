// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Text.RegularExpressions;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    public partial class ToriiServerSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Server";

        private static readonly (string Name, string Url)[] presets =
        {
            ("Torii", "lazer-api.shikkesora.com"),
            ("g0v0", "lazer-api.g0v0.top"),
            ("vipsu", "gamerherz.ddns.net"),
            ("m1pp", "lazer-api.m1pposu.dev"),
        };

        [Resolved(CanBeNull = true)]
        private IAPIProvider? api { get; set; }

        // Cached so we can force-flush the value to disk on every successful
        // change. The framework's bindable change-listener already queues a
        // 100ms debounced save, but if the client is closed inside that window
        // (or hard-crashes mid-debounce) the URL never reaches the .ini file
        // and the user sees the textbox revert to the default after restart.
        // Calling Save() explicitly here makes the persistence eager so the
        // change is durable the moment validation passes.
        private OsuConfigManager localConfig = null!;

        private SettingsTextBox serverUrlTextBox = null!;
        private string lastApiUrl = string.Empty;
        private bool isInitialLoad = true;
        private bool isProgrammaticApiUrlUpdate;
        private ScheduledDelegate? pendingValidation;
        private const double debounce_delay = 500;

        private static readonly Regex hostPortPattern = new Regex(
            @"^(?:(?:(?:[A-Za-z0-9-]+)\.)+[A-Za-z0-9-]+|(?:(?:25[0-5]|2[0-4]\d|1?\d{1,2})\.){3}(?:25[0-5]|2[0-4]\d|1?\d{1,2}))(?::(?<port>\d{1,5}))?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            localConfig = config;
            var serverUrl = config.GetBindable<string>(OsuSetting.CustomApiUrl);

            Children = new Drawable[]
            {
                serverUrlTextBox = new SettingsTextBox
                {
                    LabelText = "API server URL",
                    Current = serverUrl,
                    Keywords = new[] { "torii", "server", "api", "url", "host", "custom" },
                },
                new ServerPresetRow(serverUrl, presets),
            };

            serverUrlTextBox.Current.BindValueChanged(onCustomApiUrlChanged, true);
        }

        private void onCustomApiUrlChanged(ValueChangedEvent<string> e)
        {
            if (isProgrammaticApiUrlUpdate)
                return;

            if (isInitialLoad)
            {
                var initRaw = (e.NewValue ?? string.Empty).Trim();
                lastApiUrl = normalizeToHttps(initRaw);
                isInitialLoad = false;
                return;
            }

            pendingValidation?.Cancel();
            pendingValidation = Scheduler.AddDelayed(() =>
            {
                string rawInput = (serverUrlTextBox.Current.Value ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(rawInput))
                {
                    serverUrlTextBox.SetNoticeText(OnlineSettingsStrings.CustomApiUrlOfficialBlocked, true);
                    restoreLastApiUrlInTextBox();
                    return;
                }

                string hostPort = stripSchemeAndPath(rawInput);
                if (!isValidHostPort(hostPort))
                {
                    serverUrlTextBox.SetNoticeText(OnlineSettingsStrings.CustomApiUrlInvalid, true);
                    return;
                }

                if (APIAccess.IsUnsafeOfficialHost(hostPort))
                {
                    serverUrlTextBox.SetNoticeText(OnlineSettingsStrings.CustomApiUrlOfficialBlocked, true);
                    restoreLastApiUrlInTextBox();
                    return;
                }

                if (!string.Equals(rawInput, hostPort, StringComparison.Ordinal))
                {
                    isProgrammaticApiUrlUpdate = true;
                    serverUrlTextBox.Current.Value = hostPort;
                    isProgrammaticApiUrlUpdate = false;
                }

                string normalised = "https://" + hostPort;
                maybeApplyRuntimeEndpointIfChanged(normalised);
            }, debounce_delay);
        }

        private static bool isValidHostPort(string hostPort)
        {
            var match = hostPortPattern.Match(hostPort);
            if (!match.Success)
                return false;

            var portGroup = match.Groups["port"];
            if (portGroup.Success)
            {
                if (!int.TryParse(portGroup.Value, out int port))
                    return false;

                if (port is < 1 or > 65535)
                    return false;
            }

            return true;
        }

        private static string normalizeToHttps(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return string.Empty;

            string hostPort = stripSchemeAndPath(raw);
            return isValidHostPort(hostPort) ? "https://" + hostPort : hostPort;
        }

        private static string stripSchemeAndPath(string input)
        {
            string value = Regex.Replace(input, @"^\s*https?://", "", RegexOptions.IgnoreCase);

            int slashIndex = value.IndexOf('/');
            if (slashIndex >= 0)
                value = value[..slashIndex];

            return value.TrimEnd('/');
        }

        private void maybeApplyRuntimeEndpointIfChanged(string normalizedNewValue)
        {
            if (string.Equals(lastApiUrl, normalizedNewValue, StringComparison.OrdinalIgnoreCase))
            {
                serverUrlTextBox.SetNoticeText(string.Empty, false);
                return;
            }

            lastApiUrl = normalizedNewValue;

            // Force-flush the new value to disk immediately. The bindable
            // value-changed handler already queues a debounced save, but if
            // the user closes the client during that debounce window the URL
            // is silently lost and the textbox reverts to the default on
            // next launch — exactly the symptom Impairation reported.
            localConfig?.Save();

            if (api is APIAccess apiAccess)
            {
                if (apiAccess.ApplyRuntimeEndpointConfiguration())
                    serverUrlTextBox.SetNoticeText(OnlineSettingsStrings.CustomApiUrlAppliedWithoutRestart, false);
                else
                    serverUrlTextBox.SetNoticeText(string.Empty, false);

                return;
            }

            serverUrlTextBox.SetNoticeText(OnlineSettingsStrings.CustomApiUrlRestartRequired, false);
        }

        private void restoreLastApiUrlInTextBox()
        {
            if (string.IsNullOrWhiteSpace(lastApiUrl))
                return;

            string hostPort = stripSchemeAndPath(lastApiUrl);
            isProgrammaticApiUrlUpdate = true;
            serverUrlTextBox.Current.Value = hostPort;
            isProgrammaticApiUrlUpdate = false;
        }

        private partial class ServerPresetRow : CompositeDrawable
        {
            private readonly Bindable<string> serverUrl;
            private readonly (string Name, string Url)[] presets;
            private readonly ServerPresetButton[] buttons;

            public ServerPresetRow(Bindable<string> serverUrl, (string Name, string Url)[] presets)
            {
                this.serverUrl = serverUrl;
                this.presets = presets;
                buttons = new ServerPresetButton[presets.Length];

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var buttonFlow = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(8, 8),
                };

                for (int i = 0; i < presets.Length; i++)
                {
                    int index = i;
                    buttons[i] = new ServerPresetButton(presets[i].Name)
                    {
                        Action = () => serverUrl.Value = presets[index].Url,
                    };
                    buttonFlow.Add(buttons[i]);
                }

                InternalChild = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 8),
                    Padding = new MarginPadding { Horizontal = 20, Vertical = 12 },
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = "Popular Torii-compatible servers",
                            Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                            Colour = Color4.White.Opacity(0.62f),
                        },
                        buttonFlow,
                    },
                };
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                serverUrl.BindValueChanged(updateButtons, true);
            }

            private void updateButtons(ValueChangedEvent<string> e)
            {
                for (int i = 0; i < presets.Length; i++)
                    buttons[i].SetActive(string.Equals(e.NewValue?.Trim(), presets[i].Url, StringComparison.OrdinalIgnoreCase));
            }
        }

        private partial class ServerPresetButton : ClickableContainer
        {
            private static readonly Color4 active_colour = Color4Extensions.FromHex(@"69d7ff");
            private static readonly Color4 inactive_colour = Color4Extensions.FromHex(@"2d2940");
            private readonly Box background;
            private readonly OsuSpriteText label;
            private bool isActive;

            public ServerPresetButton(string name)
            {
                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 14;

                InternalChildren = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = inactive_colour,
                    },
                    label = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = name,
                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                        Colour = Color4.White.Opacity(0.74f),
                        Margin = new MarginPadding { Horizontal = 16, Vertical = 8 },
                    },
                };
            }

            public void SetActive(bool active)
            {
                isActive = active;
                applyVisualState(false);
            }

            protected override bool OnHover(HoverEvent e)
            {
                applyVisualState(true);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                base.OnHoverLost(e);
                applyVisualState(false);
            }

            private void applyVisualState(bool hovered)
            {
                var backgroundColour = isActive
                    ? active_colour.Opacity(hovered ? 0.30f : 0.22f)
                    : hovered ? active_colour.Opacity(0.12f) : inactive_colour;

                var textColour = isActive
                    ? active_colour
                    : hovered ? Color4.White : Color4.White.Opacity(0.74f);

                background.FadeColour(backgroundColour, 150, Easing.OutQuint);
                label.FadeColour(textColour, 150, Easing.OutQuint);
            }
        }
    }
}
