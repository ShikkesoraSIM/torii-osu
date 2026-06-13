// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using Newtonsoft.Json;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Redeem a Torii access code. The server awards any points and records the
    /// one-per-user redemption, returning the new balance and any cosmetic ids
    /// the code unlocks. 4xx (invalid / expired / already redeemed) surface via
    /// <see cref="APIRequest.Failure"/>.
    /// </summary>
    public class RedeemCodeRequest : APIRequest<APIRedeemResult>
    {
        private readonly string code;

        public RedeemCodeRequest(string code)
        {
            this.code = code;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;
            req.ContentType = @"application/json";
            req.AddRaw(JsonConvert.SerializeObject(new { code }));
            return req;
        }

        protected override string Target => @"torii/points/redeem";
    }
}
