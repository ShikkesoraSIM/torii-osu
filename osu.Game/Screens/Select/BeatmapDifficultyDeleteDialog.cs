// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Game.Beatmaps;
using osu.Game.Overlays.Dialog;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// torii: confirmation for deleting a single difficulty from song select. The editor
    /// has had this under File all along, but nobody finds it there, so the same action
    /// lives in the right-click menu now.
    /// </summary>
    public partial class BeatmapDifficultyDeleteDialog : DeletionDialog
    {
        private readonly BeatmapInfo beatmap;

        public BeatmapDifficultyDeleteDialog(BeatmapInfo beatmap)
        {
            this.beatmap = beatmap;
            BodyText = $"{beatmap.Metadata.GetDisplayTitleRomanisable(false)} [{beatmap.DifficultyName}]";
        }

        [BackgroundDependencyLoader]
        private void load(BeatmapManager beatmapManager)
        {
            DangerousAction = () => beatmapManager.DeleteDifficultyImmediately(beatmap);
        }
    }
}
