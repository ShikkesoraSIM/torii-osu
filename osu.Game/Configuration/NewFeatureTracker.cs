// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Framework.Platform;

namespace osu.Game.Configuration
{
    /// <summary>
    /// Sidecar JSON store backing the "[NEW]" badge on settings + menus.
    ///
    /// What this tracks
    /// ----------------
    /// A per-feature interaction counter. When the user clicks (taps)
    /// the host control of a [NEW] badge, the host forwards via
    /// <see cref="NewFeatureBadge.RegisterInteraction"/> which bumps
    /// this counter for the badge's feature ID. Once the counter
    /// reaches <see cref="dismiss_threshold"/> the badge auto-hides
    /// for good — the user has engaged with the feature enough times
    /// for the "new" hint to no longer be useful.
    ///
    /// Why interaction-count instead of view-count
    /// -------------------------------------------
    /// Earlier iterations counted "this badge was visible on screen" as
    /// a dismissal signal. That dismissed badges too aggressively —
    /// users would open the settings panel, scroll past a new toggle
    /// without engaging with it, and the badge would auto-clear after
    /// a few of those passive scrolls without ever drawing the user's
    /// attention. Counting actual interactions (clicks on the host
    /// control) requires the user to engage with the feature itself
    /// before the badge retires, which is exactly the discovery loop
    /// the badge is meant to drive.
    ///
    /// Why this lives outside Realm
    /// ----------------------------
    /// Same reason as <see cref="osu.Game.Skinning.PinnedSkinsStore"/>:
    /// Realm schema is locked at 51 to keep vanilla osu! lazer able to
    /// open Torii-shared realm folders, and this is Torii-only metadata
    /// that has no business in the cross-compatible schema. Stored
    /// under the same <c>torii/</c> subfolder used by the other
    /// Torii-local sidecars so a "backup my Torii state" copy only
    /// has to grab one directory.
    ///
    /// Atomic write contract
    /// ---------------------
    /// Saves go to <c>new-features-seen.json.tmp</c>, get flushed, and
    /// only then overwrite the real file. A power loss mid-save leaves
    /// the previous good copy intact rather than a torn file. Same
    /// pattern as <see cref="osu.Game.Skinning.PinnedSkinsStore"/>.
    /// </summary>
    public class NewFeatureTracker
    {
        private const string filename = @"new-features-seen.json";

        /// <summary>
        /// Number of host-control interactions the user must perform
        /// before the badge permanently dismisses itself. Tuned to
        /// "the user has clearly engaged with this feature" — three
        /// interactions: one to investigate the pill, one to actually
        /// register what the feature is, one as final reinforcement so
        /// a stray accidental double-click doesn't auto-dismiss the
        /// hint before the user noticed it.
        /// </summary>
        private const int dismiss_threshold = 3;

        private readonly Storage toriiStorage;
        private readonly object syncLock = new object();
        private readonly Dictionary<string, int> interactionCounts = new Dictionary<string, int>();
        private bool loaded;

        /// <summary>
        /// Fires after a successful <see cref="RecordInteraction"/>
        /// that changed the dismissed-state of a feature (i.e. crossed
        /// the threshold from "show badge" to "hide badge"). UI elements
        /// bound to a specific feature ID can subscribe and fade their
        /// badge out. Subscribers may be invoked on any thread;
        /// re-marshal to the update thread before touching drawables.
        /// </summary>
        public event Action<string> FeatureDismissed;

        public NewFeatureTracker(Storage baseStorage)
        {
            toriiStorage = baseStorage.GetStorageForDirectory(@"torii");
        }

        /// <summary>
        /// Returns true if the badge for this feature should still be
        /// displayed (view count is below the dismiss threshold).
        /// Returns false once the user has "seen it enough times" and
        /// for unknown feature IDs (defensive — an unknown ID means
        /// either an obsolete badge call site or a typo, neither of
        /// which should trigger a perpetual badge).
        /// </summary>
        public bool ShouldShowBadge(string featureId)
        {
            if (string.IsNullOrEmpty(featureId))
                return false;

            // Only show for IDs the registry knows about. Stops a typo
            // in a call site from creating a phantom badge that can
            // never be dismissed (because the registry-driven tooling
            // wouldn't know to clean it up either).
            if (!NewFeatureRegistry.IsKnown(featureId))
                return false;

            ensureLoaded();
            lock (syncLock)
                return interactionCounts.GetValueOrDefault(featureId, 0) < dismiss_threshold;
        }

