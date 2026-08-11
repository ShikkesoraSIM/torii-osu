// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using Newtonsoft.Json;
using osu.Framework.IO.Network;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// torii: guarda el star-rating pick de ranked play del jugador (una vez por season).
    /// <c>POST /api/v2/torii/comfort-pick</c>. El server valida el piso anti-sandbag (422 si es
    /// muy bajo) y el gate de una-vez-por-season (409). No devuelve tipado; en exito el caller
    /// re-fetchea el floor para reflejar el nuevo estado.
    /// </summary>
    public class SetComfortPickRequest : APIRequest
    {
        private readonly int rulesetId;
        private readonly float starRating;

        /// <summary>
        /// torii: las mutaciones siguen siendo seriales. Ver APIRequest.AllowConcurrentExecution.
        ///
        /// La cola paraleliza para que las LECTURAS no hagan fila (abrir un perfil eran 15 GET en
        /// serie a 190 ms cada uno). Pero dos escrituras al mismo recurso encoladas juntas si
        /// dependen del orden: equipar tres auras seguidas mandaba tres PATCH a la vez y el server
        /// se quedaba con el que commiteara ultimo, que podia no ser el que el jugador eligio.
        /// </summary>
        public override bool AllowConcurrentExecution => false;

        public SetComfortPickRequest(int rulesetId, float starRating)
        {
            this.rulesetId = rulesetId;
            this.starRating = starRating;
        }

        protected override string Target => @"torii/comfort-pick";

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;
            req.ContentType = @"application/json";
            req.AddRaw(JsonConvert.SerializeObject(new { ruleset_id = rulesetId, star_rating = starRating }));
            return req;
        }
    }
}
