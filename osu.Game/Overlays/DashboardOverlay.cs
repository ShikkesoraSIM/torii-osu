// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Game.Online.Metadata;
using osu.Game.Overlays.Dashboard;
using osu.Game.Overlays.Dashboard.CurrentlyOnline;
using osu.Game.Overlays.Dashboard.Friends;
using osu.Game.Overlays.Dashboard.UserSearch;

namespace osu.Game.Overlays
{
    public partial class DashboardOverlay : TabbableOnlineOverlay<DashboardOverlayHeader, DashboardOverlayTabs>
    {
        [Resolved]
        private MetadataClient metadataClient { get; set; } = null!;

        private IBindable<bool> metadataConnected = null!;
        private IDisposable? userPresenceWatchToken;

        public DashboardOverlay()
            : base(OverlayColourScheme.Purple)
        {
        }

        protected override DashboardOverlayHeader CreateHeader() => new DashboardOverlayHeader();

        public override bool AcceptsFocus => false;

        protected override void CreateDisplayToLoad(DashboardOverlayTabs tab)
        {
            switch (tab)
            {
                case DashboardOverlayTabs.Friends:
                    LoadDisplay(new FriendDisplay());
                    break;

                case DashboardOverlayTabs.CurrentlyPlaying:
                    LoadDisplay(new CurrentlyOnlineDisplay());
                    break;

                case DashboardOverlayTabs.UserSearch:
                    // Upstream binds UserSearchDisplay.Loading to a central
                    // BindableBool that drives the OnlineOverlay loading
                    // layer. Our DashboardOverlay doesn't have that field
                    // (older fork lineage), and adding it would touch the
                    // existing Friends / CurrentlyPlaying paths too. Skip
                    // the bind — UserSearchDisplay still tracks its own
                    // loading state internally for UI feedback, we just
                    // don't surface the spinner overlay.
                    LoadDisplay(new UserSearchDisplay());
                    break;

                default:
                    throw new NotImplementedException($"Display for {tab} tab is not implemented");
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            metadataConnected = metadataClient.IsConnected.GetBoundCopy();
            metadataConnected.BindValueChanged(_ => updateUserPresenceState());
            State.BindValueChanged(_ => updateUserPresenceState());
            updateUserPresenceState();
        }

        private void updateUserPresenceState()
        {
            if (!metadataConnected.Value)
                return;

            if (State.Value == Visibility.Visible)
                userPresenceWatchToken ??= metadataClient.BeginWatchingUserPresence();
            else
            {
                userPresenceWatchToken?.Dispose();
                userPresenceWatchToken = null;
            }
        }
    }
}
