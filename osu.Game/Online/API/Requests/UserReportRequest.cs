// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using osu.Framework.IO.Network;
using osu.Game.Overlays.Profile;

namespace osu.Game.Online.API.Requests
{
    public class UserReportRequest : APIRequest
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

        public readonly int UserID;
        public readonly UserReportReason Reason;
        public readonly string Comment;

        public UserReportRequest(int userID, UserReportReason reason, string comment)
        {
            UserID = userID;
            Reason = reason;
            Comment = comment;
        }

        protected override WebRequest CreateWebRequest()
        {
            var request = base.CreateWebRequest();
            request.Method = HttpMethod.Post;

            request.AddParameter(@"reportable_type", @"user");
            request.AddParameter(@"reportable_id", $"{UserID}");
            request.AddParameter(@"reason", Reason.ToString());
            request.AddParameter(@"comments", Comment);

            return request;
        }

        protected override string Target => @"reports";
    }
}
