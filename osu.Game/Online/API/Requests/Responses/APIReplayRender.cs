// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
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

        /// <summary>Name of the o!rdr host machine rendering this replay (e.g. "Phil's PC"). Null while queued.</summary>
        [JsonProperty("renderer")]
        public string? Renderer { get; set; }

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

    /// <summary>A single o!rdr skin as surfaced by the server's skin-search proxy.</summary>
    public class APIOrdrSkin
    {
        /// <summary>Internal skin id/name — this is what gets sent when rendering.</summary>
        [JsonProperty("skin")]
        public string Skin { get; set; } = string.Empty;

        /// <summary>Pretty display name (e.g. "WhiteCat (CK 2.0)").</summary>
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        /// <summary>Server-proxied PNG preview URL (loadable in-client — it's on our trusted host).</summary>
        [JsonProperty("preview")]
        public string? Preview { get; set; }

        /// <summary>Raw o!rdr high-res preview URL — opened in the browser by the "eye" button.</summary>
        [JsonProperty("high_res")]
        public string? HighRes { get; set; }

        [JsonProperty("author")]
        public string? Author { get; set; }

        [JsonProperty("times_used")]
        public long TimesUsed { get; set; }
    }

    public class APIOrdrSkinList
    {
        [JsonProperty("skins")]
        public List<APIOrdrSkin> Skins { get; set; } = new List<APIOrdrSkin>();
    }

    /// <summary>One of the local user's past renders (for the "recent renders" list).</summary>
    public class APIMyReplayRender
    {
        [JsonProperty("render_id")]
        public long RenderId { get; set; }

        [JsonProperty("beatmap_title")]
        public string? BeatmapTitle { get; set; }

        [JsonProperty("status")]
        public string? Status { get; set; }

        [JsonProperty("video_url")]
        public string? VideoUrl { get; set; }

        [JsonProperty("share")]
        public bool Share { get; set; }

        [JsonProperty("created_at")]
        public string? CreatedAt { get; set; }
    }

    public class APIMyReplayRenders
    {
        [JsonProperty("renders")]
        public List<APIMyReplayRender> Renders { get; set; } = new List<APIMyReplayRender>();
    }
}
