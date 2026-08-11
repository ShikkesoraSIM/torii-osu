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

        /// <summary>
        /// torii: las mutaciones siguen siendo seriales. Ver APIRequest.AllowConcurrentExecution.
        ///
        /// La cola paraleliza para que las LECTURAS no hagan fila (abrir un perfil eran 15 GET en
        /// serie a 190 ms cada uno). Pero dos escrituras al mismo recurso encoladas juntas si
        /// dependen del orden: equipar tres auras seguidas mandaba tres PATCH a la vez y el server
        /// se quedaba con el que commiteara ultimo, que podia no ser el que el jugador eligio.
        /// </summary>
        public override bool AllowConcurrentExecution => false;

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
