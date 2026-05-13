// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;

namespace osu.Game.Overlays.Settings.Sections.DebugSettings
{
    /// <summary>
    /// Settings subsection for the Torii hiccup logger — sits at the very
    /// bottom of <see cref="DebugSection"/> so it doesn't compete for
    /// attention with the everyday debug toggles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two checkboxes here form a two-stage opt-in:
    /// </para>
    /// <list type="number">
    ///     <item><description>
    ///     <b>Record frame hiccups to disk</b> — turns on the local-only
    ///     capture. Off by default; the logger component is not even
    ///     constructed unless this is on, so the feature has zero runtime
    ///     cost when off.
    ///     </description></item>
    ///     <item><description>
    ///     <b>Share with Torii devs</b> — sub-toggle, gated by the first
    ///     toggle. When on, captured records are additionally batched and
    ///     POSTed to the Torii admin dashboard. Off by default even when
    ///     the local capture is on, so users can record privately for
    ///     their own debugging without sharing anything externally.
    ///     </description></item>
    /// </list>
    /// </remarks>
    public partial class ToriiHiccupSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Torii hiccup logger";

        private Bindable<bool> loggerEnabled;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config, Storage storage)
        {
            loggerEnabled = config.GetBindable<bool>(OsuSetting.ToriiHiccupLoggerEnabled);

            Add(new SettingsItemV2(new FormCheckBox
            {
                Caption = "Record frame hiccups to disk",
                HintText = "When enabled, frames slower than ~33 ms (below 30 fps) are written as JSON lines to "
                           + "<storage>/torii/hiccups/<timestamp>.jsonl with surrounding context (current screen, "
                           + "visible overlays, GC stats, recent events) so devs can diagnose lag spikes from a "
                           + "captured session. Toggling OFF disposes the logger and stops all measurement; the "
                           + "feature has zero runtime cost when off.",
                Current = loggerEnabled,
            }));

            // Sub-toggle — only meaningful when the logger above is on, but
            // we always render it (greyed out) so users can see the feature
            // exists. The disabled state mirrors the parent toggle.
            var shareToggle = new FormCheckBox
            {
                Caption = "Share captures with Torii devs",
                HintText = "When enabled, captured hiccup records are also batch-uploaded to "
                           + "lazer-api.shikkesora.com every ~30 seconds so Torii devs can view "
                           + "them on the admin dashboard alongside reports from other users. "
                           + "Each upload identifies you by your osu! user ID (when logged in) plus "
                           + "a stable per-install device hash (a SHA-256 of a randomly-generated "
                           + "GUID — never your machine MAC, disk serial, or similar). No personally "
                           + "identifying information beyond that. Disable to keep captures local-only.",
                Current = config.GetBindable<bool>(OsuSetting.ToriiHiccupShareEnabled),
            };

            // Gate the sub-toggle on the parent toggle. When the parent flips
            // OFF, the sub-toggle's value also flips OFF (both because users
            // would expect it and because the logger component is gone, so
            // there's nothing to gate anyway).
            //
            // ORDER MATTERS — `Bindable.Value = X` throws if `Disabled == true`.
            // The previous (broken) version flipped Disabled BEFORE setting
            // Value, which crashed `ToriiHiccupSettings` on construction every
            // time the parent toggle was OFF (the immediate-fire callback ran
            // with NewValue=false, set Disabled=true, then tried to write
            // Value=false → InvalidOperationException). That crash bubbled up
            // through the whole Settings overlay and prevented users from
            // opening Settings at all.
            loggerEnabled.BindValueChanged(e =>
            {
                // Re-enable first so the value mutation below is legal even
                // if a previous toggle left the bindable disabled.
                shareToggle.Current.Disabled = false;

                if (!e.NewValue && shareToggle.Current.Value)
                    shareToggle.Current.Value = false;

                // Disable last — once disabled, the bindable rejects writes.
                shareToggle.Current.Disabled = !e.NewValue;
            }, true);

            Add(new SettingsItemV2(shareToggle));

            Add(new SettingsButtonV2
            {
                Text = "Open hiccup-log folder",
                Action = () =>
                {
                    try
                    {
                        var hiccupStorage = storage.GetStorageForDirectory("torii/hiccups");
                        string path = hiccupStorage.GetFullPath(string.Empty);
                        if (!Directory.Exists(path))
                            Directory.CreateDirectory(path);
                        hiccupStorage.PresentExternally();
                    }
                    catch { /* best-effort */ }
                },
            });
        }
    }
}
