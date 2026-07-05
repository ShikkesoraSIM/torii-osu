// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>Una nota de score (texto corto + imagen opcional) hecha por el dueño de la play.</summary>
    public class APIScoreNote
    {
        [JsonProperty("score_id")]
        public long ScoreId { get; set; }

        [JsonProperty("user_id")]
        public int UserId { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; } = string.Empty;

        [JsonProperty("text")]
        public string Text { get; set; } = string.Empty;

        [JsonProperty("has_image")]
        public bool HasImage { get; set; }

        /// <summary>URL absoluta del thumbnail servido por nuestro server (cargable por la texture store).</summary>
        public string GetImageUrl(IAPIProvider api) => $@"{api.Endpoints.APIUrl}/api/v2/torii/score-notes/{ScoreId}/image";
    }

    public class APIScoreNoteList
    {
        [JsonProperty("notes")]
        public List<APIScoreNote> Notes { get; set; } = new List<APIScoreNote>();
    }
}
