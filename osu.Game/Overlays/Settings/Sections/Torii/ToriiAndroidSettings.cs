// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    /// <summary>
    /// Android-specific Torii settings. Constructed unconditionally — the
    /// gating is done by <see cref="ToriiSection"/> at the section level so
    /// Desktop / iOS users never see this subsection at all.
    ///
    /// Currently houses the low-latency Oboe audio toggle. Future Android-only
    /// knobs (sustained performance mode, frame pacing toggle, etc.) belong
    /// here too rather than scattered across ruleset / gameplay subsections.
    /// </summary>
    public partial class ToriiAndroidSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Android";

        private readonly Bindable<SettingsNote.Data?> oboeRestartNote = new Bindable<SettingsNote.Data?>();

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            var oboeBindable = config.GetBindable<bool>(OsuSetting.EnableOboeAudio);

            Children = new Drawable[]
            {
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Low-latency audio (Oboe)",
                    HintText = "Routes audio through Google's Oboe library for AAudio MMAP-exclusive output. "
                               + "Cuts Android audio latency from ~60–200 ms down to ~15–30 ms on devices that "
                               + "support MMAP, with transparent OpenSL ES fallback on older hardware. "
                               + "Disable if your device misbehaves (Samsung security policies blocking the "
                               + "native lib, very old Android versions, etc.) — the bridge silently no-ops "
                               + "on load failure so toggling it OFF is a hard escape hatch. "
                               + "Restart the app for changes to take effect.",
                    Current = oboeBindable,
                })
                {
                    Keywords = new[] { "oboe", "android", "latency", "aaudio", "mmap", "low latency", "audio" },
                    Note = { BindTarget = oboeRestartNote },
                },
            };

            // Surface the "restart required" hint as soon as the user flips
            // the toggle — the bridge can't be hot-swapped while audio is
            // playing, so the note is the only signal that the change isn't
            // immediately live.
            oboeBindable.BindValueChanged(_ =>
            {
                oboeRestartNote.Value = new SettingsNote.Data(
                    "Restart the app for the change to take effect.",
                    SettingsNote.Type.Warning);
            });
        }
    }
}
