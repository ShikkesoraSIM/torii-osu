// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Online.Metadata;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Users;
using osuTK.Graphics;
using System;
using osu.Framework.Logging;

namespace osu.Game.Online
{
    public partial class FriendPresenceNotifier : Component
    {
        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private MetadataClient metadataClient { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private readonly Bindable<bool> notifyOnFriendPresenceChange = new BindableBool();

        private readonly IBindableList<APIRelation> friends = new BindableList<APIRelation>();
        private readonly IBindableDictionary<int, UserPresence> friendPresences = new BindableDictionary<int, UserPresence>();

        private readonly HashSet<APIUser> onlineAlertQueue = new HashSet<APIUser>();
        private readonly HashSet<APIUser> offlineAlertQueue = new HashSet<APIUser>();

        // Track previous online/offline state for each friend to only notify on actual state changes
        private readonly Dictionary<int, bool> friendOnlineStates = new Dictionary<int, bool>();

        private double? lastOnlineAlertTime;
        private double? lastOfflineAlertTime;

        private bool hasInitialised;
        private CancellationTokenSource? initialScanCts;
        private IDisposable? userPresenceWatchToken;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            config.BindWith(OsuSetting.NotifyOnFriendPresenceChange, notifyOnFriendPresenceChange);

            friends.BindTo(api.LocalUserState.Friends);
            // Do not fire the collection-changed handler immediately; we'll process initial
            // friend statuses explicitly so we can control whether notifications are posted.
            friends.BindCollectionChanged(onFriendsChanged, false);

            // Begin watching friend presence on the metadata client so we receive updates.
            userPresenceWatchToken = metadataClient.BeginWatchingUserPresence();

            friendPresences.BindTo(metadataClient.FriendPresences);
            friendPresences.BindCollectionChanged(onFriendPresenceChanged, true);

            // Also observe global user presences in case updates arrive there instead of friend-only
            // dictionaries. Some presence updates may be published to `UserPresences` instead of
            // `FriendPresences`; handle those similarly so runtime updates still produce notifications.
            metadataClient.UserPresences.BindCollectionChanged(onUserPresencesChanged, true);

            // Process initial friend statuses now: notify about currently online/offline friends
            // that already exist in the friends list at startup. Do not notify for friends
            // that are added later while the client is running.
            processInitialFriendStatuses();

            hasInitialised = true;

            // Schedule a delayed re-scan in case presence data arrives shortly after startup.
            initialScanCts?.Cancel();
            initialScanCts = new CancellationTokenSource();
            var ct = initialScanCts.Token;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500, ct).ConfigureAwait(false);
                    if (!ct.IsCancellationRequested)
                        Schedule(() => processInitialFriendStatuses());
                }
                catch (TaskCanceledException) { }
            }, ct);
        }

        private void onUserPresencesChanged(object? sender, NotifyDictionaryChangedEventArgs<int, UserPresence> e)
        {
            switch (e.Action)
            {
                case NotifyDictionaryChangedAction.Add:
                case NotifyDictionaryChangedAction.Replace:
                    foreach ((int id, UserPresence presence) in e.NewItems!)
                    {
                        APIRelation? friend = friends.FirstOrDefault(f => f.TargetID == id);

                        if (friend?.TargetUser is APIUser user)
                        {
                            // Check if user is actually online based on Status, not just presence existence
                            bool isOnline = isUserOnline(presence);
                            updateUserOnlineState(user, id, isOnline);
                        }
                    }

                    break;

                case NotifyDictionaryChangedAction.Remove:
                    foreach ((int id, _) in e.OldItems!)
                    {
                        APIRelation? friend = friends.FirstOrDefault(f => f.TargetID == id);

                        if (friend?.TargetUser is APIUser user)
                        {
                            // When presence is removed, user is offline
                            updateUserOnlineState(user, id, false);
                        }
                    }

                    break;
            }
        }

        protected override void Update()
        {
            base.Update();

            alertOnlineUsers();
            alertOfflineUsers();
        }

        private void onFriendsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                // Ignore additions while running: do not post notifications when the local user
                // actively adds someone as a friend. Initial startup processing is handled
                // separately in processInitialFriendStatuses().
                case NotifyCollectionChangedAction.Add:
                    break;

                case NotifyCollectionChangedAction.Remove:
                    foreach (APIRelation friend in e.OldItems!.Cast<APIRelation>())
                    {
                        if (friend.TargetUser is not APIUser user)
                            continue;

                        onlineAlertQueue.Remove(user);
                        offlineAlertQueue.Remove(user);
                        friendOnlineStates.Remove(friend.TargetID);
                    }

                    break;
            }
        }

        private void onFriendPresenceChanged(object? sender, NotifyDictionaryChangedEventArgs<int, UserPresence> e)
        {
            switch (e.Action)
            {
                case NotifyDictionaryChangedAction.Add:
                case NotifyDictionaryChangedAction.Replace:
                    foreach ((int friendId, UserPresence presence) in e.NewItems!)
                    {
                        // Use canonical presence lookup to determine online state. This ensures
                        // consistency with the Dashboard and other consumers of presence data.
                        APIRelation? friend = friends.FirstOrDefault(f => f.TargetID == friendId);

                        if (friend?.TargetUser is APIUser user)
                        {
                            // Check if user is actually online based on Status, not just presence existence
                            bool isOnline = isUserOnline(presence);
                            updateUserOnlineState(user, friendId, isOnline);
                        }
                    }

                    break;

                case NotifyDictionaryChangedAction.Remove:
                    foreach ((int friendId, _) in e.OldItems!)
                    {
                        // Use canonical presence lookup to determine online state; removal events
                        // may indicate absence of presence across friend/user presences.
                        APIRelation? friend = friends.FirstOrDefault(f => f.TargetID == friendId);

                        if (friend?.TargetUser is APIUser user)
                        {
                            // When presence is removed, check current state via GetPresence
                            var presence = metadataClient.GetPresence(friendId);
                            bool isOnline = presence.HasValue && isUserOnline(presence.Value);
                            updateUserOnlineState(user, friendId, isOnline);
                        }
                    }

                    break;
            }
        }

        private void processInitialFriendStatuses()
        {
            // Iterate the current friends list and post notifications for their current presence
            // state if available. This is only run once on startup.
            foreach (var relation in friends)
            {
                if (relation.TargetUser is not APIUser user)
                    continue;

                var presence = metadataClient.GetPresence(relation.TargetID);
                bool isOnline = presence.HasValue && isUserOnline(presence.Value);
                // Initialize state tracking but don't notify on initial load
                friendOnlineStates[relation.TargetID] = isOnline;
            }
        }

        /// <summary>
        /// Determines if a user is online based on their presence Status field.
        /// Only UserStatus.Online is considered online; Offline, DoNotDisturb, or null are offline.
        /// </summary>
        private bool isUserOnline(UserPresence? presence)
        {
            if (!presence.HasValue)
                return false;

            return presence.Value.Status == UserStatus.Online;
        }

        /// <summary>
        /// Updates the online state for a user and only triggers notifications if the state actually changed.
        /// </summary>
        private void updateUserOnlineState(APIUser user, int userId, bool isOnline)
        {
            // Check if state actually changed
            if (friendOnlineStates.TryGetValue(userId, out bool previousState))
            {
                // Only notify if state changed from online to offline or vice versa
                if (previousState == isOnline)
                {
                    // State hasn't changed, just update tracking (might be activity change)
                    friendOnlineStates[userId] = isOnline;
                    return;
                }
            }

            // State changed, update tracking and queue notification
            friendOnlineStates[userId] = isOnline;

            if (isOnline)
                markUserOnline(user);
            else
                markUserOffline(user);
        }

        private void markUserOnline(APIUser user)
        {
            if (!offlineAlertQueue.Remove(user))
            {
                onlineAlertQueue.Add(user);
                lastOnlineAlertTime ??= Time.Current;
            }
        }

        private void markUserOffline(APIUser user)
        {
            if (!onlineAlertQueue.Remove(user))
            {
                offlineAlertQueue.Add(user);
                lastOfflineAlertTime ??= Time.Current;
            }
        }

        private void alertOnlineUsers()
        {
            if (onlineAlertQueue.Count == 0)
                return;

            if (lastOnlineAlertTime == null || Time.Current - lastOnlineAlertTime < 1000)
                return;

            if (!notifyOnFriendPresenceChange.Value)
            {
                lastOnlineAlertTime = null;
                return;
            }

            Logger.Log($"FriendPresenceNotifier: posting online notification for {onlineAlertQueue.Count} user(s): {string.Join(", ", onlineAlertQueue.Select(u => u.Username))}");
            notifications.Post(new FriendOnlineNotification(onlineAlertQueue.ToArray()));

            onlineAlertQueue.Clear();
            lastOnlineAlertTime = null;
        }

        private void alertOfflineUsers()
        {
            if (offlineAlertQueue.Count == 0)
                return;

            if (lastOfflineAlertTime == null || Time.Current - lastOfflineAlertTime < 1000)
                return;

            if (!notifyOnFriendPresenceChange.Value)
            {
                lastOfflineAlertTime = null;
                return;
            }

            Logger.Log($"FriendPresenceNotifier: posting offline notification for {offlineAlertQueue.Count} user(s): {string.Join(", ", offlineAlertQueue.Select(u => u.Username))}");
            notifications.Post(new FriendOfflineNotification(offlineAlertQueue.ToArray()));

            offlineAlertQueue.Clear();
            lastOfflineAlertTime = null;
        }

        public partial class FriendOnlineNotification : UserAvatarNotification
        {
            private readonly ICollection<APIUser> users;

            public FriendOnlineNotification(ICollection<APIUser> users)
                : base(users.Count == 1 ? users.Single() : null)
            {
                this.users = users;

                Transient = false;
                IsImportant = false;
                Text = $"Online: {string.Join(@", ", users.Select(u => u.Username))}";
            }

            [BackgroundDependencyLoader]
            private void load(OsuColour colours, ChannelManager channelManager, ChatOverlay chatOverlay)
            {
                if (users.Count > 1)
                {
                    Icon = FontAwesome.Solid.User;
                    IconColour = colours.GrayD;
                }
                else
                {
                    Activated = () =>
                    {
                        channelManager.OpenPrivateChannel(users.Single());
                        chatOverlay.Show();

                        return true;
                    };
                }
            }

            public override string PopInSampleName => "UI/notification-friend-online";
        }

        public partial class FriendOfflineNotification : UserAvatarNotification
        {
            private readonly ICollection<APIUser> users;

            public FriendOfflineNotification(ICollection<APIUser> users)
                : base(users.Count == 1 ? users.Single() : null)
            {
                this.users = users;

                Transient = false;
                IsImportant = false;
                Text = $"Offline: {string.Join(@", ", users.Select(u => u.Username))}";
            }

            [BackgroundDependencyLoader]
            private void load(OsuColour colours)
            {
                Icon = FontAwesome.Solid.UserSlash;

                if (users.Count == 1)
                    Avatar.Colour = Color4.White.Opacity(0.25f);
                else
                    IconColour = colours.Gray3;
            }

            public override string PopInSampleName => "UI/notification-friend-offline";
        }

        protected override void Dispose(bool isDisposing)
        {
            initialScanCts?.Cancel();
            initialScanCts?.Dispose();
            userPresenceWatchToken?.Dispose();
            base.Dispose(isDisposing);
        }
    }
}
