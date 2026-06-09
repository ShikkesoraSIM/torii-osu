// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using Newtonsoft.Json;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Admin-only: mint a redeemable Torii access code worth a points amount.
    /// The server auto-generates the code string and re-validates admin status
    /// (403 otherwise). Used by the staff hub.
    /// </summary>
    public class CreateAccessCodeRequest : APIRequest<APIAccessCode>
    {
        private readonly int amount;
        private readonly int maxUses;
        private readonly string note;

        public CreateAccessCodeRequest(int amount, int maxUses, string note)
        {
            this.amount = amount;
            this.maxUses = maxUses;
            this.note = note;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;
            req.ContentType = @"application/json";
            req.AddRaw(JsonConvert.SerializeObject(new
            {
                amount,
                max_uses = maxUses,
                note = string.IsNullOrWhiteSpace(note) ? null : note,
            }));
            return req;
        }

        protected override string Target => @"torii/points/admin/codes";
    }
}
