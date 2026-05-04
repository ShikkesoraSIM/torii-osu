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
        /// no plays have landed (gates are quiet — empty state).
        /// </summary>
        [JsonProperty("top_map")]
        public APIToriiServerPulseTopMap? TopMap { get; set; }

        /// <summary>
        /// 12 × 1-minute play-count buckets, oldest first — sparkline data
        /// for the popover's micro-graph. Always present, may be all
        /// zeros on a quiet server.
        /// </summary>
        [JsonProperty("sparkline")]
        public APIToriiServerPulseSparkline Sparkline { get; set; } = new APIToriiServerPulseSparkline();
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
