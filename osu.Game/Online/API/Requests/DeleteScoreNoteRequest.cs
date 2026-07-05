// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using osu.Framework.IO.Network;

namespace osu.Game.Online.API.Requests
{
    /// <summary>Borra la nota de un score propio.</summary>
    public class DeleteScoreNoteRequest : APIRequest
    {
        private readonly long scoreId;

        public DeleteScoreNoteRequest(long scoreId)
        {
            this.scoreId = scoreId;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Delete;
            return req;
        }

        protected override string Target => $@"torii/score-notes/{scoreId}";
    }
}
