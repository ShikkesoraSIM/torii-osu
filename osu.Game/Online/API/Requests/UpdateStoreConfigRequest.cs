// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Net.Http;
using Newtonsoft.Json;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Admin-only: replace the cosmetic store's disabled-id list. Body is JSON
    /// <c>{"disabled": ["id", ...]}</c>. Server validates admin status and
    /// returns the stored config. 403 if the caller isn't an admin.
    /// </summary>
    public class UpdateStoreConfigRequest : APIRequest<APIStoreConfig>
    {
        private readonly string[] disabled;

        public UpdateStoreConfigRequest(string[] disabled)
        {
            this.disabled = disabled ?? Array.Empty<string>();
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Put;
            req.ContentType = @"application/json";
            req.AddRaw(JsonConvert.SerializeObject(new { disabled }));
            return req;
        }

        protected override string Target => @"torii/store/admin/config";
    }
}
