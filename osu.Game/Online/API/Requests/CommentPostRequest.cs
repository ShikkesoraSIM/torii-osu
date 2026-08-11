// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    public class CommentPostRequest : APIRequest<CommentBundle>
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

        public readonly CommentableType Commentable;
        public readonly long CommentableId;
        public readonly string Message;
        public readonly long? ParentCommentId;

        public CommentPostRequest(CommentableType commentable, long commentableId, string message, long? parentCommentId = null)
        {
            Commentable = commentable;
            CommentableId = commentableId;
            Message = message;
            ParentCommentId = parentCommentId;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;

            req.AddParameter(@"comment[commentable_type]", Commentable.ToString().ToLowerInvariant());
            req.AddParameter(@"comment[commentable_id]", $"{CommentableId}");
            req.AddParameter(@"comment[message]", Message);
            if (ParentCommentId.HasValue)
                req.AddParameter(@"comment[parent_id]", $"{ParentCommentId}");

            return req;
        }

        protected override string Target => "comments";
    }
}
