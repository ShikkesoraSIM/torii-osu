// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Development;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Select.Leaderboards;
using Realms;

namespace osu.Game.Online.Leaderboards
{
    public partial class LeaderboardManager : Component
    {
        /// <summary>
        /// The latest leaderboard scores fetched by the criteria in <see cref="CurrentCriteria"/>.
        /// </summary>
        public IBindable<LeaderboardScores?> Scores => scores;

        private readonly Bindable<LeaderboardScores?> scores = new Bindable<LeaderboardScores?>();

        public LeaderboardCriteria? CurrentCriteria { get; private set; }

        private IDisposable? localScoreSubscription;
        private IDisposable? beatmapUpdateSubscription;
        private GetScoresRequest? inFlightOnlineRequest;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        /// <summary>
        /// Fetch leaderboard content with the new criteria specified in the background.
        /// On completion, <see cref="Scores"/> will be updated with the results from this call (unless a more recent call with a different criteria has completed).
        /// </summary>
        public void FetchWithCriteria(LeaderboardCriteria newCriteria, bool forceRefresh = false)
        {
            if (!ThreadSafety.IsUpdateThread)
                throw new InvalidOperationException(@$"{nameof(FetchWithCriteria)} must be called from the update thread.");

            if (!forceRefresh && CurrentCriteria?.Equals(newCriteria) == true && scores.Value?.FailState == null)
                return;

            CurrentCriteria = newCriteria;
            localScoreSubscription?.Dispose();
            beatmapUpdateSubscription?.Dispose();
            beatmapUpdateSubscription = null;
            inFlightOnlineRequest?.Cancel();
            scores.Value = null;

            if (newCriteria.Beatmap == null || newCriteria.Ruleset == null)
            {
                scores.Value = LeaderboardScores.Failure(LeaderboardFailState.NoneSelected);
                return;
            }

            switch (newCriteria.Scope)
            {
                case BeatmapLeaderboardScope.Local:
                {
                    localScoreSubscription = realm.RegisterForNotifications(r =>
                        r.All<ScoreInfo>().Filter($"{nameof(ScoreInfo.BeatmapInfo)}.{nameof(BeatmapInfo.ID)} == $0"
                                                  + $" AND {nameof(ScoreInfo.BeatmapInfo)}.{nameof(BeatmapInfo.Hash)} == {nameof(ScoreInfo.BeatmapHash)}"
                                                  + $" AND {nameof(ScoreInfo.Ruleset)}.{nameof(RulesetInfo.ShortName)} == $1"
                                                  + $" AND {nameof(ScoreInfo.DeletePending)} == false"
                            , newCriteria.Beatmap.ID, newCriteria.Ruleset.ShortName), localScoresChanged);
                    return;
                }

                default:
                {
                    if (newCriteria.Sorting != LeaderboardSortMode.Score)
                        throw new NotSupportedException($@"Requesting online scores with a {nameof(LeaderboardSortMode)} other than {nameof(LeaderboardSortMode.Score)} is not supported");

                    if (!api.IsLoggedIn)
                    {
                        scores.Value = LeaderboardScores.Failure(LeaderboardFailState.NotLoggedIn);
                        return;
                    }

                    // Allow Rhythia beatmaps to bypass checks
                    bool isRhythiaBeatmap = newCriteria.Beatmap.Metadata?.Tags?.Contains("sspm", StringComparison.OrdinalIgnoreCase) == true;

                    if (!isRhythiaBeatmap)
                    {
                        if (!newCriteria.Ruleset.IsLegacyRuleset())
                        {
                            scores.Value = LeaderboardScores.Failure(LeaderboardFailState.RulesetUnavailable);
                            return;
                        }

                        int onlineID = newCriteria.Beatmap.OnlineID;
                        BeatmapOnlineStatus status = newCriteria.Beatmap.Status;

                        // Only touch realm when the snapshot we were handed already looks unavailable. For an
                        // eligible (ranked/loved/...) map -- the common case while browsing song select -- this
                        // stays a pure in-memory check, so switching songs costs no realm read. A freshly
                        // downloaded map shows up here as Status = None / OnlineID = 0 because its online
                        // metadata lookup finishes AFTER the working beatmap was resolved; only then do we
                        // re-read the live values from realm (and heal the stale snapshot) so we don't wrongly
                        // report "not available". If the lookup is still in flight, watch realm and refetch the
                        // moment it lands, instead of only resolving after switching maps and back.
                        if (onlineID <= 0 || status <= BeatmapOnlineStatus.Pending)
                            (onlineID, status) = resolveLiveBeatmapStatus(newCriteria.Beatmap);

                        if (onlineID <= 0 || status <= BeatmapOnlineStatus.Pending)
                        {
                            scores.Value = LeaderboardScores.Failure(LeaderboardFailState.BeatmapUnavailable);
                            watchForBeatmapStatusUpdate(newCriteria);
                            return;
                        }
                    }

                    if ((newCriteria.Scope.RequiresSupporter(newCriteria.ExactMods != null)) && !api.LocalUser.Value.IsSupporter)
                    {
                        scores.Value = LeaderboardScores.Failure(LeaderboardFailState.NotSupporter);
                        return;
                    }

                    if (newCriteria.Scope == BeatmapLeaderboardScope.Team && api.LocalUser.Value.Team == null)
                    {
                        scores.Value = LeaderboardScores.Failure(LeaderboardFailState.NoTeam);
                        return;
                    }

                    IReadOnlyList<Mod>? requestMods = null;

                    if (newCriteria.ExactMods != null)
                    {
                        if (!newCriteria.ExactMods.Any())
                            // add nomod for the request
                            requestMods = new Mod[] { new ModNoMod() };
                        else
                            requestMods = newCriteria.ExactMods;
                    }

                    var newRequest = new GetScoresRequest(newCriteria.Beatmap, newCriteria.Ruleset, newCriteria.Scope, requestMods);
                    newRequest.Success += response =>
                    {
                        if (inFlightOnlineRequest != null && !newRequest.Equals(inFlightOnlineRequest))
                            return;

                        var result = LeaderboardScores.Success
                        (
                            response.Scores.Select(s => s.ToScoreInfo(rulesets, newCriteria.Beatmap))
                                    .OrderByTotalScore()
                                    .Select((s, idx) =>
                                    {
                                        s.Position = idx + 1;
                                        return s;
                                    })
                                    .ToArray(),
                            scoresRequested: newRequest.ScoresRequested,
                            totalScores: response.ScoresCount,
                            response.UserScore?.CreateScoreInfo(rulesets, newCriteria.Beatmap)
                        );
                        inFlightOnlineRequest = null;
                        scores.Value = result;
                    };
                    newRequest.Failure += ex =>
                    {
                        Logger.Log($@"Failed to fetch leaderboards when displaying results: {ex}", LoggingTarget.Network);
                        if (ex is not OperationCanceledException)
                            scores.Value = LeaderboardScores.Failure(LeaderboardFailState.NetworkFailure);
                    };

                    api.Queue(inFlightOnlineRequest = newRequest);
                    break;
                }
            }
        }

