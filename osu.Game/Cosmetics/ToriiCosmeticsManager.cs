// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Configuration;

namespace osu.Game.Cosmetics
{
    /// <summary>
    /// Central, app-wide state for the cursor-trail cosmetics store: what the
    /// user owns, what's equipped, customisation unlock + per-trail length/
    /// density, and the points balance. Cached in OsuGameBase so the cursor
    /// containers (gameplay + menu) and the store overlay all share it.
    ///
    /// Persistence is client-side via OsuConfigManager for now. Points are a
    /// LOCAL cache here; spending/earning will be reconciled with the g0v0
    /// server when the shop endpoint ships. Owned/equipped likewise sync later.
    /// </summary>
    public class ToriiCosmeticsManager
    {
        private readonly OsuConfigManager config;

        /// <summary>Equipped trail id ("" = none, use the skin's trail).</summary>
        public Bindable<string> EquippedTrailId { get; }

        /// <summary>Local points balance cache (server is authoritative later).</summary>
        public Bindable<int> PointsBalance { get; }

        /// <summary>Fires when ownership / customisation changes, so the store
        /// UI can refresh.</summary>
        public event Action InventoryChanged;

        /// <summary>Fires (with the trail id) when a trail's length/density is
        /// tweaked, so an already-equipped live trail can re-apply it on the fly
        /// instead of only picking it up on the next equip.</summary>
        public event Action<string> CustomisationChanged;

        /// <summary>Local starter balance, granted once per profile (placeholder
        /// until the server economy is wired).</summary>
        public const int StartingPoints = 90000;

        public ToriiCosmeticsManager(OsuConfigManager config)
        {
            this.config = config;
            EquippedTrailId = config.GetBindable<string>(OsuSetting.EquippedCursorTrail);
            PointsBalance = config.GetBindable<int>(OsuSetting.ToriiPointsBalance);

            // Grant the starter balance once (covers profiles created before this).
            if (!config.Get<bool>(OsuSetting.ToriiPointsSeeded))
            {
                PointsBalance.Value = StartingPoints;
                config.SetValue(OsuSetting.ToriiPointsSeeded, true);
            }
        }

        // ── Ownership ───────────────────────────────────────────────────────

        public bool IsOwned(string id) => ownedSet().Contains(id);

        public IReadOnlyCollection<string> OwnedIds => ownedSet();

        /// <summary>Account-wide unlock that enables the length/density sliders.</summary>
        public bool AdjustUnlocked => config.Get<bool>(OsuSetting.CursorTrailAdjustUnlocked);

        public bool CanAfford(int price) => PointsBalance.Value >= price;

        /// <summary>Buy a trail. Returns true if the purchase went through.</summary>
        public bool Buy(string id, int price)
        {
            if (IsOwned(id) || !CanAfford(price))
                return false;

            PointsBalance.Value -= price;
            var set = ownedSet();
            set.Add(id);
            config.SetValue(OsuSetting.OwnedCursorTrails, string.Join(",", set));
            InventoryChanged?.Invoke();
            return true;
        }

        /// <summary>Buy the account-wide length/density customisation unlock.</summary>
        public bool BuyAdjustUnlock(int price)
        {
            if (AdjustUnlocked || !CanAfford(price))
                return false;

            PointsBalance.Value -= price;
            config.SetValue(OsuSetting.CursorTrailAdjustUnlocked, true);
            InventoryChanged?.Invoke();
            return true;
        }

        // ── Equip ───────────────────────────────────────────────────────────

        public void Equip(string id) => EquippedTrailId.Value = id ?? string.Empty;

        public void Unequip() => EquippedTrailId.Value = string.Empty;

        // ── Per-trail customisation (length / density multipliers) ──────────

        public (float length, float density) GetCustomisation(string id)
        {
            foreach (string entry in config.Get<string>(OsuSetting.CursorTrailCustomisations).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split(':');
                if (parts.Length == 3 && parts[0] == id
                    && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float len)
                    && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float dens))
                    return (len, dens);
            }

            return (1f, 1f);
        }

        public void SetCustomisation(string id, float length, float density)
        {
            var map = new Dictionary<string, (float, float)>();
            foreach (string entry in config.Get<string>(OsuSetting.CursorTrailCustomisations).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split(':');
                if (parts.Length == 3
                    && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float l)
                    && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float d))
                    map[parts[0]] = (l, d);
            }

            map[id] = (length, density);
            config.SetValue(OsuSetting.CursorTrailCustomisations,
                string.Join(";", map.Select(kv => $"{kv.Key}:{kv.Value.Item1.ToString(CultureInfo.InvariantCulture)}:{kv.Value.Item2.ToString(CultureInfo.InvariantCulture)}")));

            // Deliberately NOT InventoryChanged (that rebuilds the whole card
            // grid, and this fires per slider tick). CustomisationChanged is the
            // light-touch signal: a live equipped trail just re-applies the new
            // multipliers, no rebuild.
            CustomisationChanged?.Invoke(id);
        }

        /// <summary>Re-apply the saved length/density for <paramref name="id"/>
        /// to an already-built trail instance (used for live slider updates on
        /// the equipped cursor). No-op if customisation isn't unlocked.</summary>
        public void ApplyCustomisationTo(Drawable trail, string id)
        {
            if (trail is not ICosmeticTrail t || !AdjustUnlocked)
                return;

            var (length, density) = GetCustomisation(id);
            t.SetLengthMultiplier(length);
            t.SetDensityMultiplier(density);
        }

        // ── Trail building (used by the cursor containers) ──────────────────

        /// <summary>Build a fresh drawable for the equipped trail (customised if
        /// unlocked), or null if nothing is equipped / the id is unknown.</summary>
        public Drawable CreateEquippedTrail()
        {
            string id = EquippedTrailId.Value;
            if (string.IsNullOrEmpty(id))
                return null;

            var def = CosmeticCatalog.Trails.FirstOrDefault(d => d.Id == id);
            if (def == null)
                return null;

            var drawable = def.Create();
            if (drawable is ICosmeticTrail trail && AdjustUnlocked)
            {
                var (length, density) = GetCustomisation(id);
                trail.SetLengthMultiplier(length);
                trail.SetDensityMultiplier(density);
            }

            return drawable;
        }

        // ── Daily store rotation (Fortnite-style) ───────────────────────────

        /// <summary>The featured trails for today (UTC), rotating every 24h. A
        /// date-seeded shuffle so every client shows the same daily selection.</summary>
        public IReadOnlyList<CosmeticTrailDefinition> GetDailyStore(int count = 6)
        {
            int seed = (int)(DateTime.UtcNow.Date.Ticks / TimeSpan.TicksPerDay);
            var rng = new Random(seed);
            return CosmeticCatalog.Trails.OrderBy(_ => rng.Next()).Take(count).ToList();
        }

        /// <summary>Seconds until the daily store rotates (next UTC midnight).</summary>
        public double SecondsUntilRotation()
        {
            DateTime now = DateTime.UtcNow;
            return (now.Date.AddDays(1) - now).TotalSeconds;
        }

        private HashSet<string> ownedSet()
        {
            var set = new HashSet<string>();
            foreach (string s in config.Get<string>(OsuSetting.OwnedCursorTrails).Split(',', StringSplitOptions.RemoveEmptyEntries))
                set.Add(s);
            return set;
        }
    }
}
