// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Bindables;
using osu.Game.Overlays;

namespace osu.Game.Configuration
{
    public enum CustomUiHueScope
    {
        Menu,
        Overlays,
        SettingsPanel,
    }

    public static class CustomUiHueHelper
    {
        public static int ResolveHue(OsuConfigManager config, int fallbackHue, CustomUiHueScope scope)
        {
            return ResolveHue(
                config.Get<bool>(OsuSetting.CustomUIHueEnabled),
                config.Get<float>(OsuSetting.CustomUIHue),
                config.Get<bool>(OsuSetting.CustomUIHueApplyToMenu),
                config.Get<bool>(OsuSetting.CustomUIHueApplyToOverlays),
                config.Get<bool>(OsuSetting.CustomUIHueApplyToSettingsPanel),
                fallbackHue,
                scope);
        }

        public static int ResolveHue(
            bool customHueEnabled,
            float customHue,
            bool applyToMenu,
            bool applyToOverlays,
            bool applyToSettingsPanel,
            int fallbackHue,
            CustomUiHueScope scope)
        {
            if (!customHueEnabled)
                return normaliseHue(fallbackHue);

            bool scopeEnabled = scope switch
            {
                CustomUiHueScope.Menu => applyToMenu,
                CustomUiHueScope.Overlays => applyToOverlays,
                CustomUiHueScope.SettingsPanel => applyToSettingsPanel,
                _ => false,
            };

            return scopeEnabled ? normaliseHue(customHue) : normaliseHue(fallbackHue);
        }

        /// <summary>
        /// Resolves the donator-only accent hue. Returns a (hue, hasOverride)
        /// tuple — when <paramref name="config"/>'s
        /// <see cref="OsuSetting.CustomUIAccentEnabled"/> is off, hasOverride
        /// is false and the consumer should call <c>ResetAccentToBase()</c>
        /// on its colour provider so the accent re-syncs with the chrome.
        /// </summary>
        /// <remarks>
        /// The accent ALSO respects the per-scope toggles — turning off the
        /// hue for "Overlays" turns off the accent for overlays, otherwise
        /// you'd get the absurd state of a chrome-default overlay with a
        /// pink accent slapped on top.
        /// </remarks>
        public static (int hue, bool hasOverride) ResolveAccentHue(OsuConfigManager config, int fallbackHue, CustomUiHueScope scope)
        {
            bool baseEnabled = config.Get<bool>(OsuSetting.CustomUIHueEnabled);
            bool accentEnabled = config.Get<bool>(OsuSetting.CustomUIAccentEnabled);

            if (!baseEnabled || !accentEnabled)
                return (normaliseHue(fallbackHue), false);

            bool scopeEnabled = scope switch
            {
                CustomUiHueScope.Menu => config.Get<bool>(OsuSetting.CustomUIHueApplyToMenu),
                CustomUiHueScope.Overlays => config.Get<bool>(OsuSetting.CustomUIHueApplyToOverlays),
                CustomUiHueScope.SettingsPanel => config.Get<bool>(OsuSetting.CustomUIHueApplyToSettingsPanel),
                _ => false,
            };

            if (!scopeEnabled)
                return (normaliseHue(fallbackHue), false);

            return (normaliseHue(config.Get<float>(OsuSetting.CustomUIAccentHue)), true);
        }

        /// <summary>
        /// Creates a binding that keeps <paramref name="applyHue"/> updated with the resolved hue for the requested scope.
        /// </summary>
        public static IDisposable BindHue(OsuConfigManager config, int fallbackHue, CustomUiHueScope scope, Action<int> applyHue)
            => new CustomUiHueBinding(config, fallbackHue, scope, applyHue);

        /// <summary>
        /// Creates a binding that drives an <see cref="OverlayColourProvider"/>
        /// directly — both base hue and (donator) accent hue at once.
        /// Prefer this over the plain <see cref="BindHue"/> form when the
        /// consumer owns an OverlayColourProvider, so the accent override
        /// stays in sync without any extra wiring at the call site.
        /// </summary>
        public static IDisposable BindFullScheme(OsuConfigManager config, OverlayColourProvider provider, int fallbackHue, CustomUiHueScope scope)
            => new CustomUiFullSchemeBinding(config, provider, fallbackHue, scope);

        private sealed class CustomUiHueBinding : IDisposable
        {
            private readonly Bindable<bool> customHueEnabled;
            private readonly Bindable<float> customHue;
            private readonly Bindable<bool> applyToMenu;
            private readonly Bindable<bool> applyToOverlays;
            private readonly Bindable<bool> applyToSettingsPanel;

