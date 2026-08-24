// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
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
using osu.Game.Online.API.Requests.Responses;
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
        private IBindable<APIState> apiState;
        private bool busy;

        /// <summary>
        /// Ya sabemos en que parte del historial estaba esta cuenta cuando abrio el juego.
        /// Hasta que no se sepa no se festeja nada: lo que se gano con el juego cerrado no
        /// es algo que la persona "acaba de hacer", y tirarle la lista de los ultimos tres
        /// dias cada vez que abre es exactamente el cartel que nadie queria.
        /// </summary>
        private bool synced;

        public ToriiPointsWatcher()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            cursor = config?.GetBindable<int>(OsuSetting.ToriiPointsFeedCursor) ?? new Bindable<int>(0);
            reducedMotion = config?.GetBindable<bool>(OsuSetting.CosmeticsReducedMotion) ?? new Bindable<bool>(false);

            apiState = api?.State.GetBoundCopy();
            apiState?.BindValueChanged(state =>
            {
                if (state.NewValue == APIState.Online)
                {
                    // al entrar se adelanta el cursor en silencio hasta el final: eso
                    // deja la linea en "desde aca es esta sesion".
                    Schedule(() => check(false));
                }
                else if (state.NewValue == APIState.Offline)
                {
                    // se fue o cambio de cuenta: la linea se vuelve a trazar al entrar.
                    synced = false;
                }
            }, true);

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
        public void MarkPlayed() => Scheduler.AddDelayed(() => check(true), 2800);

        /// <summary>Back at the menu — a calm moment to catch anything missed.</summary>
        public void OnMenu() => check(true);

        /// <param name="celebrate">
        /// Si lo que venga se muestra o se consume callado. Callado es como se adelanta el
        /// cursor al entrar, y tambien el modo seguro: mientras no sepamos donde estaba
        /// esta cuenta, es preferible perderse un cartel que tirarle la lista entera.
        /// </param>
        private void check(bool celebrate)
        {
            if (busy || api?.IsLoggedIn != true || config == null)
                return;

            celebrate &= synced;

            busy = true;
            int since = cursor.Value;

            // vaciando el historial viejo se va de a tandas grandes: con el limite chico
            // harian falta muchas vueltas y la primera que sobrara saldria como cartel.
            int limit = celebrate ? 20 : 100;

            var req = new GetPointsFeedRequest(since, limit);
            req.Success += res => Schedule(() =>
            {
                busy = false;

                if (res == null)
                    return;

                if (cosmetics != null && res.Balance > 0)
                    cosmetics.PointsBalance.Value = res.Balance;

                int batch = res.Events?.Length ?? 0;

                foreach (var ev in res.Events ?? Array.Empty<APIPointEvent>())
                {
                    if (ev.Id > cursor.Value)
                        cursor.Value = ev.Id;
                }

                if (res.LastId > cursor.Value)
                    cursor.Value = res.LastId;

                if (!celebrate)
                {
                    // habia mas de una tanda para vaciar: se sigue antes de darse por
                    // sincronizado, si no la tanda que sobra sale como cartel despues.
                    if (batch >= limit)
                    {
                        check(false);
                        return;
                    }

                    synced = true;
                    return;
                }

                // Fold this batch's celebratable earns into ONE summary card.
                var cardLines = (res.Events ?? Array.Empty<APIPointEvent>())
                                .OrderBy(e => e.Id)
                                .Where(e => shouldInclude(e.Reason) && justHappened(e))
                                .Select(e => new PointsEarnedCard.Line(e.Amount, e.Reason, e.Ref))
                                .ToList();

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

        /// <summary>
        /// Recien ganado, no de hace rato. La regla de la sesion ya tapa lo del juego
        /// cerrado, pero mucha gente lo deja abierto toda la noche: lo que el cron del
        /// daily le dio a las 12 no es algo que "acaba de hacer" cuando vuelve a las 8.
        /// </summary>
        private static bool justHappened(APIPointEvent ev)
        {
            if (ev.CreatedAt == default)
                return true;

            var age = DateTimeOffset.UtcNow - ev.CreatedAt.ToUniversalTime();

            // adelantado respecto de nosotros: es un reloj que no coincide, no una fecha
            // vieja. Ahi no se descarta nada y decide la regla de la sesion.
            if (age < TimeSpan.FromMinutes(-2))
                return true;

            return age < TimeSpan.FromMinutes(10);
        }
    }
}
