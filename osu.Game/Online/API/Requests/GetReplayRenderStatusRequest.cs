// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>Polls the status of an o!rdr render previously submitted via <see cref="SubmitReplayRenderRequest"/>.</summary>
    public class GetReplayRenderStatusRequest : APIRequest<APIReplayRenderStatus>
    {
        private readonly long renderId;

        public GetReplayRenderStatusRequest(long renderId)
        {
            this.renderId = renderId;
        }

        protected override string Target => $@"torii/replay-render/{renderId}";
    }
}
