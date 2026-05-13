// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    public class APIChangelogIndex
    {
        [JsonProperty("streams")]
        public List<APIUpdateStream> Streams { get; set; } = new List<APIUpdateStream>();

        [JsonProperty("builds")]
        public List<APIChangelogBuild> Builds { get; set; } = new List<APIChangelogBuild>();

        [JsonProperty("search")]
        public ChangelogSearch Search { get; set; } = new ChangelogSearch();

        [JsonProperty("cursor_string")]
        public string? CursorString { get; set; }
    }

    public class ChangelogSearch
    {
        [JsonProperty("stream")]
        public string? Stream { get; set; }

        [JsonProperty("from")]
        public string? From { get; set; }

        [JsonProperty("to")]
        public string? To { get; set; }

        [JsonProperty("limit")]
        public int Limit { get; set; }
    }

    public class ChangelogVersions
    {
        [JsonProperty("previous")]
        public APIChangelogBuild? Previous { get; set; }

        [JsonProperty("next")]
        public APIChangelogBuild? Next { get; set; }
    }

}