            private readonly int fallbackHue;
            private readonly CustomUiHueScope scope;
            private readonly Action<int> applyHue;

            public CustomUiHueBinding(OsuConfigManager config, int fallbackHue, CustomUiHueScope scope, Action<int> applyHue)
            {
                this.fallbackHue = fallbackHue;
                this.scope = scope;
                this.applyHue = applyHue;

                customHueEnabled = config.GetBindable<bool>(OsuSetting.CustomUIHueEnabled);
                customHue = config.GetBindable<float>(OsuSetting.CustomUIHue);
                applyToMenu = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToMenu);
                applyToOverlays = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToOverlays);
                applyToSettingsPanel = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToSettingsPanel);

                customHueEnabled.BindValueChanged(_ => update());
                customHue.BindValueChanged(_ => update());
                applyToMenu.BindValueChanged(_ => update());
                applyToOverlays.BindValueChanged(_ => update());
                applyToSettingsPanel.BindValueChanged(_ => update(), true);
            }

            private void update()
            {
                applyHue(ResolveHue(
                    customHueEnabled.Value,
                    customHue.Value,
                    applyToMenu.Value,
                    applyToOverlays.Value,
                    applyToSettingsPanel.Value,
                    fallbackHue,
                    scope));
            }

            public void Dispose()
            {
                customHueEnabled.UnbindAll();
                customHue.UnbindAll();
                applyToMenu.UnbindAll();
                applyToOverlays.UnbindAll();
                applyToSettingsPanel.UnbindAll();
            }
        }

        // Combined binding: drives both base + accent hue on a single
        // OverlayColourProvider in one ColoursChanged firing. Avoids the
        // double-paint that would happen if a consumer wired a plain
        // BindHue + a separate accent binding to the same provider.
        private sealed class CustomUiFullSchemeBinding : IDisposable
        {
            private readonly OsuConfigManager config;
            private readonly OverlayColourProvider provider;
            private readonly int fallbackHue;
            private readonly CustomUiHueScope scope;

            private readonly Bindable<bool> customHueEnabled;
            private readonly Bindable<float> customHue;
            private readonly Bindable<bool> customAccentEnabled;
            private readonly Bindable<float> customAccentHue;
            private readonly Bindable<bool> applyToMenu;
            private readonly Bindable<bool> applyToOverlays;
            private readonly Bindable<bool> applyToSettingsPanel;

            public CustomUiFullSchemeBinding(OsuConfigManager config, OverlayColourProvider provider, int fallbackHue, CustomUiHueScope scope)
            {
                this.config = config;
                this.provider = provider;
                this.fallbackHue = fallbackHue;
                this.scope = scope;

                customHueEnabled = config.GetBindable<bool>(OsuSetting.CustomUIHueEnabled);
                customHue = config.GetBindable<float>(OsuSetting.CustomUIHue);
                customAccentEnabled = config.GetBindable<bool>(OsuSetting.CustomUIAccentEnabled);
                customAccentHue = config.GetBindable<float>(OsuSetting.CustomUIAccentHue);
                applyToMenu = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToMenu);
                applyToOverlays = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToOverlays);
                applyToSettingsPanel = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToSettingsPanel);

                customHueEnabled.BindValueChanged(_ => update());
                customHue.BindValueChanged(_ => update());
                customAccentEnabled.BindValueChanged(_ => update());
                customAccentHue.BindValueChanged(_ => update());
                applyToMenu.BindValueChanged(_ => update());
                applyToOverlays.BindValueChanged(_ => update());
                applyToSettingsPanel.BindValueChanged(_ => update(), true);
            }

            private void update()
            {
                int baseHue = ResolveHue(config, fallbackHue, scope);
                var (accentHue, hasOverride) = ResolveAccentHue(config, fallbackHue, scope);

                // Apply accent first so that ChangeColourScheme below can
                // see the latest accentHueOverridden flag and decide whether
                // to drag the accent along.
                if (hasOverride)
                    provider.ChangeAccentColourScheme(accentHue);
                else
                    provider.ResetAccentToBase();

                provider.ChangeColourScheme(baseHue);
            }

            public void Dispose()
            {
                customHueEnabled.UnbindAll();
                customHue.UnbindAll();
                customAccentEnabled.UnbindAll();
                customAccentHue.UnbindAll();
                applyToMenu.UnbindAll();
                applyToOverlays.UnbindAll();
                applyToSettingsPanel.UnbindAll();
            }
        }

        private static int normaliseHue(float hue)
        {
            int rounded = (int)MathF.Round(hue);
            int normalised = rounded % 360;

            if (normalised < 0)
                normalised += 360;

            return normalised;
        }
    }
}
