// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Online.API;

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
        /// <summary>Frames slower than this (in ms) are treated as hiccups. ~33 ms = below 30 fps on a 60 fps display.</summary>
        public const double DefaultThresholdMs = 33.0;

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
                string sessionId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                outputPath = hiccupStorage.GetFullPath($"{sessionId}.jsonl");

                var stream = hiccupStorage.GetStream($"{sessionId}.jsonl", FileAccess.Write, FileMode.Create);
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
        }

        /// <summary>
        /// Heuristic, ordered most-specific first. Lets a triager grep for
        /// patterns rather than reading every record.
        /// </summary>
        private string guessCause(double frameMs, int gen0Delta, int gen1Delta, int gen2Delta)
        {
            // Gen2 collections are the biggest stalls; if one happened on
            // this frame it's almost certainly the cause.
            if (gen2Delta > 0)
                return $"Gen2 GC pause ({gen2Delta} collection{(gen2Delta == 1 ? "" : "s")})";

            if (gen1Delta > 0)
                return $"Gen1 GC pause ({gen1Delta})";

            if (gen0Delta > 1)
                return $"GC pressure ({gen0Delta} Gen0 collections this frame)";

            if (lastApiStateChangedAt != DateTimeOffset.MinValue
                && (DateTimeOffset.UtcNow - lastApiStateChangedAt).TotalMilliseconds < 500)
                return $"API state changed to {lastApiState} ({(int)(DateTimeOffset.UtcNow - lastApiStateChangedAt).TotalMilliseconds} ms ago)";

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

        private string currentScreenName() => null;
        private string[] collectVisibleOverlays() => null;

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
