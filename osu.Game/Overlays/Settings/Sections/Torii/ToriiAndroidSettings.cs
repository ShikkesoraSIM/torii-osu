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

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
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
                               + "Toggling applies immediately — no restart required.",
                    Current = config.GetBindable<bool>(OsuSetting.EnableOboeAudio),
                })
                {
                    Keywords = new[] { "oboe", "android", "latency", "aaudio", "mmap", "low latency", "audio" },
                },
            };
        }
    }
}
