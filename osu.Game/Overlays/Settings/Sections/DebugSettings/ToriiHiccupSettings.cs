// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;

namespace osu.Game.Overlays.Settings.Sections.DebugSettings
{
    /// <summary>
    /// Settings subsection for the Torii hiccup logger — sits at the very
    /// bottom of <see cref="DebugSection"/> so it doesn't compete for
    /// attention with the everyday debug toggles. Off by default; turning it
    /// on adds a single component to the game host that records frames
    /// slower than 33 ms (sub-30 fps) into JSONL with surrounding context.
    /// </summary>
    /// <remarks>
    /// The toggle is wired in <c>OsuGame.wireToriiHiccupLogger</c>. When OFF,
    /// no component is constructed or added to the draw tree, so runtime is
    /// byte-identical to a Torii build without the logger feature. See
    /// <see cref="osu.Game.Performance.ToriiHiccupLogger"/> for the logger
    /// itself + the captured-record schema.
    /// </remarks>
    public partial class ToriiHiccupSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Torii hiccup logger";

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config, Storage storage)
        {
            Add(new SettingsItemV2(new FormCheckBox
            {
                Caption = "Record frame hiccups to disk",
                HintText = "When enabled, frames slower than ~33 ms (below 30 fps) are written as JSON lines to "
                           + "<storage>/torii/hiccups/<timestamp>.jsonl with surrounding context (current screen, "
                           + "visible overlays, GC stats, recent events) so devs can diagnose lag spikes from a "
                           + "captured session. Toggling OFF disposes the logger and stops all measurement; the "
                           + "feature has zero runtime cost when off.",
                Current = config.GetBindable<bool>(OsuSetting.ToriiHiccupLoggerEnabled),
            }));

            Add(new SettingsButtonV2
            {
                Text = "Open hiccup-log folder",
                Action = () =>
                {
                    try
                    {
                        // Make sure the directory exists before asking the OS
                        // to open it (otherwise the file manager just no-ops).
                        var hiccupStorage = storage.GetStorageForDirectory("torii/hiccups");
                        string path = hiccupStorage.GetFullPath(string.Empty);
                        if (!Directory.Exists(path))
                            Directory.CreateDirectory(path);
                        hiccupStorage.PresentExternally();
                    }
                    catch
                    {
                        // Best-effort; the toggle still works if this button can't open the folder.
                    }
                },
            });
        }
    }
}
