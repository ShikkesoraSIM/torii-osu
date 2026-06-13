// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Extensions.TypeExtensions;
using osu.Framework.Graphics;
using osu.Framework.Statistics;

namespace osu.Game.Database
{
    /// <summary>
    /// A component which performs lookups (or calculations) and caches the results.
    /// Currently not persisted between game sessions.
    /// </summary>
    public abstract partial class MemoryCachingComponent<TLookup, TValue> : Component
        where TLookup : notnull
    {
        private readonly ConcurrentDictionary<TLookup, TValue?> cache = new ConcurrentDictionary<TLookup, TValue?>();

        private readonly GlobalStatistic<MemoryCachingStatistics> statistics;

        protected virtual bool CacheNullValues => true;

        // Parallel last-access tracking + a monotonic clock for approximate-LRU eviction.
        // Only populated/consulted when MaxCacheSize is non-null, so unbounded subclasses
        // (the default) pay nothing.
        private readonly ConcurrentDictionary<TLookup, long> lastAccess = new ConcurrentDictionary<TLookup, long>();
        private long accessClock;
        private readonly object evictionLock = new object();

        /// <summary>
        /// When non-null, the cache is bounded to roughly this many entries via
        /// approximate-LRU eviction. Null (the default) keeps the original unbounded
        /// behaviour. Eviction only ever costs a recompute on a later miss; it never
        /// changes a value that gets returned.
        /// </summary>
        protected virtual int? MaxCacheSize => null;

        protected MemoryCachingComponent()
        {
            statistics = GlobalStatistics.Get<MemoryCachingStatistics>(nameof(MemoryCachingComponent<TLookup, TValue>), GetType().ReadableName());
            statistics.Value = new MemoryCachingStatistics();
        }

        /// <summary>
        /// Retrieve the cached value for the given lookup.
        /// </summary>
        /// <param name="lookup">The lookup to retrieve.</param>
        /// <param name="cancellationToken">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
        /// <param name="computationDelay">In the case a cached lookup was not possible, a value in milliseconds of to wait until performing potentially intensive lookup.</param>
        protected async Task<TValue?> GetAsync(TLookup lookup, CancellationToken cancellationToken = default, int computationDelay = 0)
        {
            if (CheckExists(lookup, out TValue? existing))
            {
                statistics.Value.HitCount++;
                return existing;
            }

            if (computationDelay > 0)
                await Task.Delay(computationDelay, cancellationToken).ConfigureAwait(false);

            var computed = await ComputeValueAsync(lookup, cancellationToken).ConfigureAwait(false);

            statistics.Value.MissCount++;

            if (computed != null || CacheNullValues)
            {
                cache[lookup] = computed;
                touch(lookup);
                statistics.Value.Usage = cache.Count;
                trimIfRequired();
            }

            return computed;
        }

        /// <summary>
        /// Invalidate all entries matching a provided predicate.
        /// </summary>
        /// <param name="matchKeyPredicate">The predicate to decide which keys should be invalidated.</param>
        protected void Invalidate(Func<TLookup, bool> matchKeyPredicate)
        {
            foreach (var kvp in cache)
            {
                if (matchKeyPredicate(kvp.Key))
                {
                    cache.TryRemove(kvp.Key, out _);
                    lastAccess.TryRemove(kvp.Key, out _);
                }
            }

            statistics.Value.Usage = cache.Count;
        }

        /// <summary>
        /// Completely purge the cache.
        /// </summary>
        public virtual void Clear()
        {
            cache.Clear();
            lastAccess.Clear();
            statistics.Value.Usage = 0;
        }

        protected bool CheckExists(TLookup lookup, [MaybeNullWhen(false)] out TValue value)
        {
            if (cache.TryGetValue(lookup, out value))
            {
                // Record the access so frequently-used keys (the active working set) stay
                // hot and survive eviction. Only costs anything when bounding is enabled.
                if (MaxCacheSize != null)
                    touch(lookup);

                return true;
            }

            return false;
        }

        private void touch(TLookup lookup)
        {
            if (MaxCacheSize == null)
                return;

            // Only stamp keys actually present so we never resurrect an evicted key's metadata.
            if (cache.ContainsKey(lookup))
                lastAccess[lookup] = Interlocked.Increment(ref accessClock);
        }

        private void trimIfRequired()
        {
            int? max = MaxCacheSize;

            if (max == null || cache.Count <= max.Value)
                return;

            // Serialise eviction so two concurrent misses cannot both trim and over-evict.
            lock (evictionLock)
            {
                int cap = max.Value;

                if (cache.Count <= cap)
                    return;

                int target = Math.Max(1, (int)(cap * 0.9));
                int toRemove = cache.Count - target;

                if (toRemove <= 0)
                    return;

                // Approximate LRU: evict the oldest-accessed keys (missing stamp sorts oldest).
                // A later lookup of an evicted key simply recomputes/refetches, so this only
                // ever costs work on a future miss, never changing a returned value.
                var victims = cache.Keys
                                   .OrderBy(k => lastAccess.TryGetValue(k, out long t) ? t : 0L)
                                   .Take(toRemove)
                                   .ToList();

                foreach (var key in victims)
                {
                    cache.TryRemove(key, out _);
                    lastAccess.TryRemove(key, out _);
                }

                statistics.Value.Usage = cache.Count;
            }
        }

        /// <summary>
        /// Called on cache miss to compute the value for the specified lookup.
        /// </summary>
        /// <param name="lookup">The lookup to retrieve.</param>
        /// <param name="token">An optional <see cref="CancellationToken"/> to cancel the operation.</param>
        /// <returns>The computed value.</returns>
        protected abstract Task<TValue?> ComputeValueAsync(TLookup lookup, CancellationToken token = default);

        private class MemoryCachingStatistics
        {
            /// <summary>
            /// Total number of cache hits.
            /// </summary>
            public int HitCount;

            /// <summary>
            /// Total number of cache misses.
            /// </summary>
            public int MissCount;

            /// <summary>
            /// Total number of cached entities.
            /// </summary>
            public int Usage;

            public override string ToString()
            {
                int totalAccesses = HitCount + MissCount;
                double hitRate = totalAccesses == 0 ? 0 : (double)HitCount / totalAccesses;

                return $"i:{Usage} h:{HitCount} m:{MissCount} {hitRate:0%}";
            }
        }
    }
}
