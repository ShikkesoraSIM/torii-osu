// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using osu.Framework.IO.Network;

namespace osu.Game.Online.API.Requests
{
    public class RegisterRhythiaMapRequest : APIRequest
    {
        private readonly int mapId;

        public RegisterRhythiaMapRequest(int mapId)
        {
            this.mapId = mapId;
        }

        protected override string Target => $@"rhythia/maps/{mapId}/register";

        protected override WebRequest CreateWebRequest()
        {
            var request = base.CreateWebRequest();
            request.Method = HttpMethod.Post;
            return request;
        }
    }
}
