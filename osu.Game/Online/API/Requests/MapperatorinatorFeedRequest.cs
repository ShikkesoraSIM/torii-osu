// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using JetBrains.Annotations;
using Newtonsoft.Json;
using osu.Framework.IO.Network;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Tells the torii server that a Mapperatorinator map with a proper custom identity
    /// was generated, so it can announce it on the community feed. Only sent when the
    /// user bothered giving the map its own title, artist and background.
    /// </summary>
    public class MapperatorinatorFeedRequest : APIRequest
    {
        private readonly string title;
        private readonly string artist;

        [CanBeNull]
        private readonly string difficultyName;

        [CanBeNull]
        private readonly string model;

        public MapperatorinatorFeedRequest(string title, string artist, [CanBeNull] string difficultyName, [CanBeNull] string model)
        {
            this.title = title;
            this.artist = artist;
            this.difficultyName = difficultyName;
            this.model = model;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;
            req.ContentType = @"application/json";
            req.AddRaw(JsonConvert.SerializeObject(new
            {
                title,
                artist,
                difficulty_name = difficultyName,
                model,
            }));
            return req;
        }

        protected override string Target => @"torii/mapperatorinator/feed";
    }
}
