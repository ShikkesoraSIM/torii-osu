// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Configuration
{
    /// <summary>
    /// DEPRECATED. Sidecar persistence for the small set of settings that
    /// ONLY exist in Torii.
    ///
    /// As of the torii.ini-as-primary-config cut-over (see
    /// <see cref="OsuConfigManager.TORII_CONFIG_FILENAME"/>) this sidecar
    /// is redundant — every key the sidecar used to mirror is now
    /// written directly to torii.ini by the standard IniConfigManager
    /// path, and the previous "game.ini gets clobbered by official
    /// lazer" failure mode can't happen because Torii no longer writes
    /// game.ini at all. The class is kept in the tree for one release
    /// cycle in case any out-of-tree call sites still reference it;
    /// will be removed in a follow-up cleanup commit.
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
    /// settings table below is intentionally tight.
    ///
    /// Implementation note
    /// -------------------
    /// The first version of this file tried to do <c>config.Get&lt;object&gt;(key)</c>
    /// to discover each bindable's type at runtime, but that throws —
    /// <c>BindableBool</c> doesn't inherit from <c>Bindable&lt;object&gt;</c>.
    /// We now register the value-type alongside each key in
    /// <see cref="torii_only_settings"/> and dispatch <c>SetValue&lt;T&gt;</c>
    /// / <c>GetBindable&lt;T&gt;</c> calls explicitly by that type. If you
    /// add a new Torii-only setting whose type isn't bool/int, extend the
    /// switch in <c>applyValue</c> / <c>watchKey</c> / <c>formatValue</c>
    /// at the bottom of this file.
    /// </summary>
    [Obsolete("torii.ini is now the primary config (see OsuConfigManager.TORII_CONFIG_FILENAME). " +
              "The sidecar mechanism is redundant; this class is scheduled for removal in a follow-up commit.")]
    internal static class ToriiSettingsPersistence
    {
        /// <summary>
        /// Filename of the sidecar in the game's storage. Plain INI so
        /// it's diffable and human-fixable from outside the app if
        /// someone ever needs to.
        /// </summary>
        private const string sidecar_filename = "torii.ini";

        /// <summary>
        /// Maps each mirrored setting to its registered value-type.
        /// Keep this in sync with the matching <c>SetDefault</c> calls
        /// in <see cref="OsuConfigManager.InitialiseDefaults"/>.
        /// Adding a key here whose declared type doesn't match the
        /// SetDefault call will throw at apply-time (correctly so —
        /// the cast to <c>Bindable&lt;T&gt;</c> would be a real bug).
        /// </summary>
        private static readonly Dictionary<OsuSetting, Type> torii_only_settings = new Dictionary<OsuSetting, Type>
        {
            // Custom UI hue (sesión 1 of the redesign).
            // CustomUIHue is a BindableFloat under the hood (the
            // SetDefault overload that takes the 0-359 range upgrades
            // ints to float internally). Mismatched type here would
            // throw "Cannot convert BindableFloat to Bindable<Int32>".
            { OsuSetting.CustomUIHueEnabled, typeof(bool) },
            { OsuSetting.CustomUIHue, typeof(float) },
            { OsuSetting.CustomUIHueApplyToMenu, typeof(bool) },
            { OsuSetting.CustomUIHueApplyToOverlays, typeof(bool) },
            { OsuSetting.CustomUIHueApplyToSettingsPanel, typeof(bool) },

            // Donator accent hue (also BindableFloat for the same
            // reason as CustomUIHue above).
            { OsuSetting.CustomUIAccentEnabled, typeof(bool) },
            { OsuSetting.CustomUIAccentHue, typeof(float) },

            // Pause/fail double-confirm (sesión 3)
            { OsuSetting.ToriiConfirmDangerousButtonsOnLongAttempts, typeof(bool) },

            // Alpha-feature unlock flags (live behind the access-code panel)
            { OsuSetting.AlphaToolbarEnabled, typeof(bool) },
            { OsuSetting.AlphaToolbarUse, typeof(bool) },
            { OsuSetting.AlphaPpDevModeEnabled, typeof(bool) },
            { OsuSetting.AlphaStableSongSelectEnabled, typeof(bool) },

            // Misc Torii visual prefs
            { OsuSetting.SongSelectBackgroundBlur, typeof(bool) },
            // UseGameplayCursorInMenus is deprecated (replaced by
            // MenuCursorStyle below). Kept in the sidecar so users
            // who wrote it from a previous Torii build don't see a
            // load error; the value is functionally ignored now.
            { OsuSetting.UseGameplayCursorInMenus, typeof(bool) },
            { OsuSetting.MenuCursorStyle, typeof(osu.Game.Graphics.Cursor.MenuCursorStyle) },
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
                foreach (var entry in torii_only_settings)
                {
                    if (!sidecar.TryGetValue(entry.Key.ToString(), out string raw) || raw == null)
                        continue;

                    try
                    {
                        applyValue(config, entry.Key, entry.Value, raw);
                    }
                    catch (Exception applyErr)
                    {
                        Logger.Log($"[torii.ini] Failed to apply {entry.Key}={raw}: {applyErr.Message}", LoggingTarget.Runtime, LogLevel.Important);
                    }
                }

                // Now wire the listeners. Each ValueChanged handler
                // re-reads the entire sidecar, replaces the one key, and
                // rewrites — atomic enough for human-edit-rate updates.
                foreach (var entry in torii_only_settings)
                {
                    try
                    {
                        watchKey(config, storage, entry.Key, entry.Value);
                    }
                    catch (Exception watchErr)
                    {
                        Logger.Log($"[torii.ini] Failed to watch {entry.Key}: {watchErr.Message}", LoggingTarget.Runtime, LogLevel.Important);
                    }
                }
            }
            catch (Exception bootErr)
            {
                Logger.Log($"[torii.ini] sidecar load failed (carrying on with osu.cfg-only): {bootErr.Message}", LoggingTarget.Runtime, LogLevel.Important);
            }
        }

        // ─── INI I/O ─────────────────────────────────────────────────

        private static Dictionary<string, string> readSidecar(Storage storage)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);

            if (!storage.Exists(sidecar_filename))
                return result;

            using (Stream stream = storage.GetStream(sidecar_filename, FileAccess.Read, FileMode.Open))
            using (var reader = new StreamReader(stream))
            {
                string line;
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

        private static void persistSingle(Storage storage, OsuSetting key, string formatted)
        {
            try
            {
                Dictionary<string, string> current = readSidecar(storage);
                current[key.ToString()] = formatted;
                writeSidecar(storage, current);
            }
            catch (Exception persistErr)
            {
                // Sidecar corruption is recoverable on next launch from
                // osu.cfg; never let a write hiccup crash the game.
                Logger.Log($"[torii.ini] failed to persist {key}: {persistErr.Message}", LoggingTarget.Runtime, LogLevel.Important);
            }
        }

        // ─── Type-aware dispatch ─────────────────────────────────────
        // All three of these switch on the same registered Type. If you
        // add a new value-type to the registry, extend ALL THREE — apply,
        // watch and format — together.

        private static void applyValue(OsuConfigManager config, OsuSetting key, Type type, string raw)
        {
            if (type == typeof(bool))
            {
                if (bool.TryParse(raw, out bool v))
                    config.SetValue(key, v);
            }
            else if (type == typeof(int))
            {
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                    config.SetValue(key, v);
            }
            else if (type == typeof(float))
            {
                if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                    config.SetValue(key, v);
            }
            else if (type == typeof(double))
            {
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    config.SetValue(key, v);
            }
            else if (type.IsEnum)
            {
                applyEnumValue(config, key, type, raw);
            }
            else
            {
                throw new InvalidOperationException($"unsupported sidecar type {type} for {key}");
            }
        }

        /// <summary>
        /// Generic enum applicator. Reflection is needed because
        /// <see cref="ConfigManager{TLookup}.SetValue{TBindable}"/> is
        /// generic on the value type and we don't know the enum type
        /// at compile time. The cost is one method-handle lookup per
        /// applied setting at startup; negligible compared to disk I/O.
        ///
        /// Routes through a typed helper (<see cref="setEnumValue{TEnum}"/>)
        /// so the actual <c>SetValue</c> call site is type-safe — the
        /// reflection only computes the right generic instantiation.
        /// </summary>
        private static void applyEnumValue(OsuConfigManager config, OsuSetting key, Type enumType, string raw)
        {
            // Enum.TryParse(Type, ...) is case-insensitive — handles
            // a hand-edited torii.ini where the value casing got
            // mangled.
            if (!Enum.TryParse(enumType, raw, ignoreCase: true, out object? parsed) || parsed == null)
                return;

            typeof(ToriiSettingsPersistence)
                .GetMethod(nameof(setEnumValue), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(enumType)
                .Invoke(null, new[] { config, (object)key, parsed });
        }

        private static void setEnumValue<TEnum>(OsuConfigManager config, OsuSetting key, TEnum value)
            where TEnum : struct, Enum
        {
            config.SetValue(key, value);
        }

        private static void watchKey(OsuConfigManager config, Storage storage, OsuSetting key, Type type)
        {
            // Resolve the bindable as its registered concrete type so the
            // GetBindable<T> cast actually succeeds (BindableBool ↦ Bindable<bool>,
            // BindableInt ↦ Bindable<int>, etc.).
            if (type == typeof(bool))
            {
                var bindable = config.GetBindable<bool>(key);
                bindable.ValueChanged += _ => persistSingle(storage, key, formatValue(bindable.Value));
            }
            else if (type == typeof(int))
            {
                var bindable = config.GetBindable<int>(key);
                bindable.ValueChanged += _ => persistSingle(storage, key, formatValue(bindable.Value));
            }
            else if (type == typeof(float))
            {
                var bindable = config.GetBindable<float>(key);
                bindable.ValueChanged += _ => persistSingle(storage, key, formatValue(bindable.Value));
            }
            else if (type == typeof(double))
            {
                var bindable = config.GetBindable<double>(key);
                bindable.ValueChanged += _ => persistSingle(storage, key, formatValue(bindable.Value));
            }
            else if (type.IsEnum)
            {
                // Same generic-via-reflection trick as applyValue —
                // GetBindable / ValueChanged subscription both need
                // the concrete enum type.
                typeof(ToriiSettingsPersistence)
                    .GetMethod(nameof(watchEnumKey), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(type)
                    .Invoke(null, new object[] { config, storage, key });
            }
            else
            {
                throw new InvalidOperationException($"unsupported sidecar type {type} for {key}");
            }
        }

        private static void watchEnumKey<TEnum>(OsuConfigManager config, Storage storage, OsuSetting key)
            where TEnum : struct, Enum
        {
            var bindable = config.GetBindable<TEnum>(key);
            bindable.ValueChanged += _ => persistSingle(storage, key, formatValue(bindable.Value));
        }

        private static string formatValue(object value)
        {
            // Use invariant culture so a comma-decimal user locale
            // doesn't write "1,5" that we then can't parse back.
            // Enums first because the bool / int branches won't match.
            return value switch
            {
                Enum e => e.ToString(),
                bool b => b.ToString(),
                int i => i.ToString(CultureInfo.InvariantCulture),
                float f => f.ToString("R", CultureInfo.InvariantCulture),
                double d => d.ToString("R", CultureInfo.InvariantCulture),
                _ => value?.ToString() ?? string.Empty,
            };
        }
    }
}