        private void localScoresChanged(IRealmCollection<ScoreInfo> sender, ChangeSet? changes)
        {
            Debug.Assert(CurrentCriteria != null);

            // This subscription may fire from changes to linked beatmaps, which we don't care about.
            // It's currently not possible for a score to be modified after insertion, so we can safely ignore callbacks with only modifications.
            if (changes?.HasCollectionChanges() == false)
                return;

            var newScores = sender.AsEnumerable();

            if (CurrentCriteria.ExactMods != null)
            {
                if (!CurrentCriteria.ExactMods.Any())
                {
                    // we need to filter out all scores that have any mods to get all local nomod scores
                    newScores = newScores.Where(s => !s.Mods.Any());
                }
                else
                {
                    // otherwise find all the scores that have all of the currently selected mods (similar to how web applies mod filters)
                    // we're creating and using a string HashSet representation of selected mods so that it can be translated into the DB query itself
                    var selectedMods = CurrentCriteria.ExactMods.Select(m => m.Acronym).ToHashSet();

                    newScores = newScores.Where(s => selectedMods.SetEquals(s.Mods.Select(m => m.Acronym)));
                }
            }

            newScores = newScores.Detach().OrderByCriteria(CurrentCriteria.Sorting);

            var newScoresArray = newScores.ToArray();
            scores.Value = LeaderboardScores.Success(newScoresArray, scoresRequested: newScoresArray.Length, totalScores: newScoresArray.Length, null);
        }

