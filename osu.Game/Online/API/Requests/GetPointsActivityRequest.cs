// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Admin-only: recent points-earning activity (top earners + large awards) for
    /// spotting abuse. 403s for non-admins, 404s gracefully on an older server.
    /// </summary>
    public class GetPointsActivityRequest : APIRequest<APIPointsActivity>
    {
        private readonly int days;

        public GetPointsActivityRequest(int days = 7)
        {
            this.days = days;
        }

        protected override string Target => $@"torii/points/admin/activity?days={days}&limit=25";
    }
}
