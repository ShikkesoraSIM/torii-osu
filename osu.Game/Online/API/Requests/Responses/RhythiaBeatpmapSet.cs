// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Collections.Generic;
using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    public class RhythiaBeatmapSet
    {
        [JsonProperty("id")]
        public int OnlineID { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("ownerUsername")]
        public string Author { get; set; }

        [JsonProperty("ownerUsername")]
        public string Artist { get; set; }

        [JsonProperty("starRating")]
        public double StarRating { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("beatmapFile")]
        public string BeatmapFile { get; set; }

        [JsonProperty("image")]
        public string Image { get; set; }

        [JsonProperty("image")]
        public string Preview { get; set; }

        [JsonProperty("image")]
        public string Background { get; set; }

        [JsonProperty("image")]
        public string Storyboard { get; set; }

        [JsonProperty("length")]
        public int Length { get; set; }

        [JsonProperty("bpm")]
        public double BPM { get; set; }

        [JsonProperty("playcount")]
        public int PlayCount { get; set; }

        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }

        [JsonProperty("tags")]
        public string Tags { get; set; }

        [JsonProperty("videoUrl")]
        public string VideoUrl { get; set; }

        [JsonProperty("ranked")]
        public bool Ranked { get; set; }

        [JsonProperty("difficulty")]
        public int Difficulty { get; set; }

        public List<RhythiaBeatmap> Beatmaps { get; set; } = new List<RhythiaBeatmap>();
    }

    public class RhythiaBeatmap
    {
        [JsonProperty("id")]
        public int OnlineID { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("difficulty")]
        public double Difficulty { get; set; }

        [JsonProperty("cs")]
        public double CS { get; set; }

        [JsonProperty("ar")]
        public double AR { get; set; }

        [JsonProperty("od")]
        public double OD { get; set; }

        [JsonProperty("hp")]
        public double HP { get; set; }

        [JsonProperty("length")]
        public int Length { get; set; }

        [JsonProperty("status")]
        public string Status { get; set; }

        [JsonProperty("checksum")]
        public string Checksum { get; set; }
    }
}
