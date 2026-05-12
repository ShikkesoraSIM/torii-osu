// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Screens.Play;

namespace osu.Game.Online.Server
{
    /// <summary>
    /// Singleton-ish polling provider that powers the toolbar
    /// <c>ToriiServerPulseButton</c> and its popover. Owns the polling
    /// cadence, the bindables that UI consumes, and the connection state
    /// machine — UI subscribes to bindables and never has to care about
    /// HTTP, retries, or auth state.
    ///
    /// Polling cadence
    /// ---------------
    /// Two cadences, switched explicitly by callers:
    ///   - <see cref="PollIntervalSecondsIdle"/> (60s) when the popover is
    ///     closed. The toolbar pip just shows the latest snapshot, so
    ///     freshness beyond a minute doesn't add value.
    ///   - <see cref="PollIntervalSecondsActive"/> (10s) while the popover
    ///     is open. The user is actively reading numbers, so we want them
    ///     to feel alive without hammering the server.
    /// Server-side cache TTL is also 10s, so the active cadence almost
    /// always reads warm cache — the DB only sees ~6 computes/min globally
    /// regardless of how many clients have the popover open simultaneously.
    ///
    /// Pause conditions
    /// ----------------
    /// Polling is suspended when:
    ///   - The user disabled the widget in settings (<c>OsuSetting.ToriiServerPulseEnabled</c>).
    ///   - The API is not in a "connected with auth" state. Pulse is a
    ///     ToriiSpecific endpoint, so polling without auth would just
    ///     cookie-check-fail in a loop. We watch <see cref="IAPIProvider.State"/>
    ///     and wake up automatically when auth flips back on.
    ///   - The user is in active gameplay (<see cref="ILocalUserPlayInfo.PlayingState"/>
    ///     == <see cref="LocalUserPlayingState.Playing"/>). The response
    ///     handler cascades through 9 bindables on the update thread,
    ///     and prod hiccup-report data showed individual pulse-response
    ///     frames stalling 200–500 ms mid-song. The cost was worse than
    ///     the value of fresh numbers between hits, so we trade pulse
    ///     freshness for zero in-game stutter.
    ///   - We're inside the <see cref="settle_window_ms"/> grace period
    ///     immediately after exiting Playing. The Player → SoloResultsScreen
    ///     transition is the single highest-cost frame in a typical session
    ///     (post-gameplay heap drop → Gen2 GC cascade → 600-700 ms stalls
    ///     in prod hiccup reports). Resuming the pulse poll in that exact
    ///     window stacks a 9-bindable cascade on top of the GC pause.
    ///     Holding off for a few seconds lets the heap settle first, after
    ///     which polling rearms automatically.
    ///
    /// Bindables exposed
    /// -----------------
    ///   - <see cref="CurrentlyPlaying"/>, <see cref="PlaysLastMinute"/>,
    ///     <see cref="PlaysLast5Min"/>, <see cref="OnlineUsers"/> — int counts.
    ///   - <see cref="TopMap"/> — nullable reference to the top-map struct.
    ///   - <see cref="Sparkline"/> — list of bucket counts.
    ///   - <see cref="LastUpdated"/> — DateTimeOffset of the last successful
    ///     snapshot (for the popover footer's "updated Xs ago").
    ///   - <see cref="ConnectionState"/> — enum: Idle / Connecting /
    ///     Connected / Offline / Disabled. Drives the connection pip in
    ///     the popover header.
    ///
    /// Events
    /// ------
    ///   - <see cref="PlayDetected"/> fires when <see cref="PlaysLastMinute"/>
    ///     ticks UP between two consecutive snapshots. The button uses it
    ///     to flash the heartbeat dot — visual feedback that the server
    ///     just registered a new play.
    ///
    /// Lifecycle
    /// ---------
    /// Component is registered as a cacheable dependency in OsuGame and
    /// kicks off polling on first load. <see cref="Component.Dispose"/>
    /// cancels the in-flight scheduler delegate so we don't leak the
    /// timer when the game shuts down.
    /// </summary>
    public partial class ToriiServerPulseProvider : Component
    {
        // Polling intervals. Tuned in tandem with the server's 10s cache
        // TTL (see torii_server_pulse.py): the active cadence matches the
        // cache window so concurrent active clients all hit warm cache.
        //
        // Idle cadence ("popover closed" — toolbar pip only) bumped from
        // 60s → 120s after live hiccup-report data showed the once-a-minute
        // response cascade was visible in idle-session timelines (a small
        // bindable cascade + log entry every 60 s). Doubling halves the
        // per-session pulse traffic while leaving the pip in sync with
        // whatever the server publishes (server itself has a 10 s cache
        // TTL + per-user activity smearing — sub-minute freshness was
        // always theatrical).
        public const int PollIntervalSecondsIdle = 120;
        public const int PollIntervalSecondsActive = 10;

