// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Extensions;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online;
using osu.Game.Rulesets;
using osu.Game.Scoring;

namespace osu.Game.Screens.Ranking.Statistics.User
{
    public partial class OverallRanking : CompositeDrawable
    {
        private const float transition_duration = 300;

        public Bindable<ScoreBasedUserStatisticsUpdate?> DisplayedUpdate { get; } = new Bindable<ScoreBasedUserStatisticsUpdate?>();
        private readonly IBindable<ScoreBasedUserStatisticsUpdate?> latestGlobalStatisticsUpdate = new Bindable<ScoreBasedUserStatisticsUpdate?>();

        private readonly ScoreInfo scoreInfo;

        private LoadingLayer loadingLayer = null!;
        private GridContainer content = null!;

        // Watchdog timers that protect against the spinner-forever
        // bug. The spectator-driven update path (UserStatisticsWatcher
        // listening for UserScoreProcessed) silently fails in several
        // documented races: the spectator hub's RegisterForSingleScore
        // can drop the subscription if g0v0 hasn't linked the score to
        // its token in time; SignalR can disconnect mid-play; the
        // OldStatistics-null branch can swallow the result; and
        // anything off the legacy-ruleset path bails before even
        // adding to watchedScores. So we run TWO fallback tiers.
        // See forceFallbackPopulate / hideSpinnerIfStillNothing.
        private ScheduledDelegate? primaryWatchdog;
        private ScheduledDelegate? secondaryWatchdog;

        // First-tier fallback fires this many ms after the panel
        // appears with no event. Picked to comfortably outlast the
        // spectator's own register-for-single-score retry window
        // (5s after the recent bump) plus typical processing time.
        private const int primary_watchdog_ms = 6000;

        // Second-tier safety net: even if our manual refetch never
        // returns (API blip, server down), bail out of the spinner
        // after this much further time. An empty panel is honest;
        // a forever spinner is a lie.
        private const int secondary_watchdog_ms = 6000;

        [Resolved(canBeNull: true)]
        private LocalUserStatisticsProvider? statisticsProvider { get; set; }

        public OverallRanking(ScoreInfo scoreInfo)
        {
            this.scoreInfo = scoreInfo;
        }

