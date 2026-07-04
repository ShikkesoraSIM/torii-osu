// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Asks the server to render a stored score's replay to video via o!rdr.
    /// The o!rdr key lives server-side only; the server enforces a per-user
    /// cooldown (1 render / 10 min) and supporter gating for premium options.
    /// </summary>
    public class SubmitReplayRenderRequest : APIRequest<APIReplayRenderSubmission>
    {
        private readonly long scoreId;
        private readonly string resolution;
        private readonly string skin;
        private readonly bool motionBlur;
        private readonly bool share;

        public SubmitReplayRenderRequest(long scoreId, string resolution, string skin, bool motionBlur, bool share)
        {
            this.scoreId = scoreId;
            this.resolution = resolution;
            this.skin = skin;
            this.motionBlur = motionBlur;
            this.share = share;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;
            req.AddParameter(@"resolution", resolution, RequestParameterType.Query);
            req.AddParameter(@"skin", skin, RequestParameterType.Query);
            req.AddParameter(@"motion_blur", motionBlur ? @"true" : @"false", RequestParameterType.Query);
            req.AddParameter(@"share", share ? @"true" : @"false", RequestParameterType.Query);
            return req;
        }

        protected override string Target => $@"torii/replay-render/{scoreId}";
    }
}