        // Grace period after exiting LocalUserPlayingState.Playing during
        // which polling stays suspended. The post-gameplay heap drop (player
        // releases pooled HitObjects / audio buffers / replay frames /
        // hit-result containers in one frame) triggers Gen2 GC cascades on
        // the SoloResultsScreen entry frame — see hiccup-report data from
        // May 2026 showing 600-700 ms stalls with +gen2=9 deltas. Stacking
        // a pulse-response handler on top of that exact frame doubles the
        // damage. 4 seconds is long enough for the runtime to finish
        // collecting and short enough that the toolbar pip is fresh again
        // before the user has finished reading their results.
        private const int settle_window_ms = 4000;

        /// <summary>
        /// Number of in-flight gameplay sessions on the server (currently
        /// playing). Initial value 0; updates via <see cref="Bindable{T}.Value"/>
        /// after every successful poll.
        /// </summary>
        public IBindable<int> CurrentlyPlaying => currentlyPlaying;
        private readonly Bindable<int> currentlyPlaying = new Bindable<int>();

        /// <summary>Submitted scores in the last 1-minute window.</summary>
        public IBindable<int> PlaysLastMinute => playsLastMinute;
        private readonly Bindable<int> playsLastMinute = new Bindable<int>();

        /// <summary>Submitted scores in the last 5-minute window.</summary>
        public IBindable<int> PlaysLast5Min => playsLast5Min;
        private readonly Bindable<int> playsLast5Min = new Bindable<int>();

        /// <summary>Distinct online user count from the metadata Redis set.</summary>
        public IBindable<int> OnlineUsers => onlineUsers;
        private readonly Bindable<int> onlineUsers = new Bindable<int>();

        /// <summary>
        /// Most-played beatmap of the last 5 min. Null when no plays have
        /// landed (server returns null) — UI shows a calm empty state in
        /// that case. Equivalent to <see cref="TopMaps"/>[0] when the
        /// list is non-empty.
        /// </summary>
        public Bindable<APIToriiServerPulseTopMap?> TopMap { get; } = new Bindable<APIToriiServerPulseTopMap?>();

        /// <summary>
        /// Top 5 most-played beatmaps of the last 5 min, ordered by play
        /// count desc. Drives the Hot Maps carousel page.
        /// </summary>
        public Bindable<IReadOnlyList<APIToriiServerPulseTopMap>> TopMaps { get; } =
            new Bindable<IReadOnlyList<APIToriiServerPulseTopMap>>(Array.Empty<APIToriiServerPulseTopMap>());

        /// <summary>
        /// Per-ruleset in-flight play counts (keys are ruleset IDs as
        /// strings: <c>"0"</c>/<c>"1"</c>/<c>"2"</c>/<c>"3"</c>). Drives
        /// the Mode Split carousel page.
        /// </summary>
        public Bindable<IReadOnlyDictionary<string, int>> ModeBreakdown { get; } =
            new Bindable<IReadOnlyDictionary<string, int>>(new Dictionary<string, int>());

        /// <summary>
        /// Recent in-flight plays (up to 8). Drives the Live Plays
        /// carousel page.
        /// </summary>
        public Bindable<IReadOnlyList<APIToriiServerPulseRecentPlay>> RecentPlays { get; } =
            new Bindable<IReadOnlyList<APIToriiServerPulseRecentPlay>>(Array.Empty<APIToriiServerPulseRecentPlay>());

        /// <summary>
        /// 12 × 1-min bucket counts, oldest first. Always populated; an
        /// idle server returns all zeros.
        /// </summary>
        public Bindable<IReadOnlyList<int>> Sparkline { get; } = new Bindable<IReadOnlyList<int>>(Array.Empty<int>());

