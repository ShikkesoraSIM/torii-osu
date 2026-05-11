// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Threading;
using osu.Game.Configuration;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Screens.Play;

namespace osu.Game.Performance
{
    /// <summary>
    /// Smart frame-time hiccup detector. Added to the OsuGame draw tree only
    /// when <c>OsuSetting.ToriiHiccupLoggerEnabled</c> is ON; otherwise the
    /// component is never constructed and runtime is byte-identical to a
    /// Torii build without it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Hot path</b> (when enabled, every frame): one read of
    /// <see cref="Drawable.Time"/>'s already-computed elapsed value + one
    /// comparison. ~10 ns per frame, no allocation, no syscall — fully
    /// cache-resident. At 60 fps the total annual cost is comparable to
    /// scrolling one extra pixel.
    /// </para>
    /// <para>
    /// <b>Slow path</b> (only when a frame exceeds the threshold, gated by
    /// a 100 ms cooldown so a hiccup storm doesn't compound): captures a
    /// <see cref="HiccupRecord"/> with surrounding context (API state, GC
    /// counts, the ring-buffered "recent events" log) and pushes it onto a
    /// bounded <see cref="Channel{T}"/>. A background <see cref="Task"/>
    /// drains the channel and writes JSONL to
    /// <c>&lt;storage&gt;/torii/hiccups/&lt;session-id&gt;.jsonl</c>.
    /// The UI thread never touches the disk.
    /// </para>
    /// <para>
    /// <b>What ends up in the log</b>: per-hiccup, one JSON object per line
    /// (so the file is greppable + tail-able + diffable). See
    /// <see cref="HiccupRecord"/> for the schema. The
    /// <c>likely_cause</c> field is a heuristic guess (e.g. "Gen2 GC pause",
    /// "API state changed Online→Offline 12 ms before", "Overlay opened:
    /// ToriiBriefingOverlay") — meant to short-circuit triage when reviewing
    /// a captured session.
    /// </para>
    /// </remarks>
    public partial class ToriiHiccupLogger : CompositeDrawable
    {
        /// <summary>
        /// Frames slower than this (in ms) are treated as hiccups. 80 ms
        /// leaves the run-of-the-mill ~16-50 ms blips alone (regular
        /// allocations, occasional vsync miss, harmless GC0s) and only
        /// captures the real perceived stutters — anything 80+ on the
        /// update thread is something the user definitely felt.
        /// </summary>
        public const double DefaultThresholdMs = 80.0;

        /// <summary>Suppress consecutive hiccup records this close together to avoid logging the same stall twice.</summary>
        private const double cooldown_ms = 100.0;

        /// <summary>Ring buffer of recent events surfaced in the hiccup record so the JSON shows what was happening just before.</summary>
        private const int recent_events_capacity = 16;

        /// <summary>Bounded queue size between Update thread and background writer; oldest dropped on overflow so we never block.</summary>
        private const int write_channel_capacity = 256;

        private readonly double thresholdMs;

        private readonly Channel<HiccupRecord> writeChannel = Channel.CreateBounded<HiccupRecord>(
            new BoundedChannelOptions(write_channel_capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true,
            });

        // Lock-free event log: uint head, modular index. Read by Update only,
        // written by Update + by hooked event callbacks (which Schedule onto
        // Update — so effectively single-writer too).
        private readonly RecentEvent[] recentEvents = new RecentEvent[recent_events_capacity];
        private int recentEventsHead;

        private double lastHiccupTime = -1000;
        private int lastGen0;
        private int lastGen1;
        private int lastGen2;
        private string lastApiState = "Unknown";
        private DateTimeOffset lastApiStateChangedAt = DateTimeOffset.MinValue;

        private CancellationTokenSource drainCts;
        private StreamWriter writer;
        private string outputPath;

        // ---- Upload pipeline (only active when ShareEnabled toggle is ON) ---
        //
        // Captures land here as they happen; the periodic flush-timer drains
        // up to MAX_UPLOAD_BATCH at a time and fires an APIRequest. The queue
        // is bounded so a long server outage doesn't grow memory unboundedly
        // — once full, the oldest pending records are dropped (the local
        // JSONL file still has every record so nothing is truly lost).
        //
        // The flush timer runs on the update thread (Scheduler.AddDelayed
        // with repeat=true) but the actual HTTP request is queued via
        // api.Queue so it goes off-thread.
        private readonly ConcurrentQueue<HiccupRecord> uploadQueue = new ConcurrentQueue<HiccupRecord>();
        private const int max_upload_batch = 50;        // server hard-caps at this too
        private const int max_upload_queue_depth = 500; // bound memory under outage
        private const double upload_interval_ms = 30_000;

