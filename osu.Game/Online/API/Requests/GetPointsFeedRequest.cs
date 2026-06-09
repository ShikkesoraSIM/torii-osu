// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Fetches the local user's recent points earnings after a cursor id, so the
    /// client can pop a "+N" toast and explain why (top play, daily play, etc.).
    /// 404s gracefully on a server without the points endpoints.
    /// </summary>
    public class GetPointsFeedRequest : APIRequest<APIPointsFeed>
    {
        private readonly int sinceId;
        private readonly int limit;

        public GetPointsFeedRequest(int sinceId, int limit = 20)
        {
            this.sinceId = sinceId;
            this.limit = limit;
        }

        protected override string Target => $@"torii/points/feed?since_id={sinceId}&limit={limit}";
    }
}
