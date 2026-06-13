// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Cosmetics;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>
    /// Watches for the player completing a map, then (back at the menu) quietly
    /// checks for a pending gift and reveals one — so gifts arrive after play
    /// rather than nagging the moment the game opens. One gift per visit, paced
    /// across plays.
    /// </summary>
    public partial class ToriiGiftWatcher : Component
    {
        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        [Resolved(canBeNull: true)]
        private ToriiCosmeticsManager cosmetics { get; set; }

        [Resolved(canBeNull: true)]
        private ToriiGiftOverlay giftOverlay { get; set; }

        private bool playedSinceCheck;
        private bool busy;

        /// <summary>Mark that the player finished a map (a results screen showed).</summary>
        public void MarkPlayed() => playedSinceCheck = true;

        /// <summary>Player is back at the main menu — a calm moment to reveal a
        /// gift if they've played since the last check.</summary>
        public void OnMenu()
        {
            if (!playedSinceCheck || busy)
                return;

            playedSinceCheck = false;
            checkForGift();
        }

        private void checkForGift()
        {
            if (api?.IsLoggedIn != true || giftOverlay == null)
                return;

            busy = true;

            var req = new GetPendingGiftsRequest();
            req.Success += res => Schedule(() =>
            {
                var gift = res.Gifts?.FirstOrDefault();
                if (gift == null)
                {
                    busy = false;
                    return;
                }

                claim(gift.Id);
            });
            req.Failure += _ => Schedule(() => busy = false);
            api.Queue(req);
        }

        private void claim(int giftId)
        {
            var req = new ClaimGiftRequest(giftId);
            req.Success += res => Schedule(() =>
            {
                busy = false;

                if (cosmetics != null)
                {
                    cosmetics.PointsBalance.Value = res.Balance;
                    foreach (string c in res.GrantedCosmetics ?? Array.Empty<string>())
                        cosmetics.Grant(c);
                }

                giftOverlay?.Display(res.Sender, res.Message, res.Points, res.GrantedCosmetics);
            });
            req.Failure += _ => Schedule(() => busy = false);
            api.Queue(req);
        }
    }
}