        [BackgroundDependencyLoader]
        private void load(UserStatisticsWatcher? userStatisticsWatcher)
        {
            AutoSizeAxes = Axes.Y;
            AutoSizeEasing = Easing.OutQuint;
            AutoSizeDuration = transition_duration;

            InternalChildren = new Drawable[]
            {
                loadingLayer = new LoadingLayer(withBox: false)
                {
                    RelativeSizeAxes = Axes.Both,
                },
                content = new GridContainer
                {
                    AlwaysPresent = true,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    ColumnDimensions = new[]
                    {
                        new Dimension(),
                        new Dimension(GridSizeMode.Absolute, 30),
                        new Dimension(),
                    },
                    RowDimensions = new[]
                    {
                        new Dimension(GridSizeMode.AutoSize),
                        new Dimension(GridSizeMode.Absolute, 10),
                        new Dimension(GridSizeMode.AutoSize),
                        new Dimension(GridSizeMode.Absolute, 10),
                        new Dimension(GridSizeMode.AutoSize),
                    },
                    Content = new[]
                    {
                        new Drawable[]
                        {
                            new GlobalRankChangeRow { StatisticsUpdate = { BindTarget = DisplayedUpdate } },
                            new SimpleStatisticTable.Spacer(),
                            new PerformancePointsChangeRow { StatisticsUpdate = { BindTarget = DisplayedUpdate } },
                        },
                        [],
                        new Drawable[]
                        {
                            new MaximumComboChangeRow { StatisticsUpdate = { BindTarget = DisplayedUpdate } },
                            new SimpleStatisticTable.Spacer(),
                            new AccuracyChangeRow { StatisticsUpdate = { BindTarget = DisplayedUpdate } },
                        },
                        [],
                        new Drawable[]
                        {
                            new RankedScoreChangeRow { StatisticsUpdate = { BindTarget = DisplayedUpdate } },
                            new SimpleStatisticTable.Spacer(),
                            new TotalScoreChangeRow { StatisticsUpdate = { BindTarget = DisplayedUpdate } },
                        }
                    }
                }
            };

            if (userStatisticsWatcher != null)
            {
                latestGlobalStatisticsUpdate.BindTo(userStatisticsWatcher.LatestUpdate);
                latestGlobalStatisticsUpdate.BindValueChanged(update =>
                {
                    if (update.NewValue?.Score.MatchesOnlineID(scoreInfo) == true)
                        DisplayedUpdate.Value = update.NewValue;
                }, true);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            DisplayedUpdate.BindValueChanged(onUpdateReceived, true);
            FinishTransforms(true);

            // Arm the primary watchdog. Cancelled the moment a real
            // update arrives (see onUpdateReceived); fires the manual
            // refetch fallback otherwise.
            primaryWatchdog = Scheduler.AddDelayed(forceFallbackPopulate, primary_watchdog_ms);
        }

        private void onUpdateReceived(ValueChangedEvent<ScoreBasedUserStatisticsUpdate?> update)
        {
            if (update.NewValue == null)
            {
                loadingLayer.Show();
                content.FadeOut(transition_duration, Easing.OutQuint);
            }
            else
            {
                // Real data landed (whether from the spectator event
                // or our own fallback) — disarm both watchdogs so we
                // don't double-populate or over-eagerly hide things.
                primaryWatchdog?.Cancel();
                secondaryWatchdog?.Cancel();

                loadingLayer.Hide();
                content.FadeIn(transition_duration, Easing.OutQuint);
            }
        }

        /// <summary>
        /// Spectator never delivered a stats update, so reach for the
        /// API directly via <see cref="LocalUserStatisticsProvider"/>.
        /// On success we synthesise a <see cref="ScoreBasedUserStatisticsUpdate"/>
        /// and feed it into the same <see cref="DisplayedUpdate"/>
        /// bindable the spectator path would have used.
        ///
        /// Fall-backs within the fall-back:
        /// - If the score's ruleset isn't a legacy one, we have no
        ///   stats to refetch (RefetchStatistics throws on non-legacy).
        ///   Just stop spinning — the rows have nothing to show
        ///   anyway.
        /// - If the API succeeds but the cached "before" stats are
        ///   null (first play of session in this ruleset), reuse the
        ///   freshly-fetched stats as Before too. The diff rows
        ///   render as "no change" — much better than null-deref or
        ///   a spinner that never ends.
        /// </summary>
        private void forceFallbackPopulate()
        {
            if (DisplayedUpdate.Value != null)
                return; // event landed in flight, nothing to do

            if (statisticsProvider == null || !scoreInfo.Ruleset.IsLegacyRuleset())
            {
                // Headless / non-legacy ruleset: no path forward.
                // Just hide the spinner and let the empty grid show.
                loadingLayer.Hide();
                return;
            }

            Logger.Log($"[overall-ranking] spectator never confirmed score {scoreInfo.OnlineID}; falling back to direct stats refetch.");

            statisticsProvider.RefetchStatistics(scoreInfo, u => Schedule(() =>
            {
                if (DisplayedUpdate.Value != null)
                    return; // spectator finally raced in, prefer its result

                // OldStatistics may be null if nothing was cached for
                // this ruleset prior. Defaulting before := after gives
                // a "no change" diff which is honest (we genuinely
                // don't know what before looked like) and keeps the
                // rows non-throwing.
                var before = u.OldStatistics ?? u.NewStatistics;
                DisplayedUpdate.Value = new ScoreBasedUserStatisticsUpdate(scoreInfo, before, u.NewStatistics);
            }));

            // Arm the second-tier safety net: if the refetch above
            // never lands either, give up cleanly instead of spinning.
            secondaryWatchdog = Scheduler.AddDelayed(hideSpinnerIfStillNothing, secondary_watchdog_ms);
        }

        private void hideSpinnerIfStillNothing()
        {
            if (DisplayedUpdate.Value != null)
                return;

            Logger.Log($"[overall-ranking] fallback refetch also never returned for score {scoreInfo.OnlineID}; abandoning spinner.");
            loadingLayer.Hide();
        }

        protected override void Dispose(bool isDisposing)
        {
            primaryWatchdog?.Cancel();
            secondaryWatchdog?.Cancel();
            base.Dispose(isDisposing);
        }
    }
}
