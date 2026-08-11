// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using Newtonsoft.Json;
using osu.Framework.IO.Network;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Admin-only: send a gift (points and/or cosmetics) to a player by username
    /// or id. The recipient receives it after their next play. Server re-validates
    /// admin. 4xx (unknown user / not admin) surface via Failure.
    /// </summary>
    public class CreateGiftRequest : APIRequest
    {
        private readonly string recipient;
        private readonly int points;
        private readonly string[] cosmetics;
        private readonly string message;

        /// <summary>
        /// torii: las mutaciones siguen siendo seriales. Ver APIRequest.AllowConcurrentExecution.
        ///
        /// La cola paraleliza para que las LECTURAS no hagan fila (abrir un perfil eran 15 GET en
        /// serie a 190 ms cada uno). Pero dos escrituras al mismo recurso encoladas juntas si
        /// dependen del orden: equipar tres auras seguidas mandaba tres PATCH a la vez y el server
        /// se quedaba con el que commiteara ultimo, que podia no ser el que el jugador eligio.
        /// </summary>
        public override bool AllowConcurrentExecution => false;

        public CreateGiftRequest(string recipient, int points, string[] cosmetics, string message)
        {
            this.recipient = recipient;
            this.points = points;
            this.cosmetics = cosmetics;
            this.message = message;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;
            req.ContentType = @"application/json";
            req.AddRaw(JsonConvert.SerializeObject(new
            {
                recipient,
                points,
                grant_cosmetics = cosmetics != null && cosmetics.Length > 0 ? cosmetics : null,
                message = string.IsNullOrWhiteSpace(message) ? null : message,
            }));
            return req;
        }

        protected override string Target => @"torii/gifts/admin/create";
    }
}
