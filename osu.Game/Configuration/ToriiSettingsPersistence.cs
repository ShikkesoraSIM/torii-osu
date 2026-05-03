// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Configuration
{
    /// <summary>
    /// Sidecar persistence for the small set of settings that ONLY exist
    /// in Torii (custom UI hue, supporter accent hue, "confirm dangerous
    /// buttons" toggle, alpha-feature unlocks, etc.).
    ///
    /// Why we need this
    /// ----------------
    /// Lazer's <see cref="OsuConfigManager"/> persists every key it knows
    /// about into the shared <c>osu.cfg</c> on disk. When a user runs the
    /// official ppy lazer client against the same data folder, that
    /// client's config manager doesn't know about Torii-only enum values
    /// — it parses what it understands and rewrites the file, dropping
    /// the keys it doesn't recognise. Returning to Torii then finds the
    /// settings reset to defaults: hue back to purple, accent back off,
    /// alpha unlocks gone, etc.
    ///
    /// To make Torii's settings survive a round-trip through official
    /// lazer we mirror them into a sidecar file <c>torii.ini</c> in the
    /// same storage. On Torii startup we read that file AFTER the base
    /// IniConfigManager has loaded osu.cfg and re-apply the values onto
    /// the existing bindables (no new bindables, no API churn). On every
    /// subsequent change we write the same key-value back to torii.ini.
    /// torii.ini is opaque to upstream lazer so it never gets touched.
    ///
    /// What we DON'T sidecar
    /// --------------------
    /// Anything upstream lazer also understands. UIScale, ChatHeight,
    /// ShowConvertedBeatmaps and friends already round-trip cleanly via
    /// osu.cfg, and writing them in two places would risk a stale
    /// torii.ini value clobbering a fresher osu.cfg value. The
    /// <see cref="ToriiOnlySettings"/> set below is intentionally tight.
    /// </summary>
    internal static class ToriiSettingsPersistence
    {
        /// <summary>
        /// Filename of the sidecar in the game's storage. Plain INI so
        /// it's diffable and human-fixable from outside the app if
        /// someone ever needs to.
        /// </summary>
        private const string sidecar_filename = "torii.ini";

        /// <summary>
        /// The key set we mirror. Keep this in sync with the matching
        /// SetDefault calls in <see cref="OsuConfigManager.InitialiseDefaults"/>
        /// — anything in here MUST already be a registered setting on
        /// the OsuConfigManager when ApplyAndWatch is called, otherwise
        /// the GetBindable lookup will throw.
        /// </summary>
        private static readonly HashSet<OsuSetting> torii_only_settings = new HashSet<OsuSetting>
        {
            // Custom UI hue (sesión 1 of the redesign)
            OsuSetting.CustomUIHueEnabled,
            OsuSetting.CustomUIHue,
            OsuSetting.CustomUIHueApplyToMenu,
            OsuSetting.CustomUIHueApplyToOverlays,
            OsuSetting.CustomUIHueApplyToSettingsPanel,

            // Donator accent hue
            OsuSetting.CustomUIAccentEnabled,
            OsuSetting.CustomUIAccentHue,

            // Pause/fail double-confirm (sesión 3)
            OsuSetting.ToriiConfirmDangerousButtonsOnLongAttempts,

            // Alpha-feature unlock flags (live behind the access-code panel)
            OsuSetting.AlphaToolbarEnabled,
            OsuSetting.AlphaToolbarUse,
            OsuSetting.AlphaPpDevModeEnabled,
            OsuSetting.AlphaStableSongSelectEnabled,

            // Misc Torii visual prefs
            OsuSetting.SongSelectBackgroundBlur,
        };

        /// <summary>
        /// Apply any sidecar values onto the live config and start
        /// watching the listed bindables so future changes flow back
        /// into <c>torii.ini</c>.
        ///
        /// Called once from <see cref="OsuConfigManager"/>'s constructor,
        /// AFTER the base class has loaded osu.cfg into the bindables.
        /// </summary>
        public static void ApplyAndWatch(OsuConfigManager config, Storage storage)
        {
            try
            {
                Dictionary<string, string> sidecar = readSidecar(storage);

                // Apply any sidecar value to the matching bindable. We
                // do this before wiring the watchers so the apply itself
                // doesn't trigger an immediate write-back of the same
                // value (would be harmless but pollutes mtime).
                foreach (var key in torii_only_settings)
                {
                    if (!sidecar.TryGetValue(key.ToString(), out string? raw) || raw is null)
                        continue;

                    try
                    {
                        applyRawToBindable(config, key, raw);
                    }
                    catch (Exception applyErr)
                    {
                        Logger.Log($"[torii.ini] Failed to apply {key}={raw}: {applyErr.Message}", LoggingTarget.Runtime, LogLevel.Important);
                    }
                }

                // Now wire the listeners. Each ValueChanged handler
                // re-reads the entire sidecar, replaces the one key, and
                // rewrites — atomic enough for human-edit-rate updates.
                foreach (var key in torii_only_settings)
                    watchBindable(config, storage, key);
            }
            catch (Exception bootErr)
            {
                Logger.Log($"[torii.ini] sidecar load failed (carrying on with osu.cfg-only): {bootErr.Message}", LoggingTarget.Runtime, LogLevel.Important);
            }
        }

        // ─── Implementation details ──────────────────────────────────

        private static Dictionary<string, string> readSidecar(Storage storage)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            if (!storage.Exists(sidecar_filename))
                return result;

            using (Stream stream = storage.GetStream(sidecar_filename, FileAccess.Read, FileMode.Open))
            using (var reader = new StreamReader(stream))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith('#') || trimmed.StartsWith(';'))
                        continue;

                    int eq = trimmed.IndexOf('=');
                    if (eq <= 0)
                        continue;

                    string key = trimmed.Substring(0, eq).Trim();
                    string value = trimmed.Substring(eq + 1).Trim();
                    result[key] = value;
                }
            }

            return result;
        }

        private static void writeSidecar(Storage storage, Dictionary<string, string> values)
        {
            // Atomic-ish write: stream out via Storage's helper which
            // already does temp-file + replace under the hood for
            // FileMode.Create. Stable enough for a settings sidecar.
            using (Stream stream = storage.GetStream(sidecar_filename, FileAccess.Write, FileMode.Create))
            using (var writer = new StreamWriter(stream))
            {
                writer.WriteLine("# Torii sidecar settings — mirrored from osu.cfg so they survive");
                writer.WriteLine("# being parsed away by the official ppy lazer client when the");
                writer.WriteLine("# data folder is shared. Do not edit by hand unless you know");
                writer.WriteLine("# the corresponding OsuSetting enum names; unknown keys are");
                writer.WriteLine("# ignored on load.");
                writer.WriteLine();

                foreach (var kvp in values)
                    writer.WriteLine($"{kvp.Key}={kvp.Value}");
            }
        }

        private static void applyRawToBindable(OsuConfigManager config, OsuSetting key, string raw)
        {
            // We don't know the bindable's type up-front so we ask the
            // config for its current value and use its type to parse the
            // raw string. Covers bool / int / float / double / enum /
            // string transparently.
            object? current = config.Get<object>(key);
            if (current is null)
                return;

            object parsed = parseRawAs(current.GetType(), raw) ?? current;

            // Use the typed setter so the bindable's own validation
            // (range clamps, enum coercion) still runs.
            var bindableType = current.GetType();
            typeof(ToriiSettingsPersistence)
                .GetMethod(nameof(setTyped), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(bindableType)
                .Invoke(null, new object?[] { config, key, parsed });
        }

        private static void setTyped<T>(OsuConfigManager config, OsuSetting key, T value)
            where T : struct, IEquatable<T>
        {
            // Generic constraint matches what ConfigManager.SetValue<T>
            // accepts for its primitive overload — covers our int / float
            // / double / bool / enum cases. We don't sidecar string-typed
            // settings; CustomApiUrl etc. are deliberately osu.cfg-only.
            config.SetValue(key, value);
        }

        private static object? parseRawAs(Type targetType, string raw)
        {
            if (targetType == typeof(bool))
                return bool.TryParse(raw, out bool b) ? b : (object?)null;
            if (targetType == typeof(int))
                return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : (object?)null;
            if (targetType == typeof(float))
                return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : (object?)null;
            if (targetType == typeof(double))
                return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : (object?)null;
            if (targetType.IsEnum)
                return Enum.TryParse(targetType, raw, ignoreCase: true, out object? e) ? e : null;
            return raw;
        }

        private static void watchBindable(OsuConfigManager config, Storage storage, OsuSetting key)
        {
            // We resolve the bindable as IBindable so we can subscribe
            // without knowing its concrete type. Same instance the
            // settings UI binds to, so OUR write-back fires on every
            // user-driven change.
            object current = config.Get<object>(key);
            if (current is null)
                return;

            var bindableType = current.GetType();

            // Late-bound subscription — avoids the need for a concrete
            // generic call site for every supported T.
            typeof(ToriiSettingsPersistence)
                .GetMethod(nameof(subscribeTyped), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(bindableType)
                .Invoke(null, new object?[] { config, storage, key });
        }

        private static void subscribeTyped<T>(OsuConfigManager config, Storage storage, OsuSetting key)
        {
            Bindable<T> bindable = config.GetBindable<T>(key);
            bindable.ValueChanged += _ => persistSingle(storage, key, bindable.Value!);
        }

        private static void persistSingle(Storage storage, OsuSetting key, object value)
        {
            try
            {
                Dictionary<string, string> current = readSidecar(storage);
                current[key.ToString()] = formatValue(value);
                writeSidecar(storage, current);
            }
            catch (Exception persistErr)
            {
                // Sidecar corruption is recoverable on next launch from
                // osu.cfg; never let a write hiccup crash the game.
                Logger.Log($"[torii.ini] failed to persist {key}: {persistErr.Message}", LoggingTarget.Runtime, LogLevel.Important);
            }
        }

        private static string formatValue(object value)
        {
            // Use invariant culture so a comma-decimal user locale
            // doesn't write "1,5" that we then can't parse back.
            return value switch
            {
                bool b => b.ToString(),
                int i => i.ToString(CultureInfo.InvariantCulture),
                float f => f.ToString("R", CultureInfo.InvariantCulture),
                double d => d.ToString("R", CultureInfo.InvariantCulture),
                Enum e => e.ToString(),
                _ => value.ToString() ?? string.Empty,
            };
        }
    }
}
