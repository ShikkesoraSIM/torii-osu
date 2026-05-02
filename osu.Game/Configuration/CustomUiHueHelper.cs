// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Bindables;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
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
        /// Returns true if the supplied user is allowed to enable supporter-
        /// gated cosmetic features (current OR past supporter). Centralised
        /// so the runtime accent gate and the settings-panel UI gate
        /// share one definition of "donator tier".
        /// </summary>
        public static bool IsDonatorTier(APIUser? user) => user != null && (user.IsSupporter || user.HasSupported);

        /// <summary>
        /// Resolves the donator-only accent hue. Returns a (hue, hasOverride)
        /// tuple — when <paramref name="config"/>'s
        /// <see cref="OsuSetting.CustomUIAccentEnabled"/> is off OR the
        /// currently-logged-in user isn't a supporter, hasOverride is false
        /// and the consumer should call <c>ResetAccentToBase()</c> on its
        /// colour provider so the accent re-syncs with the chrome.
        /// </summary>
        /// <remarks>
        /// The accent ALSO respects the per-scope toggles — turning off the
        /// hue for "Overlays" turns off the accent for overlays, otherwise
        /// you'd get the absurd state of a chrome-default overlay with a
        /// pink accent slapped on top.
        /// <para/>
        /// The supporter check is what stops a non-supporter from inheriting
        /// a previous supporter user's CustomUIAccentEnabled / CustomUIAccentHue
        /// values from the per-machine osu.cfg. The values themselves stay in
        /// config (so a temporary tag expiry doesn't wipe the user's choice)
        /// — they're just ignored by the runtime resolver while no qualifying
        /// user is signed in.
        /// </remarks>
        public static (int hue, bool hasOverride) ResolveAccentHue(OsuConfigManager config, int fallbackHue, CustomUiHueScope scope, APIUser? currentUser = null)
        {
            bool baseEnabled = config.Get<bool>(OsuSetting.CustomUIHueEnabled);
            bool accentEnabled = config.Get<bool>(OsuSetting.CustomUIAccentEnabled);

            if (!baseEnabled || !accentEnabled || !IsDonatorTier(currentUser))
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
        /// <remarks>
        /// Pass <paramref name="api"/> when available so the accent is
        /// gated on the local user's supporter status — without it the
        /// accent would still apply for anyone using a machine that
        /// previously had a supporter signed in (the values persist in
        /// per-machine osu.cfg, not per-user).
        /// </remarks>
        public static IDisposable BindFullScheme(OsuConfigManager config, OverlayColourProvider provider, int fallbackHue, CustomUiHueScope scope, IAPIProvider? api = null)
            => new CustomUiFullSchemeBinding(config, provider, fallbackHue, scope, api);

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
        // Also gates the accent on the locally-signed-in user's supporter
        // status — re-fires whenever the local user changes (login /
        // logout / account switch) so a stale supporter config can't
        // bleed into a non-supporter session.
        private sealed class CustomUiFullSchemeBinding : IDisposable
        {
            private readonly OsuConfigManager config;
            private readonly OverlayColourProvider provider;
            private readonly int fallbackHue;
            private readonly CustomUiHueScope scope;
            private readonly IAPIProvider? api;

            private readonly Bindable<bool> customHueEnabled;
            private readonly Bindable<float> customHue;
            private readonly Bindable<bool> customAccentEnabled;
            private readonly Bindable<float> customAccentHue;
            private readonly Bindable<bool> applyToMenu;
            private readonly Bindable<bool> applyToOverlays;
            private readonly Bindable<bool> applyToSettingsPanel;
            private readonly IBindable<APIUser>? localUser;

            public CustomUiFullSchemeBinding(OsuConfigManager config, OverlayColourProvider provider, int fallbackHue, CustomUiHueScope scope, IAPIProvider? api)
            {
                this.config = config;
                this.provider = provider;
                this.fallbackHue = fallbackHue;
                this.scope = scope;
                this.api = api;

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

                if (api != null)
                {
                    localUser = api.LocalUser.GetBoundCopy();
                    // Re-evaluate when the signed-in user changes — covers
                    // login, logout, account switch, and the moment the
                    // initially-deferred APIUser populates after auth.
                    localUser.BindValueChanged(_ => update());
                }

                applyToSettingsPanel.BindValueChanged(_ => update(), true);
            }

            private void update()
            {
                int baseHue = ResolveHue(config, fallbackHue, scope);
                var (accentHue, hasOverride) = ResolveAccentHue(config, fallbackHue, scope, localUser?.Value);

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
                localUser?.UnbindAll();
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
