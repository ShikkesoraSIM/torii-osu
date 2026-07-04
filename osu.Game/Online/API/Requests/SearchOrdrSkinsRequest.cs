// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>Searches o!rdr's skin catalogue (via the server proxy) to back the render panel's skin picker.</summary>
    public class SearchOrdrSkinsRequest : APIRequest<APIOrdrSkinList>
    {
        private readonly string query;

        public SearchOrdrSkinsRequest(string query)
        {
            this.query = query;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            if (!string.IsNullOrWhiteSpace(query))
                req.AddParameter(@"search", query.Trim(), RequestParameterType.Query);
            return req;
        }

        protected override string Target => @"torii/replay-render/skins";
    }
}
