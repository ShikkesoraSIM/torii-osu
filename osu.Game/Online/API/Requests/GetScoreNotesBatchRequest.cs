// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>Trae las notas de una tanda de scores (para los iconitos del leaderboard, 1 request por tanda).</summary>
    public class GetScoreNotesBatchRequest : APIRequest<APIScoreNoteList>
    {
        private readonly IReadOnlyList<long> scoreIds;

        public GetScoreNotesBatchRequest(IReadOnlyList<long> scoreIds)
        {
            this.scoreIds = scoreIds;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.AddParameter(@"score_ids", string.Join(',', scoreIds.Select(i => i.ToString())), RequestParameterType.Query);
            return req;
        }

        protected override string Target => @"torii/score-notes/batch";
    }
}
