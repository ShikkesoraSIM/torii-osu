// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.IO.Network;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// El star rating elegido de varios jugadores de una.
    /// </summary>
    /// <remarks>
    /// Lo pide el panel de ranked play para poner "Fulano 7.5" al lado de cada nombre en
    /// la cola. De a uno serian ocho pedidos para dibujar ocho nombres.
    /// </remarks>
    public class GetComfortPicksBulkRequest : APIRequest<GetComfortPicksBulkResponse>
    {
        private readonly int[] userIds;
        private readonly int rulesetId;

        public GetComfortPicksBulkRequest(int[] userIds, int rulesetId)
        {
            this.userIds = userIds;
            this.rulesetId = rulesetId;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.AddParameter(@"user_ids", string.Join(',', userIds));
            req.AddParameter(@"ruleset_id", rulesetId.ToString());
            return req;
        }

        protected override string Target => @"torii/comfort-pick/bulk";
    }

    public class GetComfortPicksBulkResponse
    {
        /// <summary>
        /// Id de usuario contra su star rating. Quien todavia no eligio NO aparece: el
        /// server no manda cero justamente para que no se lea como "eligio cero".
        /// </summary>
        [JsonProperty("picks")]
        public Dictionary<string, float> Picks { get; set; } = new Dictionary<string, float>();

        public float? For(int userId) => Picks.TryGetValue(userId.ToString(), out float sr) ? sr : null;
    }
}