        /// <summary>
        /// Last carousel page the user looked at, persisted across popover
        /// open/close (but not across app restarts) so reopening lands on
        /// the page the user was browsing. Defaults to Overview.
        /// </summary>
        public Bindable<int> LastViewedCarouselPage { get; } = new Bindable<int>(0);

        /// <summary>
        /// Server-side capture timestamp of the most recent successful
        /// snapshot. Used by the popover footer to show "updated Xs ago"
        /// — driven off server time rather than client wall clock so a
        /// clock-drift'd machine still shows accurate freshness.
        /// </summary>
        public Bindable<DateTimeOffset?> LastUpdated { get; } = new Bindable<DateTimeOffset?>();

        /// <summary>
        /// Connection state machine. UI consumers should treat this
        /// purely as a status hint — actual data freshness is conveyed
        /// by <see cref="LastUpdated"/>.
        /// </summary>
        public Bindable<ToriiServerPulseConnectionState> ConnectionState { get; } =
            new Bindable<ToriiServerPulseConnectionState>(ToriiServerPulseConnectionState.Idle);

        /// <summary>
        /// Fires once each time <see cref="PlaysLastMinute"/> ticks up
        /// between snapshots. Caller (button) flashes the heartbeat dot.
        /// We pass the delta so the consumer can scale the flash for
        /// bursts (e.g. tournament finals end → 50 plays in one bucket).
        /// </summary>
        public event Action<int>? PlayDetected;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        // canBeNull so test scenes / headless contexts that don't have a
        // local-user-play scope still spin up the provider cleanly. In a
        // real client this is always resolved off OsuGame.
        [Resolved(canBeNull: true)]
        private ILocalUserPlayInfo? playInfo { get; set; }

        private readonly Bindable<bool> enabledBindable = new BindableBool(true);

        // Local bound copy so we can subscribe + unbind on dispose without
        // touching the source bindable that other gameplay systems share.
        private IBindable<LocalUserPlayingState>? localPlayingState;

        private ScheduledDelegate? scheduledPoll;
        private GetToriiServerPulseRequest? activeRequest;

        private bool popoverOpen;

        // True between the moment the user transitions OUT of
        // LocalUserPlayingState.Playing and <see cref="settle_window_ms"/>
        // ms later. Treated as another "pause condition" by IsPollable.
        // settleClearDelegate is the scheduled delegate that flips this
        // back to false; tracked separately so we can cancel it if the
        // user dives back into gameplay during the grace window (e.g.
        // restart-from-results — common osu! flow, don't want a stale
        // settle flag eating polls when the player has already left
        // results and is loading the next attempt).
        private bool inSettleWindow;
        private ScheduledDelegate? settleClearDelegate;

        // Used to detect whether plays_last_minute moved up between
        // snapshots so we can fire PlayDetected. Stored separately
        // because the bindable's ValueChanged would also fire on
        // initial population (which isn't a "play just happened" signal).
        private int lastObservedPlaysLastMinute = -1;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            config.BindWith(OsuSetting.ToriiServerPulseEnabled, enabledBindable);

            enabledBindable.BindValueChanged(_ => onPollabilityChanged(), true);
            api.State.BindValueChanged(_ => onPollabilityChanged(), true);

            // Subscribe to PlayingState so the polling loop pauses the
            // moment we transition into Playing and rearms when we drop
            // back to Break (paused/failed/break) or NotPlaying (post-song
            // transition or menus). canBeNull guard for test contexts.
            //
            // We also drive the post-gameplay settle window from this
            // binding — see onPlayingStateChanged for the entry/exit
            // bookkeeping.
            if (playInfo != null)
            {
                localPlayingState = playInfo.PlayingState.GetBoundCopy();
                localPlayingState.BindValueChanged(onPlayingStateChanged, true);
            }
        }

        private void onPlayingStateChanged(ValueChangedEvent<LocalUserPlayingState> change)
        {
            // Entered active gameplay: cancel any in-flight settle window.
            // The Playing-state guard in IsPollable handles the actual
            // pause, and we don't want a stale settle flag holding off
            // polls once the user reaches the results screen of a future
            // round.
            if (change.NewValue == LocalUserPlayingState.Playing)
            {
                settleClearDelegate?.Cancel();
                settleClearDelegate = null;
                inSettleWindow = false;
            }
            // Exited active gameplay: start the settle window. We arm it
            // on every Playing → non-Playing transition (including
            // Playing → Break for pauses, even though paused gameplay
            // doesn't have the post-gameplay heap drop — keeps the logic
            // simple and a 4 s pulse hold during a pause menu is invisible
            // to the user).
            else if (change.OldValue == LocalUserPlayingState.Playing)
            {
                inSettleWindow = true;
                settleClearDelegate?.Cancel();
                settleClearDelegate = Scheduler.AddDelayed(() =>
                {
                    inSettleWindow = false;
                    settleClearDelegate = null;
                    onPollabilityChanged();
                }, settle_window_ms);
            }

            onPollabilityChanged();
        }

