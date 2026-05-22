// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using Newtonsoft.Json;

namespace osu.Game.Online.API.Requests.Responses
{
    /// <summary>
    /// Subset of the Torii server's per-user preference bag (the schema
    /// the website's Settings → User Preferences page reads/writes).
    ///
    /// Only the fields the client currently surfaces in-game are
    /// modelled here. The server returns the full preference object on
    /// <c>GET /api/private/user/preferences</c> — every other key just
    /// deserialises to no-op and PATCH-time we only send the fields we
    /// actually changed, so leaving the rest unmodelled doesn't lose any
    /// of the user's other preferences (volunteers, scoring mode, list
    /// view, etc.).
    ///
    /// Nullable bool because the PATCH endpoint accepts partial updates:
    /// when sending we want missing fields to ABSENT in the JSON (not
    /// <c>false</c>), so the server doesn't overwrite untouched
    /// preferences. <see cref="JsonConvert.SerializeObject"/> on a
    /// nullable bool with default <c>NullValueHandling.Include</c>
    /// surfaces null which the server tolerates — and on the receive
    /// side, missing keys become null on this POCO without crashing.
    /// </summary>
    public class APIToriiUserPreferences
    {
        /// <summary>
        /// When <c>true</c>, the server serves the real avatar / cover URLs
        /// for users whose profile is marked NSFW. When <c>false</c>
        /// (server default), <c>apply_nsfw_media_policy</c> on the server
        /// substitutes placeholders before the API response is shipped
        /// to this client.
        ///
        /// The flag is per-viewer (your preference about what YOU see),
        /// not per-author (the other side is <c>User.avatar_nsfw</c> /
        /// <c>User.cover_nsfw</c>).
        /// </summary>
        [JsonProperty("profile_media_show_nsfw")]
        public bool? ProfileMediaShowNsfw { get; set; }
    }
}
