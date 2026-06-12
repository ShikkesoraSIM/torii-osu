// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Input.Bindings;
using osu.Game.Localisation;
using osu.Game.Overlays;
using osu.Game.Screens.Footer;

namespace osu.Game.Screens.SelectV2
{
    public partial class FooterButtonOptions : ScreenFooterButton, IHasPopover
    {
        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> workingBeatmap { get; set; } = null!;

        [Resolved]
        private ISongSelect? songSelect { get; set; }

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load(OsuColour colour)
        {
            Text = SongSelectStrings.Options;
            Icon = FontAwesome.Solid.Cog;
            AccentColour = colour.Purple1;
            Hotkey = GlobalAction.ToggleBeatmapOptions;

            Action = this.ShowPopover;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            workingBeatmap.BindValueChanged(_ => beatmapChanged(), true);
        }

        private void beatmapChanged()
        {
            this.HidePopover();
            Enabled.Value = !workingBeatmap.IsDefault;
        }

        public Framework.Graphics.UserInterface.Popover GetPopover()
        {
            // Resolve the live beatmap only when the options popover actually opens,
            // instead of on every carousel selection. This is a realm query on the
            // update thread, and selections fire far more often than the popover is
            // opened, so doing it eagerly was a measured song-select stall source.
            var beatmap = realm.Run(r => r.Find<BeatmapInfo>(workingBeatmap.Value.BeatmapInfo.ID)!.ToLive(realm));

            return new Popover(this, beatmap.Value.Detach())
            {
                ColourProvider = colourProvider,
                SongSelect = songSelect
            };
        }
    }
}