        /// <summary>
        /// Notify the provider that the popover is now open / closed so
        /// it can switch polling cadence. Idempotent — calling with the
        /// same value twice is a no-op.
        /// </summary>
        public void SetPopoverOpen(bool open)
        {
            if (popoverOpen == open) return;
            popoverOpen = open;

            // Reschedule with the new cadence. If a poll is currently
            // in-flight we leave it alone; the next cycle picks up the
            // new interval.
            if (popoverOpen)
                triggerImmediatePoll();
            else
                rearmPollIfPollable();
        }

        /// <summary>
        /// Force an immediate refresh, cancelling any pending scheduled
        /// poll. Useful when the user explicitly opens the popover —
        /// don't make them wait up to 60s for fresh data when they JUST
        /// asked to see it.
        /// </summary>
        public void RefreshNow()
        {
            if (!IsPollable) return;
            triggerImmediatePoll();
        }

        // Polling is on whenever the widget is enabled AND the API is
        // authenticated AND the local user is not in active gameplay
        // AND we're outside the post-gameplay settle window. Break +
        // NotPlaying both count as "free to poll" once the settle
        // window has cleared — pauses / fails / song-select don't
        // need to be silent indefinitely.
        private bool IsPollable =>
            enabledBindable.Value
            && api.State.Value == APIState.Online
            && localPlayingState?.Value != LocalUserPlayingState.Playing
            && !inSettleWindow;

        private void onPollabilityChanged()
        {
            if (!enabledBindable.Value)
                ConnectionState.Value = ToriiServerPulseConnectionState.Disabled;
            else if (api.State.Value != APIState.Online)
                ConnectionState.Value = ToriiServerPulseConnectionState.Idle;
            else
                rearmPollIfPollable();

            if (!IsPollable)
                cancelInFlight();
        }

        private void rearmPollIfPollable()
        {
            cancelInFlight();
            if (!IsPollable) return;

            // Schedule the first poll one tick out so we don't block
            // LoadComplete or the API state-change handler. Subsequent
            // polls schedule themselves from the request callbacks.
            scheduledPoll = Scheduler.AddDelayed(poll, 100);
        }

        private void triggerImmediatePoll()
        {
            cancelInFlight();
            if (!IsPollable) return;
            scheduledPoll = Scheduler.AddDelayed(poll, 0);
        }

        private void cancelInFlight()
        {
            scheduledPoll?.Cancel();
            scheduledPoll = null;

            if (activeRequest != null)
            {
                // Don't actually call Cancel — the request handler will
                // run regardless once the socket flushes. We just
                // stop caring about the result by clearing the field;
                // any callback that does run will see activeRequest is
                // null / different and short-circuit.
                activeRequest = null;
            }
        }

        private void poll()
        {
            if (!IsPollable) return;

            ConnectionState.Value = ToriiServerPulseConnectionState.Connecting;

            var request = new GetToriiServerPulseRequest();
            activeRequest = request;

            request.Success += response =>
            {
                // If a newer poll already started or the provider was
                // disabled while this request was in flight, drop the
                // late result on the floor.
                if (activeRequest != request) return;
                activeRequest = null;

                applySnapshot(response);
                schedulePollNextCycle();
            };

            request.Failure += ex =>
            {
                if (activeRequest != request) return;
                activeRequest = null;

                Logger.Log($"ToriiServerPulse poll failed: {ex.Message}", LoggingTarget.Network, LogLevel.Verbose);
                ConnectionState.Value = ToriiServerPulseConnectionState.Offline;

                // Even on failure, keep the polling loop alive — temporary
                // outages should self-heal on the next tick. Idle cadence
                // even when popover is open: no point hammering during a
                // server hiccup.
                schedulePollNextCycle(forceIdleCadence: true);
            };

            api.Queue(request);
        }

