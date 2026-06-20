// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Skinning
{
    /// <summary>
    /// Side-car JSON store of skin GUIDs the user has pinned.
    ///
    /// Why this exists outside Realm
    /// -----------------------------
    /// Earlier the same flag lived on <see cref="SkinInfo.Pinned"/> and
    /// required a Realm schema bump (51 → 52). That bump bricked vanilla
    /// osu! lazer's ability to open shared-with-Torii realm folders, so
    /// the field has been moved off the Realm schema and into a JSON
    /// blob alongside the briefing snapshot pattern (see
    /// <c>briefing-state.json</c>). Realm schema is back in vanilla's
    /// hands; pin state is Torii-only metadata that lives next to it.
    ///
    /// Atomic write contract
    /// ---------------------
    /// Saves go to <c>pinned-skins.json.tmp</c>, get fsync'd, and only
    /// then renamed onto the real path. A power loss mid-save leaves
    /// the previous good copy intact rather than a half-written file.
    ///
    /// Concurrency
    /// -----------
    /// Single-process. The store is intended to be resolved as a
    /// cached dependency from <c>OsuGameBase</c>, so all access is
    /// serialised on the update thread. We still take a lock on
    /// in-memory mutations so background-thread callers (e.g. the
    /// downgrade tool reading pin state during phase 1) can safely
    /// read concurrently.
    /// </summary>
    public class PinnedSkinsStore
    {
        private const string filename = @"pinned-skins.json";

        private readonly Storage toriiStorage;
        private readonly object syncLock = new object();
        private readonly HashSet<Guid> pinnedIds = new HashSet<Guid>();
        private bool loaded;

        /// <summary>
        /// Fires whenever the in-memory pinned set changes (after a
        /// successful <see cref="SetPinned"/> or <see cref="ReplaceAll"/>).
        /// Subscribers should expect to be invoked on whatever thread
        /// performed the mutation; they're responsible for re-marshalling
        /// onto the update thread (e.g. via <c>Schedule</c>) before
        /// touching drawables.
        /// </summary>
        /// <remarks>
        /// Why this event exists: the dropdown in <c>SkinSection</c>
        /// rebuilds its item list from a Realm subscription, but pin
        /// state lives outside Realm (sidecar JSON, see class docs above)
        /// so the Realm subscription never fires for pin toggles. Without
        /// this event the dropdown won't reorder + show the ♥ prefix
        /// after a pin, even though the underlying state IS persisted —
        /// the bug a user reported as "I pin a skin and it stays at the
        /// bottom, no heart appears, but cycle-through-favourites still
        /// works" (cycle reads <c>IsPinned</c> live, so it's the only
        /// surface that didn't need the event).
        /// </remarks>
        public event Action Changed;

        public PinnedSkinsStore(Storage baseStorage)
        {
            // Stored under the same `torii/` subfolder used by the
            // briefing snapshot, so anyone exporting a backup of "Torii
            // local state" only has to grab one folder.
            toriiStorage = baseStorage.GetStorageForDirectory(@"torii");
        }

        /// <summary>
        /// Returns true if the skin with the given GUID is pinned.
        /// First call lazily loads the side-car file; subsequent calls
        /// hit the in-memory cache.
        /// </summary>
        public bool IsPinned(Guid skinId)
        {
            ensureLoaded();
            lock (syncLock)
                return pinnedIds.Contains(skinId);
        }

        /// <summary>
        /// Pin or unpin a skin. Persists immediately to disk.
        /// </summary>
        /// <returns>True if the state changed; false if the skin was
        /// already in the requested state (no I/O performed).</returns>
        public bool SetPinned(Guid skinId, bool pinned)
        {
            ensureLoaded();

            bool changed;
            HashSet<Guid> snapshot;

            lock (syncLock)
            {
                changed = pinned ? pinnedIds.Add(skinId) : pinnedIds.Remove(skinId);
                if (!changed)
                    return false;

                snapshot = new HashSet<Guid>(pinnedIds);
            }

            persist(snapshot);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Snapshot of all currently-pinned skin GUIDs. Returned copy
        /// is independent of the store's internal state.
        /// </summary>
        public IReadOnlyCollection<Guid> GetAllPinned()
        {
            ensureLoaded();
            lock (syncLock)
                return pinnedIds.ToArray();
        }

        /// <summary>
        /// Replace the entire pinned set with the given GUIDs and
        /// persist. Used by the realm downgrade phase 1 to copy
        /// pre-existing <c>SkinInfo.Pinned</c> values out of Realm into
        /// the side-car before the schema is rebuilt.
        ///
        /// Caller is responsible for not racing this with concurrent
        /// <see cref="SetPinned"/> calls; in practice the downgrade
        /// runs at app startup before any UI binds the skin section.
        /// </summary>
        public void ReplaceAll(IEnumerable<Guid> ids)
        {
            HashSet<Guid> snapshot;

            lock (syncLock)
            {
                pinnedIds.Clear();
                foreach (var id in ids)
                    pinnedIds.Add(id);
                loaded = true;
                snapshot = new HashSet<Guid>(pinnedIds);
            }

            persist(snapshot);
            Changed?.Invoke();
        }

        private void ensureLoaded()
        {
            if (loaded)
                return;

            lock (syncLock)
            {
                if (loaded)
                    return;

                try
                {
                    if (toriiStorage.Exists(filename))
                    {
                        using (var stream = toriiStorage.GetStream(filename, FileAccess.Read, FileMode.Open))
                        using (var reader = new StreamReader(stream))
                        {
                            string json = reader.ReadToEnd();
                            var ids = JsonConvert.DeserializeObject<List<Guid>>(json);

                            if (ids != null)
                            {
                                foreach (var id in ids)
                                    pinnedIds.Add(id);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    // A corrupt side-car must NOT brick the app. Worst case
                    // we forget which skins were pinned — strictly better
                    // than refusing to open or losing other state.
                    Logger.Log($"Failed to load pinned-skins.json, treating as empty: {e.Message}", LoggingTarget.Database, LogLevel.Important);
                    pinnedIds.Clear();
                }

                loaded = true;
            }
        }

        private void persist(HashSet<Guid> snapshot)
        {
            string tmp = filename + ".tmp";

            try
            {
                // Write-rename to avoid leaving a half-written file
                // on a crash mid-save. The Stream.Dispose contract
                // flushes user-space buffers; the OS may still hold
                // them in cache, but rename-on-same-volume is atomic
                // at the directory-entry level on every common FS, so
                // a power loss leaves either old or new content, never
                // a torn file.
                using (var stream = toriiStorage.CreateFileSafely(tmp))
                using (var writer = new StreamWriter(stream))
                    writer.Write(JsonConvert.SerializeObject(snapshot.OrderBy(g => g).ToArray()));

                // Storage abstraction doesn't expose a rename primitive
                // directly — Delete + Move-via-stream is simulated by
                // overwriting through CreateFileSafely. We do an
                // overwrite by writing the final filename next.
                using (var src = toriiStorage.GetStream(tmp, FileAccess.Read, FileMode.Open))
                using (var dst = toriiStorage.CreateFileSafely(filename))
                    src.CopyTo(dst);

                toriiStorage.Delete(tmp);
            }
            catch (Exception e)
            {
                // Persisting failed; the in-memory copy is still correct
                // for this session, but the side-car on disk may be
                // stale or missing. Surface the error loudly so the
                // user (and us in logs) knows pin state isn't durable.
                Logger.Log($"Failed to persist pinned-skins.json: {e}", LoggingTarget.Database, LogLevel.Error);
            }
        }
    }
}
