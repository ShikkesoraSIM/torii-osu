// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Text.RegularExpressions;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Online.API;


namespace osu.Game.Overlays.Settings.Sections.Online
{
    public partial class WebSettings : SettingsSubsection
    {
        protected override LocalisableString Header => OnlineSettingsStrings.WebHeader;

        [Resolved(CanBeNull = true)]
        private IAPIProvider? api { get; set; }

        private SettingsTextBox customApiUrlTextBox = null!;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = OnlineSettingsStrings.ExternalLinkWarning,
                    Current = config.GetBindable<bool>(OsuSetting.ExternalLinkWarning)
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = OnlineSettingsStrings.PreferNoVideo,
                    Current = config.GetBindable<bool>(OsuSetting.PreferNoVideo)
                })
                {
                    Keywords = new[] { "no-video" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = OnlineSettingsStrings.AutomaticallyDownloadMissingBeatmaps,
                    Current = config.GetBindable<bool>(OsuSetting.AutomaticallyDownloadMissingBeatmaps),
                })
                {
                    Keywords = new[] { "spectator", "replay" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = OnlineSettingsStrings.ShowExplicitContent,
                    Current = config.GetBindable<bool>(OsuSetting.ShowOnlineExplicitContent),
                })
                {
                    Keywords = new[] { "nsfw", "18+", "offensive" },
                },
                customApiUrlTextBox = new SettingsTextBox
                {
                    LabelText = OnlineSettingsStrings.CustomApiUrl,
                    Current = config.GetBindable<string>(OsuSetting.CustomApiUrl),
                }
            };

            customApiUrlTextBox.Current.BindValueChanged(onCustomApiUrlChanged, true);
        }

        private string lastApiUrl = string.Empty;
        private bool isInitialLoad = true;
        private bool isProgrammaticApiUrlUpdate;
        private ScheduledDelegate? pendingValidation;
        private const double debounce_delay = 500;

        // Validates a host[:port] input without scheme or path.
        private static readonly Regex hostPortPattern = new Regex(
            pattern:
                @"^(?:" +
                    @"(?:(?:[A-Za-z0-9-]+)\.)+[A-Za-z0-9-]+" +                                  // multi-level domain (at least one dot)
                @"|" +
                    @"(?:(?:25[0-5]|2[0-4]\d|1?\d{1,2})\.){3}(?:25[0-5]|2[0-4]\d|1?\d{1,2})" +   // IPv4 0-255
                @")(?::(?<port>\d{1,5}))?$",
            options: RegexOptions.Compiled | RegexOptions.CultureInvariant);

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
                string rawInput = (customApiUrlTextBox.Current.Value ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(rawInput))
                {
                    customApiUrlTextBox.SetNoticeText(OnlineSettingsStrings.CustomApiUrlOfficialBlocked, true);
                    restoreLastApiUrlInTextBox();
                    return;
                }

                string hostPort = stripSchemeAndPath(rawInput);
                if (!isValidHostPort(hostPort))
                {
                    customApiUrlTextBox.SetNoticeText(OnlineSettingsStrings.CustomApiUrlInvalid, true);
                    return;
                }

                if (APIAccess.IsUnsafeOfficialHost(hostPort))
                {
                    customApiUrlTextBox.SetNoticeText(OnlineSettingsStrings.CustomApiUrlOfficialBlocked, true);
                    restoreLastApiUrlInTextBox();
                    return;
                }

                if (!string.Equals(rawInput, hostPort, StringComparison.Ordinal))
                {
                    isProgrammaticApiUrlUpdate = true;
                    customApiUrlTextBox.Current.Value = hostPort;
                    isProgrammaticApiUrlUpdate = false;
                }

                string normalised = "https://" + hostPort;
                maybeApplyRuntimeEndpointIfChanged(normalised);
            }, debounce_delay);
        }

        private static bool isValidHostPort(string hostPort)
        {
            var m = hostPortPattern.Match(hostPort);
            if (!m.Success) return false;

            // Validate port range if supplied.
            var g = m.Groups["port"];
            if (g.Success)
            {
                if (!int.TryParse(g.Value, out int port)) return false;
                if (port < 1 || port > 65535) return false;
            }
            return true;
        }

        // Normalises to values used for comparison.
        private static string normalizeToHttps(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

            string hostPort = stripSchemeAndPath(raw);
            return isValidHostPort(hostPort) ? "https://" + hostPort : hostPort;
        }

        /// <summary>
        /// Strips protocol and path from endpoint input.
        /// </summary>
        private static string stripSchemeAndPath(string input)
        {
            string s = Regex.Replace(input, @"^\s*https?://", "", RegexOptions.IgnoreCase);

            int slash = s.IndexOf('/');
            if (slash >= 0) s = s.Substring(0, slash);

            s = s.TrimEnd('/');

            return s;
        }

        private void maybeApplyRuntimeEndpointIfChanged(string normalizedNewValue)
        {
            if (string.Equals(lastApiUrl, normalizedNewValue, StringComparison.OrdinalIgnoreCase))
            {
                customApiUrlTextBox.SetNoticeText(string.Empty, false);
                return;
            }

            lastApiUrl = normalizedNewValue;

            if (api is APIAccess apiAccess)
            {
                if (apiAccess.ApplyRuntimeEndpointConfiguration())
                    customApiUrlTextBox.SetNoticeText(OnlineSettingsStrings.CustomApiUrlAppliedWithoutRestart, false);
                else
                    customApiUrlTextBox.SetNoticeText(string.Empty, false);

                return;
            }

            customApiUrlTextBox.SetNoticeText(OnlineSettingsStrings.CustomApiUrlRestartRequired, false);
        }

        private void restoreLastApiUrlInTextBox()
        {
            if (string.IsNullOrWhiteSpace(lastApiUrl))
                return;

            string hostPort = stripSchemeAndPath(lastApiUrl);

            isProgrammaticApiUrlUpdate = true;
            customApiUrlTextBox.Current.Value = hostPort;
            isProgrammaticApiUrlUpdate = false;
        }
    }
}
