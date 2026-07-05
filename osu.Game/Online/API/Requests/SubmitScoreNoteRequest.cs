// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Crea/edita la nota de un score propio. Multipart: texto + imagen opcional
    /// (el server la procesa a un thumbnail liviano de 400x400).
    /// </summary>
    public class SubmitScoreNoteRequest : APIRequest<APIScoreNote>
    {
        private readonly long scoreId;
        private readonly string text;
        private readonly byte[]? imageBytes;
        private readonly bool removeImage;

        public SubmitScoreNoteRequest(long scoreId, string text, byte[]? imageBytes = null, bool removeImage = false)
        {
            this.scoreId = scoreId;
            this.text = text;
            this.imageBytes = imageBytes;
            this.removeImage = removeImage;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Put;
            req.AddParameter(@"text", text);
            if (imageBytes != null)
                req.AddFile(@"image", imageBytes, "note.png");
            if (removeImage)
                req.AddParameter(@"remove_image", @"true");
            return req;
        }

        protected override string Target => $@"torii/score-notes/{scoreId}";
    }
}
