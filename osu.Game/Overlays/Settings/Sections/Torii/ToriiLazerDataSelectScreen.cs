// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Localisation;
using osu.Framework.Screens;
using osu.Framework.Platform;
using osu.Game.IO;
using osu.Game.Localisation;
using osu.Game.Overlays.Dialog;
using osu.Game.Overlays.Settings.Sections.Maintenance;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    public partial class ToriiLazerDataSelectScreen : DirectorySelectScreen
    {
        [Resolved]
        private Storage storage { get; set; }

        [Resolved]
        private OsuGameBase game { get; set; }

        [Resolved(canBeNull: true)]
        private IDialogOverlay dialogOverlay { get; set; }

        protected override DirectoryInfo InitialPath
        {
            get
            {
                string? detected = ToriiStoragePathHelper.GetLikelyLazerStoragePath();
                if (!string.IsNullOrEmpty(detected))
                    return new DirectoryInfo(detected);

                return new DirectoryInfo(storage.GetFullPath(string.Empty)).Parent;
            }
        }

        public override bool AllowExternalScreenChange => false;

        public override bool DisallowExternalBeatmapRulesetChanges => true;

        public override bool HideOverlaysOnEnter => true;

        public override LocalisableString HeaderText => ToriiSettingsStrings.PickLazerFolderHeader;

        protected override bool IsValidDirectory(DirectoryInfo info) => ToriiStoragePathHelper.LooksLikeLazerStoragePath(info.FullName);

        protected override void OnSelection(DirectoryInfo directory)
        {
            if (!ToriiStoragePathHelper.LooksLikeLazerStoragePath(directory.FullName))
                return;

            dialogOverlay?.Push(new ConfirmDialog(MaintenanceSettingsStrings.RestartAndReOpenRequiredForCompletion, () =>
            {
                (storage as OsuStorage)?.ChangeDataPath(directory.FullName);
                game.Exit();
            }));
        }
    }
}
