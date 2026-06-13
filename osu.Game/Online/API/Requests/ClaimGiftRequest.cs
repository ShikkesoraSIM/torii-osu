// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using Newtonsoft.Json;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>Claim a pending gift: the server awards the points and returns
    /// the cosmetic ids to unlock locally.</summary>
    public class ClaimGiftRequest : APIRequest<APIGiftClaim>
    {
        private readonly int giftId;

        public ClaimGiftRequest(int giftId)
        {
            this.giftId = giftId;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;
            req.ContentType = @"application/json";
            req.AddRaw(JsonConvert.SerializeObject(new { gift_id = giftId }));
            return req;
        }

        protected override string Target => @"torii/gifts/claim";
    }
}
