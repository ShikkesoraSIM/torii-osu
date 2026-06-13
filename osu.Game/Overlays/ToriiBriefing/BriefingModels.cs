// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Rulesets;

namespace osu.Game.Overlays.ToriiBriefing
{
    /// <summary>
    /// Mutable container that gathers the in-flight responses needed to
    /// display a single briefing. Lives only between dispatch and display —
    /// once the briefing is rendered (or abandoned), the instance is GC'd.
    /// </summary>
    /// <remarks>
    /// Only the user-stats and top-scores requests are blocking; the radar
    /// is purely additive UI, so the briefing renders as soon as the two
    /// blocking requests land and the radar fills in afterwards if it
    /// arrives. This shaves the slowest (or failing) request out of the
    /// critical path on login.
    /// </remarks>
    internal sealed class PendingBriefing
    {
        private int remainingBlockingRequests = 2;

        public readonly int RequestId;
        public readonly APIUser LocalUser;
        public readonly RulesetInfo Ruleset;
        public readonly bool UsePpDev;
        public readonly string SessionKey;

        public APIUser User;
        public List<SoloScoreInfo> TopScores;
        public ToriiBriefingRadarResponse Radar;
        public bool IsComplete => remainingBlockingRequests <= 0;

        public PendingBriefing(int requestId, string sessionKey, APIUser localUser, RulesetInfo ruleset, bool usePpDev)
        {
            RequestId = requestId;
            SessionKey = sessionKey;
            LocalUser = localUser;
            Ruleset = ruleset;
            UsePpDev = usePpDev;
        }

        public void MarkBlockingComplete() => remainingBlockingRequests--;
    }

    /// <summary>
    /// Render-ready briefing data. Composed from the live API responses
    /// in <see cref="PendingBriefing"/> and the local snapshot history,
    /// then handed to the overlay's view code.
    /// </summary>
    internal sealed class BriefingPayload
    {
        public APIUser User;
        public RulesetInfo Ruleset;
        public string Variant;
        public BriefingSnapshot Current;
        public BriefingSnapshot Previous;
        public List<BriefingScoreChange> ScoreChanges;
        public List<BriefingMessage> UnreadMessages;
        public List<BriefingRadarEvent> RadarEvents;
        public bool RadarFirstSnapshot;
        public int RadarTrackedCount;
    }

    /// <summary>
    /// Persisted snapshot index, keyed by <c>{userId}:{rulesetShortName}:{variant}</c>.
    /// Stored as <c>briefing-state.json</c> inside the Torii storage folder.
    /// </summary>
    internal sealed class BriefingState
    {
        [JsonProperty("users")]
        public Dictionary<string, BriefingSnapshot> Users { get; set; } = new Dictionary<string, BriefingSnapshot>();

        /// <summary>
        /// Tracks one-shot pp-dev promotion migrations that have already been
        /// consumed (so we don't repeatedly resurrect the user's pre-promotion
        /// stable snapshot as the comparison baseline).
        /// </summary>
        [JsonProperty("consumed_promotion_migrations")]
        public HashSet<string> ConsumedPromotionMigrations { get; set; } = new HashSet<string>();
    }

    /// <summary>
    /// The most recently displayed briefing, persisted so
    /// <c>ShowLastBriefing()</c> works after a restart even if the API
    /// is unreachable.
    /// </summary>
    internal sealed class StoredBriefing
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Ruleset { get; set; }
        public string Variant { get; set; }
        public BriefingSnapshot Current { get; set; }
        public BriefingSnapshot Previous { get; set; }
        public List<BriefingScoreChange> ScoreChanges { get; set; } = new List<BriefingScoreChange>();
        public List<BriefingMessage> UnreadMessages { get; set; } = new List<BriefingMessage>();
        public List<BriefingRadarEvent> RadarEvents { get; set; } = new List<BriefingRadarEvent>();
        public bool RadarFirstSnapshot { get; set; }
        public int RadarTrackedCount { get; set; }
    }

    /// <summary>
    /// A point-in-time snapshot of one user's stats + top-play set,
    /// used as the comparison baseline for the next briefing.
    /// </summary>
    internal sealed class BriefingSnapshot
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Ruleset { get; set; }
        public string Variant { get; set; }
        public DateTimeOffset CapturedAt { get; set; }
        public int? GlobalRank { get; set; }
        public int? CountryRank { get; set; }
        public double? PP { get; set; }
        public List<BriefingScoreSnapshot> TopScores { get; set; } = new List<BriefingScoreSnapshot>();
    }

    /// <summary>
    /// Slim score snapshot — only the fields needed to detect recalcs
    /// (delta on PP) and render the recalc list.
    /// </summary>
    internal sealed class BriefingScoreSnapshot
    {
        public ulong ScoreId { get; set; }
        public string Title { get; set; }
        public string Rank { get; set; }
        public double? PP { get; set; }
        public double Accuracy { get; set; }
    }

    /// <summary>One row in the recalc card. Computed at briefing time, never stored on its own.</summary>
    internal sealed class BriefingScoreChange
    {
        public string Title { get; set; }
        public double OldPP { get; set; }
        public double NewPP { get; set; }
        public double Delta { get; set; }
    }

    /// <summary>One unread chat ping shown in the dojo whispers card.</summary>
    internal sealed class BriefingMessage
    {
        public string Sender { get; set; }
        public string Channel { get; set; }
        public string Preview { get; set; }
    }

    /// <summary>One dojo radar event (snipe / leaderboard movement) shown in the radar card.</summary>
    internal sealed class BriefingRadarEvent
    {
        public string Title { get; set; }
        public string Detail { get; set; }
        public string Severity { get; set; }
    }
}
