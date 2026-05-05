// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>
    /// Response payload for <see cref="GetToriiServerPulseRequest"/>. Mirrors
    /// the JSON shape produced by <c>app/router/v2/torii_server_pulse.py</c>
    /// on the g0v0-server.
    ///
    /// Defensive defaults
    /// ------------------
    /// Every field gets a sane default (zeros, empty strings, null sub-
    /// objects) so a partially-populated payload (e.g. server hiccup, an
    /// older server build that doesn't yet emit the field) deserialises
    /// cleanly to a no-op render rather than crashing on missing
    /// properties. The pulse widget is best-effort decoration — a buggy
    /// server response should never tank the toolbar.
    /// </summary>
    public class APIToriiServerPulse
    {
        /// <summary>
        /// ISO-8601 timestamp of when the snapshot was computed on the
        /// server. Used by the popover footer's "updated Xs ago" stamp.
        /// </summary>
        [JsonProperty("captured_at")]
        public DateTimeOffset CapturedAt { get; set; }

        /// <summary>
        /// Number of in-flight gameplay sessions (open score tokens with
        /// no submitted score yet, capped to "started in the last 10 min"
        /// so crashed clients' stale tokens don't inflate the count).
        /// </summary>
        [JsonProperty("currently_playing")]
        public int CurrentlyPlaying { get; set; }

        /// <summary>
        /// Submitted scores in the most recent 1-minute window.
        /// </summary>
        [JsonProperty("plays_last_minute")]
        public int PlaysLastMinute { get; set; }

        /// <summary>
        /// Submitted scores in the most recent 5-minute window. Used for
        /// the popover's secondary "X plays/min average" line.
        /// </summary>
        [JsonProperty("plays_last_5min")]
        public int PlaysLast5Min { get; set; }

        /// <summary>
        /// Distinct online presences in the metadata Redis set. Same
        /// number that the website's home page / admin overview shows,
        /// so the two surfaces never disagree.
        /// </summary>
        [JsonProperty("online_users")]
        public int OnlineUsers { get; set; }

        /// <summary>
        /// Most-played beatmap of the last 5 minutes, or <c>null</c> when
        /// no plays have landed. Equivalent to <see cref="TopMaps"/>[0]
        /// — kept on the response for legacy clients that haven't picked
        /// up the multi-map list yet.
        /// </summary>
        [JsonProperty("top_map")]
        public APIToriiServerPulseTopMap? TopMap { get; set; }

        /// <summary>
        /// Top 5 most-played beatmaps of the last 5 minutes, ordered by
        /// play count desc. Empty list when the server has been quiet —
        /// the Hot Maps carousel page renders a calm empty state in
        /// that case. Older server responses that don't carry this
        /// field deserialise to an empty list (defensive default).
        /// </summary>
        [JsonProperty("top_maps")]
        public List<APIToriiServerPulseTopMap> TopMaps { get; set; } = new List<APIToriiServerPulseTopMap>();

        /// <summary>
        /// Per-ruleset count of currently in-flight plays. Keys are the
        /// ruleset ID as a string (<c>"0"</c> osu / <c>"1"</c> taiko /
        /// <c>"2"</c> catch / <c>"3"</c> mania) — matches the server-side
        /// JSON object key constraint. Modes with zero plays are absent
        /// from the dict; the Mode Split page treats missing as zero.
        /// </summary>
        [JsonProperty("mode_breakdown")]
        public Dictionary<string, int> ModeBreakdown { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Most-recently-started in-flight plays (up to 8) — powers the
        /// "Live Plays" carousel page. Each entry carries enough user +
        /// beatmap context to render a row without follow-up lookups.
        /// </summary>
        [JsonProperty("recent_plays")]
        public List<APIToriiServerPulseRecentPlay> RecentPlays { get; set; } = new List<APIToriiServerPulseRecentPlay>();

        /// <summary>
        /// 12 × 1-minute play-count buckets, oldest first — sparkline data
        /// for the popover's micro-graph. Always present, may be all
        /// zeros on a quiet server.
        /// </summary>
        [JsonProperty("sparkline")]
        public APIToriiServerPulseSparkline Sparkline { get; set; } = new APIToriiServerPulseSparkline();
    }

    /// <summary>
    /// A single recent in-flight play row. Mirrors the server-side
    /// <c>recent_plays[]</c> entry shape from
    /// <c>app/router/v2/torii_server_pulse.py:_compute_recent_plays</c>.
    ///
    /// "Recent" here means "the user opened a score token" — i.e. they
    /// just started playing and haven't submitted yet. The list refreshes
    /// every poll cycle, so the rows naturally rotate as new plays start
    /// and old ones complete (token gets a score_id and disappears from
    /// the in-flight set).
    /// </summary>
    public class APIToriiServerPulseRecentPlay
    {
        [JsonProperty("user_id")]
        public long UserId { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Avatar URL ready to feed straight into <c>LargeTextureStore</c>
        /// — the server resolves this against its own user model so we
        /// don't need a follow-up <c>/users/{id}</c> lookup just for the
        /// avatar.
        /// </summary>
        [JsonProperty("avatar_url")]
        public string AvatarUrl { get; set; } = string.Empty;

        [JsonProperty("beatmap_id")]
        public long BeatmapId { get; set; }

        [JsonProperty("beatmapset_id")]
        public long BeatmapSetId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("title_unicode")]
        public string TitleUnicode { get; set; } = string.Empty;

        [JsonProperty("artist")]
        public string Artist { get; set; } = string.Empty;

        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;

        [JsonProperty("ruleset_id")]
        public int RulesetId { get; set; }

        /// <summary>
        /// Discriminator: <c>"playing"</c> for in-flight (just started,
        /// no submitted score yet) or <c>"submitted"</c> for finished
        /// scores within the visibility window. The client renders
        /// different badge content for each status (time-elapsed for
        /// playing, pp + grade for submitted).
        ///
        /// Older server responses without this field default to
        /// "playing" — keeps the client behaviour stable when talking
        /// to a server that hasn't picked up the v4 schema yet.
        /// </summary>
        [JsonProperty("status")]
        public string Status { get; set; } = "playing";

        /// <summary>
        /// Seconds since the player started this attempt. Only set when
        /// <see cref="Status"/> = "playing".
        /// </summary>
        [JsonProperty("started_seconds_ago")]
        public int StartedSecondsAgo { get; set; }

        // ─── Submitted-status fields ─────────────────────────────────
        // These are only meaningful when Status == "submitted". JSON
        // defaults zero them out for "playing" rows; the client gates
        // rendering on the status discriminator before reading these.

        [JsonProperty("score_id")]
        public long ScoreId { get; set; }

        /// <summary>Seconds since the score was submitted.</summary>
        [JsonProperty("submitted_seconds_ago")]
        public int SubmittedSecondsAgo { get; set; }

        /// <summary>Total pp the score earned (after weighting / mods).</summary>
        [JsonProperty("pp")]
        public double Pp { get; set; }

        /// <summary>
        /// Change in the user's account-level pp this score introduced
        /// (<c>statistics.pp_after − pp_before</c>, clamped to ≥ 0).
        /// Captured at submission time and stored on the Score row, so
        /// reads are O(1). Used by the badge to render a secondary
        /// "+Xpp to total" line when this score actually moved the
        /// user's overall ranking pp; otherwise the badge shows just
        /// the score's pp value.
        /// </summary>
        [JsonProperty("account_pp_delta")]
        public double AccountPpDelta { get; set; }

        /// <summary>Accuracy as 0..1 (multiply by 100 for display).</summary>
        [JsonProperty("accuracy")]
        public double Accuracy { get; set; }

        [JsonProperty("max_combo")]
        public int MaxCombo { get; set; }

        /// <summary>
        /// Letter rank — "SS", "S", "A", "B", "C", "D", "F". Empty
        /// string when not provided (e.g. play just started).
        /// </summary>
        [JsonProperty("rank")]
        public string Rank { get; set; } = string.Empty;

        /// <summary>Unicode title with romanised fallback.</summary>
        public string DisplayTitle => !string.IsNullOrEmpty(TitleUnicode) ? TitleUnicode : Title;

        /// <summary>True for a finished score, false for an in-flight play.</summary>
        public bool IsSubmitted => string.Equals(Status, "submitted", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Top-played beatmap card data. Carries enough beatmapset metadata
    /// (covers, title, artist, version) for the client to render the card
    /// without a follow-up lookup.
    /// </summary>
    public class APIToriiServerPulseTopMap
    {
        [JsonProperty("beatmap_id")]
        public long BeatmapId { get; set; }

        [JsonProperty("beatmapset_id")]
        public long BeatmapSetId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; } = string.Empty;

        [JsonProperty("title_unicode")]
        public string TitleUnicode { get; set; } = string.Empty;

        [JsonProperty("artist")]
        public string Artist { get; set; } = string.Empty;

        [JsonProperty("artist_unicode")]
        public string ArtistUnicode { get; set; } = string.Empty;

        /// <summary>
        /// Difficulty name (e.g. "Insane", "Extreme"). Server calls this
        /// <c>version</c> matching the upstream osu! API convention.
        /// </summary>
        [JsonProperty("version")]
        public string Version { get; set; } = string.Empty;

        [JsonProperty("creator")]
        public string Creator { get; set; } = string.Empty;

        /// <summary>
        /// Beatmapset cover URL bundle. Same shape as the rest of the
        /// osu! API <c>covers</c> dict (<c>cover</c>, <c>cover@2x</c>,
        /// <c>card</c>, <c>card@2x</c>, <c>list</c>, <c>list@2x</c>,
        /// <c>slimcover</c>, <c>slimcover@2x</c>). Nullable — some legacy
        /// beatmapsets don't have populated covers.
        /// </summary>
        [JsonProperty("covers")]
        public Dictionary<string, string>? Covers { get; set; }

        /// <summary>
        /// How many plays this map received in the last 5 minutes.
        /// Drives the "X plays" badge on the card.
        /// </summary>
        [JsonProperty("play_count_5min")]
        public int PlayCount5Min { get; set; }

        /// <summary>
        /// 0=osu, 1=taiko, 2=catch, 3=mania. Used to colour the ruleset
        /// glyph next to the difficulty name.
        /// </summary>
        [JsonProperty("ruleset_id")]
        public int RulesetId { get; set; }

        [JsonProperty("star_rating")]
        public double StarRating { get; set; }

        /// <summary>
        /// Convenience: prefer the unicode title if set, else fall back to
        /// the romanised title.
        /// </summary>
        public string DisplayTitle => !string.IsNullOrEmpty(TitleUnicode) ? TitleUnicode : Title;

        /// <summary>
        /// Convenience: prefer the unicode artist if set, else fall back
        /// to the romanised artist.
        /// </summary>
        public string DisplayArtist => !string.IsNullOrEmpty(ArtistUnicode) ? ArtistUnicode : Artist;

        /// <summary>
        /// Best cover URL we have, picking the highest-resolution variant
        /// available. <c>cover@2x</c> &gt; <c>cover</c> &gt; <c>card@2x</c>
        /// &gt; <c>card</c>. Returns null when no cover is available at all.
        /// </summary>
        public string? BestCoverUrl
        {
            get
            {
                if (Covers == null) return null;

                foreach (string key in new[] { @"cover@2x", @"cover", @"card@2x", @"card", @"slimcover@2x", @"slimcover", @"list@2x", @"list" })
                {
                    if (Covers.TryGetValue(key, out string? url) && !string.IsNullOrEmpty(url))
                        return url;
                }

                return null;
            }
        }
    }

    /// <summary>
    /// Sparkline payload. Always carries the bucket geometry (count +
    /// seconds-per-bucket) so the client doesn't have to assume — change
    /// the server-side constants and the client picks them up.
    /// </summary>
    public class APIToriiServerPulseSparkline
    {
        [JsonProperty("bucket_seconds")]
        public int BucketSeconds { get; set; } = 60;

        [JsonProperty("bucket_count")]
        public int BucketCount { get; set; } = 12;

        /// <summary>
        /// Per-bucket play counts, oldest first. Length should equal
        /// <see cref="BucketCount"/>; defensive code in the consumer
        /// truncates / pads if the server ever lies.
        /// </summary>
        [JsonProperty("buckets")]
        public List<int> Buckets { get; set; } = new List<int>();
    }
}
