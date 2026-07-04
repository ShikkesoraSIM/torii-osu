// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>Response of submitting a replay render to the server's o!rdr proxy.</summary>
    public class APIReplayRenderSubmission
    {
        [JsonProperty("render_id")]
        public long RenderId { get; set; }

        [JsonProperty("message")]
        public string? Message { get; set; }

        /// <summary>Per-user cooldown applied by the server (seconds).</summary>
        [JsonProperty("cooldown_seconds")]
        public int CooldownSeconds { get; set; }
    }

    /// <summary>Live status of an o!rdr render, proxied by the server.</summary>
    public class APIReplayRenderStatus
    {
        [JsonProperty("render_id")]
        public long RenderId { get; set; }

        /// <summary>Human-readable progress from o!rdr ("Waiting in queue...", "Rendering (45%)").</summary>
        [JsonProperty("progress")]
        public string? Progress { get; set; }

        /// <summary>The final share link. Null until the render completes.</summary>
        [JsonProperty("video_url")]
        public string? VideoUrl { get; set; }

        [JsonProperty("removed")]
        public bool Removed { get; set; }

        [JsonProperty("error_code")]
        public int ErrorCode { get; set; }

        [JsonProperty("error_message")]
        public string? ErrorMessage { get; set; }

        public bool IsDone => !string.IsNullOrEmpty(VideoUrl);

        public bool IsFailed => ErrorCode != 0 || Removed;
    }

    /// <summary>Remaining per-user render cooldown.</summary>
    public class APIReplayRenderCooldown
    {
        [JsonProperty("seconds_remaining")]
        public int SecondsRemaining { get; set; }
    }
}
