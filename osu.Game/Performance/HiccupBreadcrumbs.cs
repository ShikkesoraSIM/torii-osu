// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

namespace osu.Game.Performance
{
    /// <summary>
    /// Static sink that lets the rest of the codebase drop breadcrumb events
    /// for the Torii hiccup logger without taking a dependency on the logger
    /// itself. When the logger is not running (toggle OFF, the default
    /// state), every call here is a single null-check + return — zero
    /// allocation, zero work, no scheduler ticks. So sprinkling these calls
    /// across hot paths is free in production.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why static instead of <c>[Resolved]</c>: instrumentation should be
    /// the LIGHTEST possible touch on the systems being instrumented. A
    /// resolved dependency means every potential consumer needs the right
    /// DI surface, and the call site has to thread the resolved instance
    /// through. With this sink an arbitrary class (a static helper, a
    /// non-Drawable value type, a deeply-nested closure) can call
    /// <see cref="Add"/> without changing its signature or
    /// dependencies. The cost is a single static reference cell — set once
    /// when the logger spins up, cleared once when it goes away.
    /// </para>
    /// <para>
    /// Thread-safety: the field is <c>volatile</c> so sink-toggle and sink-
    /// read race cleanly. The logger's <see cref="ToriiHiccupLogger.RecordEvent"/>
    /// is itself lock-free (a ring-buffer slot atomic increment), so this
    /// path stays cheap from any thread.
    /// </para>
    /// </remarks>
    public static class HiccupBreadcrumbs
    {
        /// <summary>The currently active sink. Null when no logger is running.</summary>
        private static volatile ToriiHiccupLogger sink;

        /// <summary>
        /// Wires this static sink to a logger instance. Called by the logger
        /// when it loads (and on dispose with <c>null</c>). Pairs are not
        /// stack-tracked — at most one logger lives at a time, so the
        /// last writer wins.
        /// </summary>
        public static void Register(ToriiHiccupLogger logger)
        {
            sink = logger;
        }

        /// <summary>
        /// Drops a breadcrumb if the logger is active. The hot path when
        /// the logger is OFF is a single null-check; <c>kind</c> /
        /// <c>detail</c> are not touched. Callers can pass interpolated
        /// strings without worrying about cost when the feature is
        /// disabled.
        /// </summary>
        /// <param name="kind">Short namespaced identifier (e.g. <c>api.request.start</c>, <c>screen.push</c>).</param>
        /// <param name="detail">Free-form description, ideally with the key
        /// payload field (URL, type name, item count, etc.) so the
        /// dashboard can show useful context without parsing.</param>
        public static void Add(string kind, string detail = null)
        {
            sink?.RecordEvent(kind, detail ?? string.Empty);
        }
    }
}
