// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Screens;
using System.Globalization;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Online.Spectator;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking;
using osu.Game.Rulesets;

namespace osu.Game.Screens.Play
{
    /// <summary>
    /// A player instance which supports submitting scores to an online store.
    /// </summary>
    public abstract partial class SubmittingPlayer : Player
    {
        /// <summary>
        /// The token to be used for the current submission. This is fetched via a request created by <see cref="CreateTokenRequest"/>.
        /// </summary>
        private long? token;

        /// <summary>
        /// Tracks the async token retrieval kicked off in <see cref="LoadAsyncComplete"/>.
        /// Awaited from the (background) submission path so the load thread is never blocked
        /// on the API roundtrip. Blocking the load thread on retry caused audible audio pops
        /// while the new player instance was being prepared.
        /// </summary>
        private Task<bool> tokenRetrievalTask = Task.FromResult(false);

        [Resolved]
        private IAPIProvider api { get; set; }

        [Resolved]
        private SpectatorClient spectatorClient { get; set; }

        [Resolved]
        private SessionStatics statics { get; set; }

        [Resolved]
        private IRulesetConfigCache rulesetConfigCache { get; set; }

        [Resolved(canBeNull: true)]
        [CanBeNull]
        private UserStatisticsWatcher userStatisticsWatcher { get; set; }

        private readonly object scoreSubmissionLock = new object();
        private TaskCompletionSource<bool> scoreSubmissionSource;

        protected SubmittingPlayer(PlayerConfiguration configuration = null)
            : base(configuration)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (DrawableRuleset == null)
            {
                // base load must have failed (e.g. due to an unknown mod); bail.
                return;
            }

            AddInternal(new PlayerTouchInputDetector());

            // We probably want to move this display to something more global.
            // Probably using the OSD somehow.
            AddInternal(new GameplayOffsetControl
            {
                Margin = new MarginPadding(20),
                Anchor = Anchor.CentreRight,
                Origin = Anchor.CentreRight,
            });
        }

        protected override GameplayClockContainer CreateGameplayClockContainer(WorkingBeatmap beatmap, double gameplayStart) => new MasterGameplayClockContainer(beatmap, gameplayStart)
        {
            ShouldValidatePlaybackRate = true,
        };

        protected override void LoadAsyncComplete()
        {
            base.LoadAsyncComplete();
            tokenRetrievalTask = handleTokenRetrieval();
        }

        private Task<bool> handleTokenRetrieval()
        {
            // Token request construction should happen post-load to allow derived classes to potentially prepare DI backings that are used to create the request.
            var tcs = new TaskCompletionSource<bool>();

            if (Mods.Value.Any(m => !m.UserPlayable))
            {
                handleTokenFailure(new InvalidOperationException("Non-user playable mod selected."));
                return tcs.Task;
            }

            if (!api.IsLoggedIn)
            {
                handleTokenFailure(new InvalidOperationException("API is not online."));
                return tcs.Task;
            }

            var req = CreateTokenRequest();

            if (req == null)
            {
                handleTokenFailure(new InvalidOperationException("Request could not be constructed."));
                return tcs.Task;
            }

            req.Success += r =>
            {
                Logger.Log($"Score submission token retrieved ({r.ID})");
                token = r.ID;
                tcs.TrySetResult(true);
            };
            req.Failure += ex => handleTokenFailure(ex, displayNotification: true);

            api.Queue(req);

            // Don't block the load thread waiting for the API roundtrip — the submission path
            // (which already runs on a background Task.Run) awaits this with a generous timeout.
            // Blocking here used to cause audio pops on retry because LoadAsyncComplete sits on
            // the load thread that the audio engine shares for new-track preparation.
            Task.Delay(30000).ContinueWith(timeoutTask =>
            {
                _ = timeoutTask;
                if (!tcs.Task.IsCompleted)
                    req.TriggerFailure(new InvalidOperationException("Token retrieval timed out (request never run)"));
            }, TaskScheduler.Default).FireAndForget();

            return tcs.Task;

            void handleTokenFailure(Exception exception, bool displayNotification = false)
            {
                tcs.TrySetResult(false);

                bool shouldExit = ShouldExitOnTokenRetrievalFailure(exception);

                if (displayNotification || shouldExit)
                {
                    string whatWillHappen = shouldExit
                        ? "Play in this state is not permitted."
                        : "Your score will not be submitted.";

                    if (string.IsNullOrEmpty(exception.Message))
                        Logger.Error(exception, $"Failed to retrieve a score submission token.\n\n{whatWillHappen}");
                    else
                    {
                        switch (exception.Message)
                        {
                            case @"missing token header":
                            case @"invalid client hash":
                            case @"invalid verification hash":
                                Logger.Log($"Please ensure that you are using the latest version of the official game releases.\n\n{whatWillHappen}", level: LogLevel.Important);
                                break;

                            case @"invalid or missing beatmap_hash":
                                Logger.Log($"This beatmap does not match the online version. Please update or redownload it.\n\n{whatWillHappen}", level: LogLevel.Important);
                                break;

                            case @"expired token":
                                Logger.Log($"Your system clock is set incorrectly. Please check your system time, date and timezone.\n\n{whatWillHappen}", level: LogLevel.Important);
                                break;

                            default:
                                Logger.Log($"{whatWillHappen} {exception.Message}", level: LogLevel.Important);
                                break;
                        }
                    }
                }

                if (shouldExit)
                {
                    Schedule(() =>
                    {
                        ValidForResume = false;
                        this.Exit();
                    });
                }
            }
        }

