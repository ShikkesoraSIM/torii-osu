// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Fetches a page of the local user's points ledger (newest first) for the
    /// points-history overlay. 404s gracefully on a server without the points endpoints.
    /// </summary>
    public class GetPointsHistoryRequest : APIRequest<APIPointsHistory>
    {
        private readonly int limit;
        private readonly int offset;

        public GetPointsHistoryRequest(int limit = 50, int offset = 0)
        {
            this.limit = limit;
            this.offset = offset;
        }

        protected override string Target => $@"torii/points/me/history?limit={limit}&offset={offset}";
    }
}
