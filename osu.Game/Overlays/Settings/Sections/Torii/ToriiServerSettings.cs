// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Dialog;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    public partial class ToriiServerSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Server";

        // Optional so test scenes without OsuGame / IDialogOverlay still bind cleanly.
        [Resolved(CanBeNull = true)]
        private OsuGame? game { get; set; }

        [Resolved(CanBeNull = true)]
        private IDialogOverlay? dialogOverlay { get; set; }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            var pulseEnabled = config.GetBindable<bool>(OsuSetting.ToriiServerPulseEnabled);

            Children = new Drawable[]
            {
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Show server pulse on toolbar",
                    HintText = "Live server activity widget (currently playing, plays per minute, top "
                               + "map). Turning it OFF fully removes it: no polling, no toolbar button, "
                               + "no background work at all, as if it didn't exist. Changing this "
                               + "restarts the game so it takes full effect.",
                    Current = pulseEnabled,
                })
                {
                    Keywords = new[] { @"pulse", @"server", @"activity", @"online", @"toolbar", @"playing", @"torii" },
                },
            };

            pulseEnabled.BindValueChanged(change =>
            {
                // Skip the initial bind; only a real user flip should prompt a restart.
                if (change.NewValue == change.OldValue)
                    return;

                dialogOverlay?.Push(new ConfirmDialog(
                    "The server pulse is built (or skipped) at startup, so the game needs to restart to fully apply this. It will close now, please open it again.",
                    () => game?.AttemptExit(),
                    () => pulseEnabled.Value = change.OldValue));
            });
        }
    }
}