        /// <summary>
        /// Called when a token could not be retrieved for submission.
        /// </summary>
        /// <param name="exception">The error causing the failure.</param>
        /// <returns>Whether gameplay should be immediately exited as a result. Returning false allows the gameplay session to continue. Defaults to true.</returns>
        protected virtual bool ShouldExitOnTokenRetrievalFailure(Exception exception) => true;

        public override bool AllowCriticalSettingsAdjustment
        {
            get
            {
                // General limitations to ensure players don't do anything too weird.
                // These match stable for now.

                // TODO: the blocking conditions should probably display a message.
                if (!IsBreakTime.Value && GameplayClockContainer.CurrentTime - GameplayClockContainer.GameplayStartTime > 10000)
                    return false;

                if (GameplayClockContainer.IsPaused.Value)
                    return false;

                return base.AllowCriticalSettingsAdjustment;
            }
        }

        protected override async Task PrepareScoreForResultsAsync(Score score)
        {
            await base.PrepareScoreForResultsAsync(score).ConfigureAwait(false);

            score.ScoreInfo.Date = DateTimeOffset.Now;

            await submitScore(score).ConfigureAwait(false);

            // Order matters here: RegisterForStatisticsUpdateAfter MUST run before EndPlaying.
            //
            // EndPlaying triggers the spectator hub's RegisterForSingleScoreAsync, which —
            // when the score has already finished server-side processing by the time it
            // arrives — dispatches UserScoreProcessed back to this client immediately.
            // Our handler (UserStatisticsWatcher.userScoreProcessed) looks the score up in
            // watchedScores[scoreId]; if we haven't registered yet it bails silently and
            // the rank/PP popup never fires.
            //
            // Upstream's order put EndPlaying first. That race manifested as ~50% missing
            // popups for plays that ended quickly (custom-rate DT, fast maps) — those have
            // the smallest window between the redis publish and the client's local hookup.
            userStatisticsWatcher?.RegisterForStatisticsUpdateAfter(score.ScoreInfo);
            spectatorClient.EndPlaying(GameplayState);
        }

        [Resolved]
        private RealmAccess realm { get; set; }

        protected override void StartGameplay()
        {
            base.StartGameplay();

            // User expectation is that last played should be updated when entering the gameplay loop
            // from multiplayer / playlists / solo.
            realm.WriteAsync(r =>
            {
                var realmBeatmap = r.Find<BeatmapInfo>(Beatmap.Value.BeatmapInfo.ID);
                if (realmBeatmap != null)
                    realmBeatmap.LastPlayed = DateTimeOffset.Now;
            });

            spectatorClient.BeginPlaying(token, GameplayState, Score);
        }

        public override bool Pause()
        {
            bool wasPaused = GameplayClockContainer.IsPaused.Value;

            bool paused = base.Pause();

            if (!wasPaused && paused)
                Score.ScoreInfo.Pauses.Add((int)Math.Round(GameplayClockContainer.CurrentTime));

            return paused;
        }

        protected override void ConcludeFailedScore(Score score)
        {
            base.ConcludeFailedScore(score);
            submitFromFailOrQuit(score);
        }

        public override bool OnExiting(ScreenExitEvent e)
        {
            bool exiting = base.OnExiting(e);
            submitFromFailOrQuit(Score);

            // Avoid deep-cloning score data on the update thread while the player screen is exiting.
            // Quits do not need to keep a cloned "last local score" around, which avoids a large allocation
            // right as we're transitioning back to menu.
            var scoreInfo = Score?.ScoreInfo;
            if (scoreInfo != null && !GameplayState.HasQuit)
            {
                Task.Run(() =>
                {
                    var scoreInfoCopy = scoreInfo.DeepClone();
                    Schedule(() => statics.SetValue(Static.LastLocalUserScore, scoreInfoCopy));
                }).FireAndForget();
            }
            else
                statics.SetValue<ScoreInfo?>(Static.LastLocalUserScore, null);

            return exiting;
        }

