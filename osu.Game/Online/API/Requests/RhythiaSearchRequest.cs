// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    public class RhythiaSearchRequest : APIRequest<List<RhythiaBeatmapSet>>
    {
        private readonly string query;
        private readonly int page;

        public RhythiaSearchRequest(string query, int page)
        {
            this.query = query;
            this.page = page;
        }

        protected override string Target => $@"api/v2/rhythia/search?query={System.Uri.EscapeDataString(query)}&page={page}";
    }
}
