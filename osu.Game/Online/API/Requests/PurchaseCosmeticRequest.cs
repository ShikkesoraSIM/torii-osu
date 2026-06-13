// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using Newtonsoft.Json;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Spends points server-side to own a cosmetic. The server is authoritative: it
    /// deducts from the real balance and records ownership, and the response carries
    /// the reconciled balance. Idempotent server-side, so a re-buy never double-charges.
    /// </summary>
    public class PurchaseCosmeticRequest : APIRequest<APIPurchaseResult>
    {
        private readonly string cosmeticId;
        private readonly int price;

        public PurchaseCosmeticRequest(string cosmeticId, int price)
        {
            this.cosmeticId = cosmeticId;
            this.price = price;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;
            req.ContentType = @"application/json";
            req.AddRaw(JsonConvert.SerializeObject(new { cosmetic_id = cosmeticId, price }));
            return req;
        }

        protected override string Target => @"torii/store/purchase";
    }
}