        private void submitFromFailOrQuit(Score score)
        {
            if (LoadedBeatmapSuccessfully)
            {
                spectatorClient.EndPlaying(GameplayState);

                if (GameplayState.HasQuit)
                {
                    Logger.Log("Skipping online score submission for quit.");
                    return;
                }

                Task.Run(async () =>
                {
                    // compare: https://github.com/ppy/osu/blob/ccf1acce56798497edfaf92d3ece933469edcf0a/osu.Game/Screens/Play/Player.cs#L848-L851
                    var scoreCopy = score.DeepClone();
                    await submitScore(scoreCopy).ConfigureAwait(false);
                }).FireAndForget();
            }
        }

        /// <summary>
        /// Construct a request to be used for retrieval of the score token.
        /// Can return null, at which point <see cref="ShouldExitOnTokenRetrievalFailure"/> will be fired.
        /// </summary>
        [CanBeNull]
        protected abstract APIRequest<APIScoreToken> CreateTokenRequest();

        /// <summary>
        /// Construct a request to submit the score.
        /// Will only be invoked if the request constructed via <see cref="CreateTokenRequest"/> was successful.
        /// </summary>
        /// <param name="score">The score to be submitted.</param>
        /// <param name="token">The submission token.</param>
        protected abstract APIRequest<MultiplayerScore> CreateSubmissionRequest(Score score, long token);

        private async Task submitScore(Score score)
        {
            var masterClock = GameplayClockContainer as MasterGameplayClockContainer;

            if (masterClock?.PlaybackRateValid.Value != true)
            {
                Logger.Log("Score submission cancelled due to audio playback rate discrepancy.");
                return;
            }

            // Wait for the (non-blocking) token retrieval kicked off in LoadAsyncComplete to finish.
            // In practice this is virtually always already complete by the time gameplay ends, but if
            // the API was slow we'd rather wait here (we're already on a background task) than block
            // the load thread up front.
            try
            {
                await tokenRetrievalTask.ConfigureAwait(false);
            }
            catch
            {
                // failure is logged via the request callback; fall through and check `token` below.
            }

            // token may be null if the request failed but gameplay was still allowed (see HandleTokenRetrievalFailure).
            if (token == null)
            {
                Logger.Log("No token, skipping score submission");
                return;
            }

            // if the user never hit anything, this score should not be counted in any way.
            if (!score.ScoreInfo.Statistics.Any(s => s.Key.IsHit() && s.Value > 0))
            {
                Logger.Log("No hits registered, skipping score submission");
                return;
            }

            // zero scores should also never be submitted.
            if (score.ScoreInfo.TotalScore == 0)
            {
                Logger.Log("Zero score, skipping score submission");
                return;
            }

            if (GameplayState.Ruleset?.RulesetInfo?.ShortName == "osuspaceruleset")
            {
                float hitWindow = 25f;
                float? configured = realm.Run(r =>
                {
                    var s = r.All<RealmRulesetSetting>()
                        .FirstOrDefault(x => x.RulesetName == "osuspaceruleset" && x.Key == "HitWindow");
                    if (s == null)
                        return (float?)null;
                    if (float.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
                        return parsed;
                    return (float?)null;
                });
                if (configured.HasValue)
                    hitWindow = configured.Value;

                if (hitWindow < 25f || hitWindow > 55f)
                {
                    Logger.Log($"Score submission cancelled due to invalid Space hitwindow ({hitWindow}ms).");
                    return;
                }
            }

            TaskCompletionSource<bool> submissionSource;
            Task<bool> existingSubmission = null;

            // mind the timing of this.
            // once `scoreSubmissionSource` is created, it is presumed that submission is taking place in the background,
            // so all exceptional circumstances that would disallow submission must be handled above.
            lock (scoreSubmissionLock)
            {
                if (scoreSubmissionSource != null)
                    existingSubmission = scoreSubmissionSource.Task;
                else
                    scoreSubmissionSource = new TaskCompletionSource<bool>();

                submissionSource = scoreSubmissionSource;
            }

            if (existingSubmission != null)
            {
                await existingSubmission.ConfigureAwait(false);
                return;
            }

            Logger.Log($"Beginning score submission (token:{token.Value})...");
            var request = CreateSubmissionRequest(score, token.Value);

            request.Success += s =>
            {
                score.ScoreInfo.OnlineID = s.ID;
                score.ScoreInfo.Position = s.Position;
                score.ScoreInfo.PP = s.PP;
                score.ScoreInfo.Ranked = s.Ranked;

                submissionSource.SetResult(true);
                Logger.Log($"Score submission completed! (token:{token.Value} id:{s.ID})");
            };

            request.Failure += e =>
            {
                Logger.Error(e, $"Failed to submit score (token:{token.Value}): {e.Message}");
                submissionSource.SetResult(false);
            };

            api.Queue(request);
            await submissionSource.Task.ConfigureAwait(false);
        }

        protected override ResultsScreen CreateResults(ScoreInfo score)
        {
            return new SoloResultsScreen(score)
            {
                AllowRetry = true,
                IsLocalPlay = true,
            };
        }
    }
}
