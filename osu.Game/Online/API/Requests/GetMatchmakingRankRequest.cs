// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// torii: trae el rango de ranked play del jugador (rating + placement) para el badge de la
    /// cola. <c>GET /api/v2/torii/comfort-pick/rank?ruleset_id=</c>.
    /// </summary>
    public class GetMatchmakingRankRequest : APIRequest<APIMatchmakingRank>
    {
        private readonly int rulesetId;

        public GetMatchmakingRankRequest(int rulesetId)
        {
            this.rulesetId = rulesetId;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.AddParameter(@"ruleset_id", rulesetId.ToString());
            return req;
        }

        protected override string Target => @"torii/comfort-pick/rank";
    }
}
