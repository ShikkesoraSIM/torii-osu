// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using osu.Framework.IO.Network;

namespace osu.Game.Online.API.Requests
{
    public class UploadSSPMMapRequest : APIUploadRequest
    {
        private readonly byte[] fileBytes;
        private readonly string fileName;

        public UploadSSPMMapRequest(byte[] fileBytes, string fileName)
        {
            this.fileBytes = fileBytes;
            this.fileName = fileName;
        }

        protected override string Target => @"maps/import";

        protected override WebRequest CreateWebRequest()
        {
            var request = base.CreateWebRequest();
            request.Method = HttpMethod.Post;
            request.AddFile("file", fileBytes, fileName);
            return request;
        }
    }
}