        private void applySnapshot(APIToriiServerPulse snapshot)
        {
            int prevPlaysLastMinute = lastObservedPlaysLastMinute;

            // Group A: toolbar-essentials. These drive the always-visible
            // toolbar pip, the per-minute play counter, and the heartbeat
            // dot — the user sees these no matter what screen they're on,
            // so they have to land synchronously. Three int bindables +
            // the connection state enum; cheap subscribers (a single text
            // sprite, a colored dot). Sub-millisecond cost in practice.
            currentlyPlaying.Value = snapshot.CurrentlyPlaying;
            playsLastMinute.Value = snapshot.PlaysLastMinute;
            onlineUsers.Value = snapshot.OnlineUsers;
            ConnectionState.Value = ToriiServerPulseConnectionState.Connected;

            // PlayDetected goes in the first frame too: the heartbeat
            // flash should feel reactive, not lag a frame behind the
            // counter update.
            if (prevPlaysLastMinute >= 0 && snapshot.PlaysLastMinute > prevPlaysLastMinute)
            {
                int delta = snapshot.PlaysLastMinute - prevPlaysLastMinute;
                try
                {
                    PlayDetected?.Invoke(delta);
                }
                catch (Exception ex)
                {
                    // Defensive — a buggy subscriber should not take down
                    // polling. Log + carry on.
                    Logger.Log($"ToriiServerPulse PlayDetected handler threw: {ex}", LoggingTarget.Runtime, LogLevel.Important);
                }
            }

            lastObservedPlaysLastMinute = snapshot.PlaysLastMinute;

            // Group B: popover-only bindables. These drive the carousel
            // pages (TopMap card, mode-breakdown bars, recent-plays list,
            // sparkline, top-maps flow). Subscribers are layout-heavy —
            // sparkline is 12 bars with width animations, top-maps is a
            // FillFlowContainer of styled rows. Deferring to the next
            // Update tick splits the bindable cascade across two frames,
            // halving the worst-case frame cost when the popover is open
            // (and costing nothing extra when it's closed — Schedule is
            // ~free for a no-op closure). Order within the lambda matches
            // the original sequence so any downstream consumer that
            // happens to read more than one bindable sees a consistent
            // pair.
            Schedule(() =>
            {
                playsLast5Min.Value = snapshot.PlaysLast5Min;
                TopMap.Value = snapshot.TopMap;
                TopMaps.Value = snapshot.TopMaps ?? (IReadOnlyList<APIToriiServerPulseTopMap>)Array.Empty<APIToriiServerPulseTopMap>();
                ModeBreakdown.Value = snapshot.ModeBreakdown ?? (IReadOnlyDictionary<string, int>)new Dictionary<string, int>();
                RecentPlays.Value = snapshot.RecentPlays ?? (IReadOnlyList<APIToriiServerPulseRecentPlay>)Array.Empty<APIToriiServerPulseRecentPlay>();
                Sparkline.Value = snapshot.Sparkline?.Buckets ?? (IReadOnlyList<int>)Array.Empty<int>();
                LastUpdated.Value = snapshot.CapturedAt;
            });
        }

        private void schedulePollNextCycle(bool forceIdleCadence = false)
        {
            if (!IsPollable) return;

            int seconds = forceIdleCadence || !popoverOpen
                ? PollIntervalSecondsIdle
                : PollIntervalSecondsActive;

            scheduledPoll = Scheduler.AddDelayed(poll, seconds * 1000);
        }

        protected override void Dispose(bool isDisposing)
        {
            cancelInFlight();
            settleClearDelegate?.Cancel();
            settleClearDelegate = null;
            base.Dispose(isDisposing);
        }
    }

    /// <summary>
    /// Connection state for the pulse provider's last poll. Drives the
    /// small status pip in the popover header so the user can tell at a
    /// glance whether the numbers they're seeing are live or stale.
    /// </summary>
    public enum ToriiServerPulseConnectionState
    {
        /// <summary>Not yet attempted — pre-login or API not yet ready.</summary>
        Idle,

        /// <summary>Poll request in flight.</summary>
        Connecting,

        /// <summary>Last poll succeeded.</summary>
        Connected,

        /// <summary>Last poll failed (network error, server 5xx, etc).</summary>
        Offline,

        /// <summary>User toggled the widget off in settings.</summary>
        Disabled,
    }
}
