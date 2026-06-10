// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Configuration;
using osu.Game.Cosmetics;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>
    /// Watches the server points ledger and, after a play, pops ONE aggregated
    /// "points earned" summary — top play + daily + pp milestone + medals folded
    /// into a single card with the grand total, the breakdown and the new balance.
    /// Checks at calm moments (a play just finished / back at the menu) and advances
    /// a persisted cursor so each earn is celebrated exactly once. Gifts and
    /// redeemed codes have their own reveal overlays, so they're consumed silently.
    /// Hosts the card layer itself.
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
        private FillFlowContainer<PointsEarnedCard> flow;
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

            Child = flow = new FillFlowContainer<PointsEarnedCard>
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, BriefingTheme.SpacingSm),
                Y = 72,
            };
        }

        /// <summary>A play just finished. Wait a beat longer than a snappy toast so
        /// the medal pop + every award (top play is committed in the stats pass)
        /// have settled — then the summary appears once, as its own clear moment,
        /// instead of racing the medals.</summary>
        public void MarkPlayed() => Scheduler.AddDelayed(check, 2800);

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

                // First sync on a fresh client (cursor 0): adopt the latest id
                // without replaying the whole history as a wall of cards.
                if (since == 0)
                {
                    cursor.Value = res.LastId;
                    return;
                }

                // Fold this batch's celebratable earns into ONE summary card.
                var cardLines = new List<PointsEarnedCard.Line>();

                foreach (var ev in res.Events.OrderBy(e => e.Id))
                {
                    if (shouldInclude(ev.Reason))
                        cardLines.Add(new PointsEarnedCard.Line(ev.Amount, ev.Reason, ev.Ref));

                    if (ev.Id > cursor.Value)
                        cursor.Value = ev.Id;
                }

                if (res.LastId > cursor.Value)
                    cursor.Value = res.LastId;

                if (cardLines.Count > 0)
                    flow.Add(new PointsEarnedCard(cardLines, res.Balance, reducedMotion.Value));
            });
            req.Failure += _ => Schedule(() => busy = false);
            api.Queue(req);
        }

        // Gifts + redeemed codes get their own full reveal overlay, so don't fold
        // them into the play summary. Medals ARE included now — their points count
        // toward the total even though the medal unlock has its own animation.
        private static bool shouldInclude(string reason) =>
            reason != "gift" && reason != "access_code";
    }
}
