// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// torii: trae el piso anti-sandbag + estado del star-rating pick de ranked play para un ruleset.
    /// <c>GET /api/v2/torii/comfort-pick/floor?ruleset_id=</c>.
    /// </summary>
    public class GetComfortPickFloorRequest : APIRequest<APIComfortPickFloor>
    {
        private readonly int rulesetId;

        public GetComfortPickFloorRequest(int rulesetId)
        {
            this.rulesetId = rulesetId;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.AddParameter(@"ruleset_id", rulesetId.ToString());
            return req;
        }

        protected override string Target => @"torii/comfort-pick/floor";
    }
}
