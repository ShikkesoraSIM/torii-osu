// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Rooms;

namespace osu.Game.Screens.OnlinePlay
{
    /// <summary>
    /// Represent a checksum-verifying beatmap availability tracker usable for online play screens.
    ///
    /// This differs from a regular download tracking composite as this accounts for the
    /// databased beatmap set's checksum, to disallow from playing with an altered version of the beatmap.
    /// </summary>
    public abstract partial class OnlinePlayBeatmapAvailabilityTracker : CompositeComponent
    {
        /// <summary>
        /// The current availability of <see cref="PlaylistItem"/>'s beatmap.
        /// </summary>
        public virtual IBindable<BeatmapAvailability> Availability => availability; // Virtual for mocking in some tests.

        /// <summary>
        /// The playlist item to track the availability of.
        /// </summary>
        protected readonly Bindable<PlaylistItem?> PlaylistItem = new Bindable<PlaylistItem?>();

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private BeatmapLookupCache beatmapLookupCache { get; set; } = null!;

        private readonly Bindable<BeatmapAvailability> availability = new Bindable<BeatmapAvailability>(BeatmapAvailability.NotDownloaded());

        private ScheduledDelegate? progressUpdate;
        private BeatmapDownloadTracker? downloadTracker;
        private IDisposable? realmSubscription;

        // Mismatch-warning state. Both fields reset in cancelTracking() so
        // re-tracking the same playlist item gets a fresh check.
        //
        //   - mismatchWarningPending: a debounced timer that defers the actual
        //     log call by 1.5s. The metadata-lookup pipeline that populates
        //     OnlineMD5Hash runs on its own scheduler — without this debounce,
        //     updateAvailability fires while OnlineMD5Hash is still empty and
        //     incorrectly reports a mismatch even when the file is bit-perfect.
        //   - mismatchWarningEmitted: dedupe flag so the warning only ever
        //     surfaces once per import. Without it, the realm subscription and
        //     the progress tracker each call updateAvailability after import
        //     completes and both can hit the log line in succession (this is
        //     the "two notifications in a row" the user reported).
        private ScheduledDelegate? mismatchWarningPending;
        private bool mismatchWarningEmitted;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            PlaylistItem.BindValueChanged(item =>
            {
                // the underlying playlist is regularly cleared for maintenance purposes (things which probably need to be fixed eventually).
                // to avoid exposing a state change when there may actually be none, ignore all nulls for now.
                if (item.NewValue == null)
                    return;

                // Initially set to unknown until we have attained a good state.
                // This has the wanted side effect of forcing a state change when the current playlist
                // item changes at the server but our local availability doesn't necessarily change
                // (ie. we have both the previous and next item LocallyAvailable).
                //
                // Note that even without this, the server will trigger a state change and things will work.
                // This is just for safety.
                availability.Value = BeatmapAvailability.Unknown();

                cancelTracking();

                beatmapLookupCache.GetBeatmapAsync(item.NewValue.Beatmap.OnlineID).ContinueWith(task => Schedule(() =>
                {
                    var beatmap = task.GetResultSafely();

                    if (beatmap != null && PlaylistItem.Value?.Beatmap.OnlineID == beatmap.OnlineID)
                        startTracking(beatmap);
                }), TaskContinuationOptions.OnlyOnRanToCompletion);
            }, true);
        }

        private void cancelTracking()
        {
            downloadTracker?.RemoveAndDisposeImmediately();
            realmSubscription?.Dispose();
            mismatchWarningPending?.Cancel();
            mismatchWarningPending = null;
            mismatchWarningEmitted = false;
        }

        private void startTracking(APIBeatmap beatmap)
        {
            Debug.Assert(beatmap.BeatmapSet != null);

            downloadTracker = new BeatmapDownloadTracker(beatmap.BeatmapSet);
            downloadTracker.State.BindValueChanged(_ => Scheduler.AddOnce(updateAvailability), true);
            downloadTracker.Progress.BindValueChanged(_ =>
            {
                if (downloadTracker.State.Value != DownloadState.Downloading)
                    return;

                // incoming progress changes are going to be at a very high rate.
                // we don't want to flood the network with this, so rate limit how often we send progress updates.
                if (progressUpdate?.Completed != false)
                    progressUpdate = Scheduler.AddDelayed(updateAvailability, progressUpdate == null ? 0 : 500);
            }, true);

            AddInternal(downloadTracker);

            // handles changes to hash that didn't occur from the import process (ie. a user editing the beatmap in the editor, somehow).
            realmSubscription = realm.RegisterForNotifications(_ => queryBeatmap(), (_, changes) =>
            {
                if (changes == null)
                    return;

                Scheduler.AddOnce(updateAvailability);
            });

            void updateAvailability()
            {
                switch (downloadTracker.State.Value)
                {
                    case DownloadState.Unknown:
                        availability.Value = BeatmapAvailability.Unknown();
                        break;

                    case DownloadState.NotDownloaded:
                        availability.Value = BeatmapAvailability.NotDownloaded();
                        break;

                    case DownloadState.Downloading:
                        availability.Value = BeatmapAvailability.Downloading((float)downloadTracker.Progress.Value);
                        break;

                    case DownloadState.Importing:
                        availability.Value = BeatmapAvailability.Importing();
                        break;

                    case DownloadState.LocallyAvailable:
                        bool available = queryBeatmap().Any();

                        availability.Value = available ? BeatmapAvailability.LocallyAvailable() : BeatmapAvailability.NotDownloaded();

                        if (available)
                        {
                            // We've just confirmed a clean match. If a mismatch warning
                            // had been queued (because OnlineMD5Hash hadn't propagated
                            // yet on the previous tick), kill it — the file IS fine.
                            mismatchWarningPending?.Cancel();
                            mismatchWarningPending = null;
                        }
                        else if (!mismatchWarningEmitted && mismatchWarningPending == null && downloadTracker.Progress.Value == 1)
                        {
                            // Defer the warning by 1.5s so the metadata-lookup pipeline
                            // (which populates OnlineMD5Hash on a separate scheduler)
                            // gets a chance to settle. If a clean match arrives in the
                            // meantime the cancel above suppresses this. Otherwise we
                            // fire ONCE and flip mismatchWarningEmitted so the duplicate
                            // realm/progress callbacks don't surface a second toast.
                            mismatchWarningPending = Scheduler.AddDelayed(() =>
                            {
                                mismatchWarningPending = null;
                                if (queryBeatmap().Any())
                                    return;
                                mismatchWarningEmitted = true;
                                Logger.Log("The imported beatmap set does not match the online version.", LoggingTarget.Runtime, LogLevel.Important);
                            }, 1500);
                        }

                        break;

                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            IQueryable<BeatmapInfo> queryBeatmap() =>
                realm.Realm.All<BeatmapInfo>()
                     .ForOnlineId(beatmap.OnlineID);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            realmSubscription?.Dispose();
        }
    }
}
