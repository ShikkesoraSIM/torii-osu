// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Game.Users;

namespace osu.Game.Online.Metadata
{
    /// <summary>
    /// Interface for metadata-related remote procedure calls to be executed on the client side.
    /// </summary>
    public interface IMetadataClient : IStatefulUserHubClient
    {
        /// <summary>
        /// Delivers the set of requested <see cref="BeatmapUpdates"/> to the client.
        /// </summary>
        Task BeatmapSetsUpdated(BeatmapUpdates updates);

        /// <summary>
        /// Delivers an update of the <see cref="UserPresence"/> of the user with the supplied <paramref name="userId"/>.
        /// </summary>
        Task UserPresenceUpdated(int userId, UserPresence? status);

        /// <summary>
        /// Delivers and update of the <see cref="UserPresence"/> of a friend with the supplied <paramref name="userId"/>.
        /// </summary>
        Task FriendPresenceUpdated(int userId, UserPresence? presence);

        /// <summary>
        /// Delivers an update of the current "daily challenge" status.
        /// Null value means there is no "daily challenge" currently active.
        /// </summary>
        Task DailyChallengeUpdated(DailyChallengeInfo? info);

        /// <summary>
        /// Delivers information that a multiplayer score was set in a watched room.
        /// To receive these, the client must call <see cref="IMetadataServer.BeginWatchingMultiplayerRoom"/> for a given room first.
        /// </summary>
        Task MultiplayerRoomScoreSet(MultiplayerRoomScoreSetEvent roomScoreSetEvent);

        /// <summary>
        /// Notifies clients that the public-facing payload of <paramref name="userId"/>
        /// has changed (equipped aura, group membership, custom title, profile
        /// hue, ...). Receiving clients should invalidate any cached APIUser
        /// snapshot for that user — typically by re-fetching the user via
        /// <c>GetUserRequest</c> the next time it's needed, or by calling
        /// <see cref="IAPIProvider.RefreshLocalUser"/> when the broadcast
        /// targets the locally-signed-in user.
        /// </summary>
        /// <remarks>
        /// Cheap signal — the payload is just an int, so blasting this on
        /// every cosmetic change is fine even at scale. Concrete listeners
        /// then decide how aggressively to refresh based on whether the
        /// affected user is currently visible to them.
        /// </remarks>
        Task UserUpdated(int userId);
    }
}
