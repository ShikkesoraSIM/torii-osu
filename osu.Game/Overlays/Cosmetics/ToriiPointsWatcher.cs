// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Configuration;
using osu.Game.Cosmetics;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>
    /// Watches the server points ledger and pops a "+N points" toast after a play,
    /// explaining why it was earned. Modeled on <see cref="ToriiGiftWatcher"/>: it
    /// checks at calm moments (a play just finished / back at the menu) and advances
    /// a persisted cursor so each earn is celebrated exactly once. Reasons that have
    /// their own popup (gifts, redeemed codes) are consumed silently here to avoid a
    /// double celebration. Hosts the toast layer itself.
    /// </summary>
    public partial class ToriiPointsWatcher : Container
    {
        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        [Resolved(canBeNull: true)]
        private OsuConfigManager config { get; set; }

        [Resolved(canBeNull: true)]
        private ToriiCosmeticsManager cosmetics { get; set; }

        private Bindable<int> cursor;
        private Bindable<bool> reducedMotion;
        private FillFlowContainer<PointsEarnedToast> flow;
        private bool busy;

        public ToriiPointsWatcher()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            cursor = config?.GetBindable<int>(OsuSetting.ToriiPointsFeedCursor) ?? new Bindable<int>(0);
            reducedMotion = config?.GetBindable<bool>(OsuSetting.CosmeticsReducedMotion) ?? new Bindable<bool>(false);

            Child = flow = new FillFlowContainer<PointsEarnedToast>
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, BriefingTheme.SpacingSm),
                Y = 72,
            };
        }

        /// <summary>A play just finished — check shortly after so points have committed server-side.</summary>
        public void MarkPlayed() => Scheduler.AddDelayed(check, 1200);

        /// <summary>Back at the menu — a calm moment to catch anything missed.</summary>
        public void OnMenu() => check();

        private void check()
        {
            if (busy || api?.IsLoggedIn != true || config == null)
                return;

            busy = true;
            int since = cursor.Value;

            var req = new GetPointsFeedRequest(since);
            req.Success += res => Schedule(() =>
            {
                busy = false;

                if (res == null)
                    return;

                if (cosmetics != null && res.Balance > 0)
                    cosmetics.PointsBalance.Value = res.Balance;

                if (res.Events == null || res.Events.Length == 0)
                {
                    if (res.LastId > cursor.Value)
                        cursor.Value = res.LastId;
                    return;
                }

                // First sync on a fresh client (cursor 0): adopt the latest id without
                // replaying the whole history as a wall of toasts.
                if (since == 0)
                {
                    cursor.Value = res.LastId;
                    return;
                }

                foreach (var ev in res.Events.OrderBy(e => e.Id))
                {
                    if (shouldToast(ev.Reason))
                        flow.Add(new PointsEarnedToast(ev.Amount, ev.Reason, ev.Ref, reducedMotion.Value, ev.BalanceAfter));

                    if (ev.Id > cursor.Value)
                        cursor.Value = ev.Id;
                }

                if (res.LastId > cursor.Value)
                    cursor.Value = res.LastId;
            });
            req.Failure += _ => Schedule(() => busy = false);
            api.Queue(req);
        }

        // Gifts, redeemed codes and medals already get their own celebration
        // popup / overlay, so don't double-toast (or double-sound) those.
        private static bool shouldToast(string reason) =>
            reason != "gift" && reason != "access_code" && reason != "medal";
    }
}
