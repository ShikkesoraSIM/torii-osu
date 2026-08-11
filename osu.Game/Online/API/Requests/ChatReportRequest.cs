// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using osu.Framework.IO.Network;
using osu.Game.Overlays.Chat;

namespace osu.Game.Online.API.Requests
{
    public class ChatReportRequest : APIRequest
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

        public readonly long? MessageId;
        public readonly ChatReportReason Reason;
        public readonly string Comment;

        public ChatReportRequest(long? id, ChatReportReason reason, string comment)
        {
            MessageId = id;
            Reason = reason;
            Comment = comment;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;

            req.AddParameter(@"reportable_type", @"message");
            req.AddParameter(@"reportable_id", $"{MessageId}");
            req.AddParameter(@"reason", Reason.ToString());
            req.AddParameter(@"comments", Comment);

            return req;
        }

        protected override string Target => @"reports";
    }
}