        /// <summary>
        /// Bump the interaction counter for this feature. Returns true
        /// if the caller should still display the badge AFTER this
        /// interaction (the new count is below the threshold), false
        /// if this interaction was the one that crossed the dismiss
        /// boundary.
        ///
        /// Implementation note: persists on every increment. This is
        /// cheap (single small JSON write, ~100 bytes) and means an
        /// app crash mid-session can't lose progress toward a dismiss.
        /// </summary>
        public bool RecordInteraction(string featureId)
        {
            if (string.IsNullOrEmpty(featureId) || !NewFeatureRegistry.IsKnown(featureId))
                return false;

            ensureLoaded();

            Dictionary<string, int> snapshot;
            int newCount;
            bool crossedThreshold;

            lock (syncLock)
            {
                int current = interactionCounts.GetValueOrDefault(featureId, 0);

                // Already past the threshold — no further writes needed.
                // This is the fast path on every interaction after the
                // user has fully dismissed the badge for this feature.
                if (current >= dismiss_threshold)
                    return false;

                newCount = current + 1;
                interactionCounts[featureId] = newCount;
                crossedThreshold = newCount >= dismiss_threshold;
                snapshot = new Dictionary<string, int>(interactionCounts);
            }

            persist(snapshot);

            if (crossedThreshold)
                FeatureDismissed?.Invoke(featureId);

            return newCount < dismiss_threshold;
        }

        /// <summary>
        /// Debug / settings-reset helper: forget every recorded
        /// interaction so every badge re-arms. Not wired into the UI
        /// yet; expected callers are dev / test code that wants to
        /// verify badge behaviour without manually editing the JSON.
        /// </summary>
        public void ResetAll()
        {
            Dictionary<string, int> snapshot;

            lock (syncLock)
            {
                interactionCounts.Clear();
                loaded = true;
                snapshot = new Dictionary<string, int>();
            }

            persist(snapshot);
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
                            var parsed = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);

                            if (parsed != null)
                            {
                                foreach (var kv in parsed)
                                {
                                    // Defensive: clamp loaded counts to
                                    // [0, threshold]. Stops a malformed
                                    // file with negative or absurdly
                                    // large numbers from causing the
                                    // dismiss math to misbehave.
                                    int clamped = Math.Max(0, Math.Min(kv.Value, dismiss_threshold));
                                    interactionCounts[kv.Key] = clamped;
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    // A corrupt sidecar must NOT brick the app. Worst
                    // case the user re-sees a badge they already
                    // dismissed — strictly better than refusing to
                    // open or losing other state.
                    Logger.Log($"Failed to load new-features-seen.json, treating as empty: {e.Message}", LoggingTarget.Runtime, LogLevel.Important);
                    interactionCounts.Clear();
                }

                loaded = true;
            }
        }

        private void persist(Dictionary<string, int> snapshot)
        {
            string tmp = filename + ".tmp";

            try
            {
                // Serialise with sorted keys for deterministic file
                // diffs (helps when shipping the json around or
                // grepping logs across users).
                string json = JsonConvert.SerializeObject(snapshot.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value));

                using (var stream = toriiStorage.CreateFileSafely(tmp))
                using (var writer = new StreamWriter(stream))
                    writer.Write(json);

                // The Storage abstraction doesn't expose a rename
                // primitive, so we re-copy via streams onto the final
                // filename — same pattern as PinnedSkinsStore. Atomic
                // at the directory-entry level on every common FS.
                using (var src = toriiStorage.GetStream(tmp, FileAccess.Read, FileMode.Open))
                using (var dst = toriiStorage.CreateFileSafely(filename))
                    src.CopyTo(dst);

                toriiStorage.Delete(tmp);
            }
            catch (Exception e)
            {
                // Persisting failed; the in-memory copy is still correct
                // for this session, but the sidecar on disk may be stale.
                // Surface the error so we notice in logs, but don't
                // throw — a failed badge persist is not worth crashing
                // the entire UI subsystem over.
                Logger.Log($"Failed to persist new-features-seen.json: {e}", LoggingTarget.Runtime, LogLevel.Error);
            }
        }
    }
}