        private string sessionIdString;
        private string deviceHash;
        private ScheduledDelegate uploadTimerDelegate;
        private Bindable<bool> shareEnabled;
        private Bindable<string> deviceHashBindable;

        // Bound copy of the play-state so we can wake the upload pipeline
        // the instant the player drops out of Playing — without this we'd
        // wait up to a full 30 s tick for the periodic timer to retry,
        // which would feel like the dashboard is stuck 30 s behind every
        // session.
        private IBindable<LocalUserPlayingState> playingStateBound;
        private static readonly JsonSerializerSettings json_settings = new JsonSerializerSettings
        {
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            NullValueHandling = NullValueHandling.Ignore,
        };

        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        [Resolved(canBeNull: true)]
        private OsuGame osuGame { get; set; }

        [Resolved(canBeNull: true)]
        private OsuConfigManager osuConfig { get; set; }

        // Optional — null in test scenes that don't spin up a play scope.
        // Used by flushUploadQueue to skip uploads during active gameplay
        // so the periodic tick doesn't bother the update thread.
        [Resolved(canBeNull: true)]
        private ILocalUserPlayInfo playInfo { get; set; }

        [Resolved]
        private Storage storage { get; set; }

        public ToriiHiccupLogger(double thresholdMs = DefaultThresholdMs)
        {
            this.thresholdMs = thresholdMs;

            // We don't draw anything ourselves; we just need an Update tick.
            // AlwaysPresent so we keep ticking even when culled or invisible.
            AlwaysPresent = true;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            try
            {
                // One file per session — easy to discard old captures, easy to
                // tell which file goes with which run when the user shares logs.
                var hiccupStorage = storage.GetStorageForDirectory("torii/hiccups");
                sessionIdString = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                outputPath = hiccupStorage.GetFullPath($"{sessionIdString}.jsonl");

                var stream = hiccupStorage.GetStream($"{sessionIdString}.jsonl", FileAccess.Write, FileMode.Create);
                writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = false };

                // Header line — easier to spot orphaned captures and verify
                // versioning when parsing later.
                writer.WriteLine(JsonConvert.SerializeObject(new
                {
                    type = "session_start",
                    schema = 1,
                    started_at = DateTimeOffset.UtcNow,
                    threshold_ms = thresholdMs,
                    osu_version = typeof(OsuGame).Assembly.GetName().Version?.ToString() ?? "unknown",
                }, json_settings));

                drainCts = new CancellationTokenSource();
                _ = Task.Run(() => drainLoop(drainCts.Token));

                // Wire the cross-codebase breadcrumb sink to this logger.
                // From now on, any RecordEvent calls from elsewhere
                // (HiccupBreadcrumbs.Add) flow into our ring buffer.
                HiccupBreadcrumbs.Register(this);

                Logger.Log($"[ToriiHiccup] logger started, writing to {outputPath}", LoggingTarget.Runtime, LogLevel.Important);

                lastGen0 = GC.CollectionCount(0);
                lastGen1 = GC.CollectionCount(1);
                lastGen2 = GC.CollectionCount(2);

                // Hook API state changes for the "what was happening" context.
                if (api != null)
                {
                    lastApiState = api.State.Value.ToString();
                    api.State.BindValueChanged(s =>
                    {
                        lastApiState = s.NewValue.ToString();
                        lastApiStateChangedAt = DateTimeOffset.UtcNow;
                        recordEvent("api_state", $"{s.OldValue} → {s.NewValue}");
                    });
                }

                // Wire the upload pipeline. Pure no-op if osuConfig wasn't
                // resolved (test scenes etc.) — local capture still works.
                if (osuConfig != null && api != null)
                {
                    shareEnabled = osuConfig.GetBindable<bool>(OsuSetting.ToriiHiccupShareEnabled);
                    deviceHashBindable = osuConfig.GetBindable<string>(OsuSetting.ToriiHiccupDeviceHash);

                    // Generate a stable device hash on first need. The
                    // content is a SHA-256 of a fresh GUID — deliberately
                    // not derived from machine identity (no MAC / disk
                    // serial / etc.), so it's privacy-friendly while still
                    // letting the dashboard correlate reports across
                    // sessions from this install.
                    if (string.IsNullOrEmpty(deviceHashBindable.Value))
                        deviceHashBindable.Value = ComputeFreshDeviceHash();
                    deviceHash = deviceHashBindable.Value;

                    // Upload timer fires on the update thread. The actual
                    // HTTP request goes off-thread via api.Queue so this
                    // tick is cheap.
                    uploadTimerDelegate = Scheduler.AddDelayed(flushUploadQueue, upload_interval_ms, true);

                    // Wake the upload pipeline immediately on exit from
                    // gameplay — bridges the timer gap so anything captured
                    // mid-song uploads within a frame of the player landing
                    // on song-select / results / a fail screen, instead of
                    // waiting up to 30 s for the next periodic tick. Only
                    // the Playing→anything transition counts; Break→Playing
                    // and the initial NotPlaying→NotPlaying on bind don't
                    // do anything (and the queue is empty at startup anyway
                    // so a stray call would early-return).
                    if (playInfo != null)
                    {
                        playingStateBound = playInfo.PlayingState.GetBoundCopy();
                        playingStateBound.BindValueChanged(e =>
                        {
                            if (e.OldValue == LocalUserPlayingState.Playing
                                && e.NewValue != LocalUserPlayingState.Playing)
                                flushUploadQueue();
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[ToriiHiccup] failed to start: {ex.Message}", LoggingTarget.Runtime, LogLevel.Error);
            }
        }

        protected override void Update()
        {
            base.Update();

            // The framework already computed Time.Elapsed for us; we just
            // read it. No syscall, no Stopwatch allocation, no math beyond
            // a comparison. This is the entire hot-path cost.
            double elapsed = Time.Elapsed;

            if (elapsed < thresholdMs)
                return;

            // Cooldown so a single 200 ms stall doesn't generate three log
            // entries (one per detection-eligible frame within the stall).
            if (Time.Current - lastHiccupTime < cooldown_ms)
                return;

            lastHiccupTime = Time.Current;
            captureHiccup(elapsed);
        }

        // ---------------------------------------------------------------
        //  Slow path — only runs when a hiccup is detected
        // ---------------------------------------------------------------

        private void captureHiccup(double frameMs)
        {
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);

            int gen0Delta = gen0 - lastGen0;
            int gen1Delta = gen1 - lastGen1;
            int gen2Delta = gen2 - lastGen2;

            lastGen0 = gen0;
            lastGen1 = gen1;
            lastGen2 = gen2;

            var record = new HiccupRecord
            {
                Type = "hiccup",
                Timestamp = DateTimeOffset.UtcNow,
                FrameMs = frameMs,
                Thread = "Update",

                ApiState = lastApiState,
                LoggedIn = api?.IsLoggedIn ?? false,
                CurrentScreen = currentScreenName(),
                VisibleOverlays = collectVisibleOverlays(),

                Gen0Count = gen0,
                Gen1Count = gen1,
                Gen2Count = gen2,
                Gen0Delta = gen0Delta,
                Gen1Delta = gen1Delta,
                Gen2Delta = gen2Delta,
                TotalMemoryMb = (int)(GC.GetTotalMemory(false) / 1048576),

                RecentEvents = snapshotRecentEvents(),
                LikelyCause = guessCause(frameMs, gen0Delta, gen1Delta, gen2Delta),
            };

            writeChannel.Writer.TryWrite(record);

            // Also enqueue for upload if the share toggle is on. The actual
            // HTTP send happens on the periodic flush — never on the same
            // frame as the hiccup itself, so this stays a constant-time
            // enqueue.
            enqueueForUpload(record);
        }

        // ---------------------------------------------------------------
        //  Upload pipeline (only active when ShareEnabled toggle is ON)
        // ---------------------------------------------------------------

        private void enqueueForUpload(HiccupRecord record)
        {
            if (shareEnabled == null || !shareEnabled.Value) return;

            // Bound the queue depth — under a server outage we don't want
            // unbounded memory growth. The local JSONL still has every
            // record, so this just means very-old pending records may not
            // make it to the dashboard.
            if (uploadQueue.Count >= max_upload_queue_depth)
            {
                if (uploadQueue.TryDequeue(out _))
                {
                    // log only occasionally to avoid spam
                }
            }

            uploadQueue.Enqueue(record);
        }

        private void flushUploadQueue()
        {
            if (api == null || shareEnabled == null || !shareEnabled.Value)
                return;

            if (uploadQueue.IsEmpty)
                return;

            // Don't upload mid-song. The HTTP itself goes off-thread via
            // api.Queue, but request construction + the success/failure
            // callbacks all schedule onto the update thread, and the 30 s
            // tick landing during gameplay was visible in prod hiccup data
            // (one upload took 7 s under a backed-up queue, then the next
            // frame's response cascade stuttered). Captures still land in
            // the local JSONL and the in-memory queue (bounded to 500),
            // so nothing is lost — the queue drains on the next 30 s tick
            // after the player drops out of gameplay. Break / NotPlaying
            // both allow upload; only the hot-loop Playing state is gated.
            if (playInfo != null && playInfo.PlayingState.Value == LocalUserPlayingState.Playing)
                return;

            // Drain up to MAX_UPLOAD_BATCH from the queue. The server caps
            // batches at 50, so chunking here matches that ceiling.
            var batch = new List<HiccupRecord>(max_upload_batch);
            while (batch.Count < max_upload_batch && uploadQueue.TryDequeue(out var rec))
                batch.Add(rec);

            if (batch.Count == 0) return;

            try
            {
                var payload = new HiccupBatchPayload
                {
                    SessionId = sessionIdString,
                    DeviceHash = deviceHash,
                    OsuVersion = typeof(OsuGame).Assembly.GetName().Version?.ToString() ?? "unknown",
                    Platform = RuntimeInfo.OS.ToString(),
                    CpuArch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                    Records = batch.ToArray(),
                };

                var request = new SubmitToriiHiccupReportsRequest(payload);

                request.Success += response =>
                {
                    if (response.Dropped > 0)
                    {
                        Logger.Log(
                            $"[ToriiHiccup] uploaded batch ({response.Accepted} accepted, {response.Dropped} dropped server-side)",
                            LoggingTarget.Runtime, LogLevel.Verbose);
                    }
                };

                request.Failure += ex =>
                {
                    // Drop the batch on persistent failure rather than re-queueing.
                    // The local JSONL has every record, so re-uploading the
                    // same data on next session would just spam duplicates.
                    Logger.Log(
                        $"[ToriiHiccup] upload failed (batch of {batch.Count}): {ex.Message}",
                        LoggingTarget.Runtime, LogLevel.Verbose);
                };

                api.Queue(request);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ToriiHiccup] flush threw: {ex.Message}", LoggingTarget.Runtime, LogLevel.Error);
            }
        }

        /// <summary>
        /// Generates a stable per-install device identifier as a SHA-256 of
        /// a freshly-generated GUID. Privacy-friendly: derived from random
        /// bytes, not from any machine attribute. Stored in osu.cfg so it
        /// persists across game runs but is per-install.
        /// </summary>
        private static string ComputeFreshDeviceHash()
        {
            byte[] bytes = Guid.NewGuid().ToByteArray();
            byte[] hash = SHA256.HashData(bytes);
            var sb = new StringBuilder(64);
            foreach (byte b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>
        /// Heuristic, ordered most-specific first. Lets a triager grep for
        /// patterns rather than reading every record.
        /// </summary>
        /// <remarks>
        /// Now consumes the recent-events ring buffer too: if a known-slow
        /// operation (realm.run, carousel.filter, api.request, ...) ended in
        /// the last few hundred ms before the stall, we attribute the cause
        /// to it. That's a fuzzy correlation, but combined with the
        /// stack-sample evidence (Path B, see <c>StackTopFrames</c>) it gives
        /// the dashboard a punchy first guess for the cause column.
        /// </remarks>
        private string guessCause(double frameMs, int gen0Delta, int gen1Delta, int gen2Delta)
        {
            var now = DateTimeOffset.UtcNow;

            // Most-specific GC hits first — Gen2 stalls are unmissable.
            if (gen2Delta > 0)
                return $"Gen2 GC pause ({gen2Delta} collection{(gen2Delta == 1 ? "" : "s")})";

            // Walk the recent-events ring, newest first. Any event in the
            // 500 ms window before the stall is a candidate cause; we pick
            // the freshest matching kind.
            int head = recentEventsHead;
            int n = Math.Min(head, recent_events_capacity);
            for (int i = 0; i < n; i++)
            {
                int writeIndex = head - 1 - i;
                int idx = ((writeIndex % recent_events_capacity) + recent_events_capacity) % recent_events_capacity;
                var ev = recentEvents[idx];

                if (ev.Kind == null) continue;

                double age = (now - ev.AtUtc).TotalMilliseconds;
                if (age > 500) break; // beyond correlation window — older events get ignored

                switch (ev.Kind)
                {
                    case "realm.run":
                        return $"Realm query on update thread {(int)age} ms ago — {ev.Detail}";

                    case "carousel.filter":
                        return $"Carousel filter operation {(int)age} ms ago — {ev.Detail}";

                    case "api.request.start":
                        // A request still in flight at hiccup time → strongly
                        // suggests synchronous wait on a network call. End-of-
                        // request would be a different shape.
                        return $"API request in flight ({ev.Detail}) — possible synchronous wait";

                    case "api.request.end":
                        // A long request just finished and the next frame is
                        // slow → the response handler is doing heavy work
                        // (parse / bind / layout cascade).
                        return $"API request handler ({ev.Detail})";

                    case "api_state":
                        return $"API state cascade ({ev.Detail}) {(int)age} ms ago";

                    case "screen.push":
                        return $"Screen entry — {ev.Detail} {(int)age} ms ago";

                    case "screen.exit":
                        return $"Screen exit — {ev.Detail} {(int)age} ms ago";

                    case "overlay.show":
                        return $"Overlay opened — {ev.Detail} {(int)age} ms ago";

                    case "notify.beatmap":
                    case "notify.score":
                    case "notify.skin":
                        return $"Import / notification — {ev.Kind.Substring("notify.".Length)} {(int)age} ms ago";
                }
            }

            // Fallbacks based on GC + frame size only when no breadcrumb explains the stall.
            if (gen1Delta > 0)
                return $"Gen1 GC pause ({gen1Delta})";

            if (gen0Delta > 1)
                return $"GC pressure ({gen0Delta} Gen0 collections this frame)";

            if (frameMs > 500)
                return "Major stall (>500 ms) — likely a synchronous I/O or a deadlock-recovery";

            if (frameMs > 200)
                return "Heavy stall (>200 ms) — likely a synchronous DB query or big layout pass";

            return "Unknown — see context fields";
        }

        // ---------------------------------------------------------------
        //  Context capture helpers
        // ---------------------------------------------------------------

        // We deliberately don't walk the game's draw tree here — most of
        // CompositeDrawable's children are protected, and reflection on the
        // hot-path triage is fragile. Instead we rely on:
        //   - The recent_events ring buffer (RecordEvent breadcrumbs from
        //     other Torii systems and from the api.State subscription).
        //   - lastApiState set in load().
        // If a future iteration needs richer state context, we can either
        // expose it from OsuGame as an internal accessor or hook screen
        // navigation events from outside this class via RecordEvent.

        /// <summary>
        /// Reads the current top-of-stack screen via the internal accessor
        /// OsuGame exposes for us. Returns null if the host isn't fully
        /// loaded yet (very-early hiccups during startup) or if the screen
        /// stack hasn't been initialised. Cheap — single property read.
        /// </summary>
        private string currentScreenName()
        {
            try
            {
                return osuGame?.CurrentTopScreen?.GetType().Name;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Snapshots the type names of every focused overlay that's currently
        /// visible. Bounded by the registered-focused-overlays list (typically
        /// &lt; 20), so the loop is fast even on the slow path.
        /// </summary>
        private string[] collectVisibleOverlays()
        {
            try
            {
                var registered = osuGame?.RegisteredFocusedOverlays;
                if (registered == null) return null;

                List<string> visible = null;
                foreach (var overlay in registered)
                {
                    if (overlay.State.Value == osu.Framework.Graphics.Containers.Visibility.Visible)
                    {
                        visible ??= new List<string>(4);
                        visible.Add(overlay.GetType().Name);
                    }
                }

                return visible?.ToArray();
            }
            catch
            {
                return null;
            }
        }

        // ---------------------------------------------------------------
        //  Recent-events ring buffer (shows what was happening just before)
        // ---------------------------------------------------------------

        /// <summary>
        /// Public hook so other Torii systems (briefing show, popover open,
        /// etc.) can drop a breadcrumb into the recent-events log. Cheap,
        /// no I/O, no allocation in steady state — just a pre-allocated
        /// struct slot in the ring buffer.
        /// </summary>
        public void RecordEvent(string kind, string detail)
        {
            recordEvent(kind, detail);
        }

        private void recordEvent(string kind, string detail)
        {
            // Multi-writer-safe-ish — kind/detail are immutable strings so a
            // torn read just sees an older entry; we don't fence here.
            int slot = Interlocked.Increment(ref recentEventsHead) - 1;
            recentEvents[(uint)slot % recent_events_capacity] = new RecentEvent
            {
                Kind = kind,
                Detail = detail,
                AtUtc = DateTimeOffset.UtcNow,
            };
        }

        private RecentEvent[] snapshotRecentEvents()
        {
            int head = recentEventsHead;
            int n = Math.Min(head, recent_events_capacity);
            var snap = new RecentEvent[n];
            for (int i = 0; i < n; i++)
            {
                // C#'s `%` returns a negative remainder for negative numbers,
                // so canonicalise: ((x % cap) + cap) % cap. With n bounded by
                // head, the input is always ≥ 0, but defensive math is cheap.
                int writeIndex = head - 1 - i;
                int idx = ((writeIndex % recent_events_capacity) + recent_events_capacity) % recent_events_capacity;
                snap[i] = recentEvents[idx];
            }
            return snap;
        }

        // ---------------------------------------------------------------
        //  Background drain — JSON serialise + write off the UI thread
        // ---------------------------------------------------------------

        private async Task drainLoop(CancellationToken cancellation)
        {
            try
            {
                var reader = writeChannel.Reader;
                while (await reader.WaitToReadAsync(cancellation).ConfigureAwait(false))
                {
                    while (reader.TryRead(out var record))
                    {
                        try
                        {
                            string line = JsonConvert.SerializeObject(record, json_settings);
                            await writer.WriteLineAsync(line.AsMemory(), cancellation).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            Logger.Log($"[ToriiHiccup] write failed: {ex.Message}", LoggingTarget.Runtime, LogLevel.Error);
                        }
                    }

                    // Flush on each batch so logs survive a crash.
                    try { await writer.FlushAsync(cancellation).ConfigureAwait(false); }
                    catch { /* ignore */ }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger.Log($"[ToriiHiccup] drain loop crashed: {ex.Message}", LoggingTarget.Runtime, LogLevel.Error);
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            try
            {
                // Unregister the static breadcrumb sink first so any
                // late-firing event hooks (during shutdown) don't try to
                // write into a disposed channel.
                HiccupBreadcrumbs.Register(null);

                uploadTimerDelegate?.Cancel();
                // Drop the upload queue on dispose — local JSONL still
                // has every record so nothing is truly lost; a fresh
                // upload session starts on next launch.
                while (uploadQueue.TryDequeue(out _)) { }

                drainCts?.Cancel();
                writeChannel.Writer.TryComplete();
                writer?.Flush();
                writer?.Dispose();
            }
            catch { /* ignore — we're shutting down */ }

            base.Dispose(isDisposing);
        }
    }

    /// <summary>One per-hiccup record, written as a JSON line.</summary>
    public sealed class HiccupRecord
    {
        [JsonProperty("type")] public string Type { get; set; }
        [JsonProperty("ts")] public DateTimeOffset Timestamp { get; set; }
        [JsonProperty("frame_ms")] public double FrameMs { get; set; }
        [JsonProperty("thread")] public string Thread { get; set; }

        [JsonProperty("api_state")] public string ApiState { get; set; }
        [JsonProperty("logged_in")] public bool LoggedIn { get; set; }
        [JsonProperty("current_screen")] public string CurrentScreen { get; set; }
        [JsonProperty("visible_overlays")] public string[] VisibleOverlays { get; set; }

        [JsonProperty("gen0_count")] public int Gen0Count { get; set; }
        [JsonProperty("gen1_count")] public int Gen1Count { get; set; }
        [JsonProperty("gen2_count")] public int Gen2Count { get; set; }
        [JsonProperty("gen0_delta")] public int Gen0Delta { get; set; }
        [JsonProperty("gen1_delta")] public int Gen1Delta { get; set; }
        [JsonProperty("gen2_delta")] public int Gen2Delta { get; set; }
        [JsonProperty("total_memory_mb")] public int TotalMemoryMb { get; set; }

        [JsonProperty("recent_events")] public RecentEvent[] RecentEvents { get; set; }
        [JsonProperty("likely_cause")] public string LikelyCause { get; set; }
    }

    /// <summary>Breadcrumb in the recent-events ring buffer.</summary>
    public struct RecentEvent
    {
        [JsonProperty("kind")] public string Kind { get; set; }
        [JsonProperty("detail")] public string Detail { get; set; }
        [JsonProperty("at_utc")] public DateTimeOffset AtUtc { get; set; }
    }
}
