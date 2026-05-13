// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using osu.Framework.IO.Network;
using osu.Game.Beatmaps;

namespace osu.Game.Online.API.Requests
{
    public class UpdateBeatmapRankStatusRequest : APIRequest
    {
        private readonly long beatmapSetId;
        private readonly BeatmapRankStatus status;

        public UpdateBeatmapRankStatusRequest(long beatmapSetId, BeatmapRankStatus status)
        {
            this.beatmapSetId = beatmapSetId;
            this.status = status;
        }

        protected override string Target => $@"admin/beatmaps/{beatmapSetId}/rank";

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;
            req.AddParameter(@"status", ((int)status).ToString());
            return req;
        }
    }
}
