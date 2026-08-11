// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using Newtonsoft.Json;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    public class VerifySessionRequest : APIRequest
    {
        /// <summary>
        /// torii: las mutaciones siguen siendo seriales. Ver APIRequest.AllowConcurrentExecution.
        ///
        /// La cola paraleliza para que las LECTURAS no hagan fila (abrir un perfil eran 15 GET en
        /// serie a 190 ms cada uno). Pero dos escrituras al mismo recurso encoladas juntas si
        /// dependen del orden: equipar tres auras seguidas mandaba tres PATCH a la vez y el server
        /// se quedaba con el que commiteara ultimo, que podia no ser el que el jugador eligio.
        /// </summary>
        public override bool AllowConcurrentExecution => false;

        public readonly string VerificationKey;

        public VerifySessionRequest(string verificationKey)
        {
            VerificationKey = verificationKey;

            Failure += _ =>
            {
                string? response = WebRequest?.GetResponseString();
                if (string.IsNullOrEmpty(response))
                    return;

                var responseObject = JsonConvert.DeserializeObject<VerificationFailureResponse>(response);
                RequiredVerificationMethod = responseObject?.RequiredSessionVerificationMethod;
            };
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();

            req.Method = HttpMethod.Post;
            req.AddParameter(@"verification_key", VerificationKey);

            return req;
        }

        protected override string Target => @"session/verify";

        public SessionVerificationMethod? RequiredVerificationMethod { get; internal set; }

        private class VerificationFailureResponse
        {
            [JsonProperty("method")]
            public SessionVerificationMethod? RequiredSessionVerificationMethod { get; set; }
        }
    }
}
