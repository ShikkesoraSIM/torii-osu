// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using osu.Framework.IO.Network;
using osu.Game.Rulesets;

namespace osu.Game.Online.API.Requests
{
    public class GetToriiBriefingRadarRequest : APIRequest<ToriiBriefingRadarResponse>
    {
        private readonly RulesetInfo ruleset;
        private readonly int trackTop;
        private readonly int maxEvents;

        public GetToriiBriefingRadarRequest(RulesetInfo ruleset, int trackTop = 5, int maxEvents = 8)
        {
            this.ruleset = ruleset;
            this.trackTop = trackTop;
            this.maxEvents = maxEvents;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.AddParameter("mode", ruleset.ShortName);
            req.AddParameter("track_top", trackTop.ToString());
            req.AddParameter("max_events", maxEvents.ToString());
            req.AddToriiPpVariantIfEnabled(API);
            return req;
        }

        protected override string Target => @"torii/briefing/radar";
    }

    public class ToriiBriefingRadarResponse
    {
        [JsonProperty("mode")]
        public string Mode { get; set; } = string.Empty;

        [JsonProperty("variant")]
        public string Variant { get; set; } = string.Empty;

        [JsonProperty("captured_at")]
        public DateTimeOffset CapturedAt { get; set; }

        [JsonProperty("first_snapshot")]
        public bool FirstSnapshot { get; set; }

        [JsonProperty("tracked_count")]
        public int TrackedCount { get; set; }

        [JsonProperty("events")]
        public List<ToriiBriefingRadarEvent> Events { get; set; } = new List<ToriiBriefingRadarEvent>();
    }

    public class ToriiBriefingRadarEvent
    {
        [JsonProperty("type")]
        public string Type { get; set; } = string.Empty;

        [JsonProperty("severity")]
        public string Severity { get; set; } = string.Empty;

        [JsonProperty("beatmap_id")]
        public long? BeatmapId { get; set; }

        [JsonProperty("score_id")]
        public long? ScoreId { get; set; }

        [JsonProperty("previous_position")]
        public int? PreviousPosition { get; set; }

        [JsonProperty("current_position")]
        public int? CurrentPosition { get; set; }

        [JsonProperty("actor_user_id")]
        public long? ActorUserId { get; set; }

        [JsonProperty("actor_username")]
        public string ActorUsername { get; set; } = string.Empty;

        [JsonProperty("headline")]
        public string Headline { get; set; } = string.Empty;

        [JsonProperty("detail")]
        public string Detail { get; set; } = string.Empty;
    }
}
