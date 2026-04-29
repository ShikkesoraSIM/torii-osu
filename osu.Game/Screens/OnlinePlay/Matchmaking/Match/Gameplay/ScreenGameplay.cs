// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Framework.Screens;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Scoring;
using osu.Game.Screens.OnlinePlay.Multiplayer;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.Match.Gameplay
{
    public partial class ScreenGameplay : MultiplayerPlayer
    {
        public ScreenGameplay(Room room, PlaylistItem playlistItem, MultiplayerRoomUser[] users)
            : base(room, playlistItem, users)
        {
            // Upstream's MultiplayerPlayer ctor adds a `showFailingOverlay`
            // bool that flows into PlayerConfiguration.ShowFailingOverlay.
            // Both pieces are part of a wider rework we haven't pulled yet
            // (Select/Leaderboards → Play/Leaderboards namespace migration
            // among other things), so for now this Torii build keeps the
            // failing overlay visible during ranked-play matches. The
            // hide-while-failing UX behaviour can land in a follow-up that
            // brings PlayerConfiguration up to date.
        }

        protected override async Task PrepareScoreForResultsAsync(Score score)
        {
            await base.PrepareScoreForResultsAsync(score).ConfigureAwait(false);

            Scheduler.Add(() =>
            {
                if (this.IsCurrentScreen())
                    this.Exit();
            });
        }
    }
}
