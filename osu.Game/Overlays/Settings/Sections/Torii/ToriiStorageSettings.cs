// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.IO;
using osu.Game.Localisation;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    public partial class ToriiStorageSettings : SettingsSubsection
    {
        protected override LocalisableString Header => ToriiSettingsStrings.DataSourceHeader;

        [Resolved(canBeNull: true)]
        private OsuGame? game { get; set; }

        [BackgroundDependencyLoader]
        private void load(Storage storage)
        {
            Add(new SettingsButtonV2
            {
                Text = ToriiSettingsStrings.ManageDataSource,
                TooltipText = (storage as OsuStorage)?.CustomStoragePath is string customPath && !string.IsNullOrEmpty(customPath)
                    ? ToriiSettingsStrings.CurrentModeLinked(customPath)
                    : ToriiSettingsStrings.CurrentModePortable,
                Keywords = new[] { "torii", "lazer", "storage", "folder", "maps", "skins", "scores", "portable", "data" },
                Action = () => game?.PerformFromScreen(menu => menu.Push(new ToriiLazerDataSelectScreen())),
            });
        }
    }
}
