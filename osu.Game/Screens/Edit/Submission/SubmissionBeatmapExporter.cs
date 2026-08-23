// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Screens.Edit.Submission
{
    public class SubmissionBeatmapExporter : LegacyBeatmapExporter
    {
        private readonly uint? beatmapSetId;
        private readonly HashSet<int>? allocatedBeatmapIds;

        /// <summary>
        /// IDs the server allocated that no difficulty in this set claims: the pool for
        /// difficulties that need a new one. Worked out on the first difficulty exported,
        /// because until then we don't know which IDs are spoken for.
        /// </summary>
        private HashSet<int>? freeBeatmapIds;

        public SubmissionBeatmapExporter(Storage storage, PutBeatmapSetResponse putBeatmapSetResponse)
            : base(storage)
        {
            beatmapSetId = putBeatmapSetResponse.BeatmapSetId;
            allocatedBeatmapIds = putBeatmapSetResponse.BeatmapIds.Select(id => (int)id).ToHashSet();
        }

        protected override void MutateBeatmap(BeatmapSetInfo beatmapSet, IBeatmap playableBeatmap)
        {
            base.MutateBeatmap(beatmapSet, playableBeatmap);

            if (beatmapSetId != null && allocatedBeatmapIds != null)
            {
                playableBeatmap.BeatmapInfo.BeatmapSet = beatmapSet;
                playableBeatmap.BeatmapInfo.BeatmapSet!.OnlineID = (int)beatmapSetId;

                if (allocatedBeatmapIds.Contains(playableBeatmap.BeatmapInfo.OnlineID))
                {
                    allocatedBeatmapIds.Remove(playableBeatmap.BeatmapInfo.OnlineID);
                    return;
                }

                // torii: los ids que quedan libres son los que no reclama NINGUNA diff del
                // set, no "el primero que sobre": si no, una diff nueva se queda con el id
                // de una que todavia no exportamos y esa despues no encuentra el suyo.
                freeBeatmapIds ??= allocatedBeatmapIds
                                   .Except(beatmapSet.Beatmaps.Select(b => b.OnlineID))
                                   .ToHashSet();

                if (freeBeatmapIds.Count == 0)
                    throw new InvalidOperationException(@"Ran out of new beatmap IDs to assign to unsubmitted beatmaps!");

                // torii: una diff con un id que este set no tiene es una que ya se subio en
                // otro lado y ahora la estan metiendo aca. no se le puede mudar el id (se lo
                // estariamos robando al mapa original, con sus scores), asi que entra como
                // diff nueva. antes esto reventaba la subida entera con un error que no le
                // decia nada a nadie.
                if (playableBeatmap.BeatmapInfo.OnlineID > 0)
                    Logger.Log($@"Difficulty ""{playableBeatmap.BeatmapInfo.DifficultyName}"" carried BeatmapID {playableBeatmap.BeatmapInfo.OnlineID} from another set; it will be submitted as a new difficulty.");

                int newId = freeBeatmapIds.First();
                freeBeatmapIds.Remove(newId);
                allocatedBeatmapIds.Remove(newId);
                playableBeatmap.BeatmapInfo.OnlineID = newId;
            }
        }
    }
}