        private (int onlineID, BeatmapOnlineStatus status) resolveLiveBeatmapStatus(BeatmapInfo beatmapInfo)
        {
            int onlineID = beatmapInfo.OnlineID;
            BeatmapOnlineStatus status = beatmapInfo.Status;

            try
            {
                realm.Run(r =>
                {
                    BeatmapInfo? live = beatmapInfo.ID != Guid.Empty ? r.Find<BeatmapInfo>(beatmapInfo.ID) : null;

                    if (live == null && onlineID > 0)
                        live = r.All<BeatmapInfo>().FirstOrDefault(b => b.OnlineID == onlineID);

                    if (live == null)
                        return;

                    onlineID = live.OnlineID;
                    status = live.Status;

                    // Heal the detached snapshot in place so the score request and any other consumer of this
                    // BeatmapInfo see the resolved status. Managed (live) instances already reflect realm.
                    if (!beatmapInfo.IsManaged)
                    {
                        beatmapInfo.OnlineID = onlineID;
                        beatmapInfo.Status = status;
                    }
                });
            }
            catch (Exception e)
            {
                Logger.Log($@"Failed to refresh beatmap status from realm for leaderboard: {e}", LoggingTarget.Database);
            }

            return (onlineID, status);
        }

        private void watchForBeatmapStatusUpdate(LeaderboardCriteria criteria)
        {
            beatmapUpdateSubscription?.Dispose();
            beatmapUpdateSubscription = null;

            var beatmapInfo = criteria.Beatmap;
            if (beatmapInfo == null || beatmapInfo.ID == Guid.Empty)
                return;

            Guid id = beatmapInfo.ID;
            beatmapUpdateSubscription = realm.RegisterForNotifications(r => r.All<BeatmapInfo>().Where(b => b.ID == id), (sender, changes) =>
            {
                // Ignore the initial population callback; only react when the async metadata lookup actually
                // modifies the beatmap (online id / status landing after a fresh download).
                if (changes == null)
                    return;

                var live = sender.FirstOrDefault();
                if (live == null || live.OnlineID <= 0 || live.Status <= BeatmapOnlineStatus.Pending)
                    return;

                if (CurrentCriteria?.Equals(criteria) == true)
                    FetchWithCriteria(criteria, forceRefresh: true);
            });
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            localScoreSubscription?.Dispose();
            beatmapUpdateSubscription?.Dispose();
        }
    }

    public record LeaderboardCriteria(
        BeatmapInfo? Beatmap,
        RulesetInfo? Ruleset,
        BeatmapLeaderboardScope Scope,
        Mod[]? ExactMods,
        LeaderboardSortMode Sorting = LeaderboardSortMode.Score
    );

    public record LeaderboardScores
    {
        /// <summary>
        /// The collection of all scores received through the leaderboard lookup.
        /// </summary>
        public ICollection<ScoreInfo> TopScores { get; }

        /// <summary>
        /// The number of scores which was requested.
        /// Used to determine whether the returned leaderboard can be judged to be a partial or full leaderboard
        /// (i.e. whether <see cref="TopScores"/> contains all scores that it could ever contain).
        /// </summary>
        public int ScoresRequested { get; }

        /// <summary>
        /// The number of all scores that exist on the leaderboard.
        /// </summary>
        public int TotalScores { get; }

        public bool IsPartial => ScoresRequested < TotalScores;

        /// <summary>
        /// The local user's best score.
        /// </summary>
        public ScoreInfo? UserScore { get; }

        /// <summary>
        /// The failure state that occurred when attempting to retrieve the leaderboard.
        /// </summary>
        public LeaderboardFailState? FailState { get; }

        public IEnumerable<ScoreInfo> AllScores
        {
            get
            {
                foreach (var score in TopScores)
                    yield return score;

                if (UserScore != null && TopScores.All(topScore => !topScore.Equals(UserScore) && !topScore.MatchesOnlineID(UserScore)))
                    yield return UserScore;
            }
        }

        private LeaderboardScores(ICollection<ScoreInfo> topScores, int scoresRequested, int totalScores, ScoreInfo? userScore, LeaderboardFailState? failState)
        {
            TopScores = topScores;
            ScoresRequested = scoresRequested;
            TotalScores = totalScores;
            UserScore = userScore;
            FailState = failState;
        }

        public static LeaderboardScores Success(ICollection<ScoreInfo> topScores, int scoresRequested, int totalScores, ScoreInfo? userScore)
            => new LeaderboardScores(topScores, scoresRequested, totalScores, userScore, null);

        public static LeaderboardScores Failure(LeaderboardFailState failState)
            => new LeaderboardScores([], scoresRequested: 0, totalScores: 0, null, failState);
    }

    public enum LeaderboardFailState
    {
        NetworkFailure = -1,
        BeatmapUnavailable = -2,
        RulesetUnavailable = -3,
        NoneSelected = -4,
        NotLoggedIn = -5,
        NotSupporter = -6,
        NoTeam = -7
    }
}
