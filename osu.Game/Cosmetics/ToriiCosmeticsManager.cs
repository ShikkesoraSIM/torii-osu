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

        /// <summary>Equipped username-colour id ("" = default white). Ownership
        /// is shared with everything else via <see cref="IsOwned"/> / <see cref="Buy"/>.</summary>
        public Bindable<string> EquippedNameColourId { get; }

        /// <summary>Local points balance cache (server is authoritative later).</summary>
        public Bindable<int> PointsBalance { get; }

        /// <summary>"Potato PC" mode: store previews show a frozen snapshot
        /// instead of animating live, for weak hardware.</summary>
        public Bindable<bool> StorePotatoMode { get; }

        /// <summary>Fires when ownership / customisation changes, so the store
        /// UI can refresh.</summary>
        public event Action InventoryChanged;

        /// <summary>Fires (with the trail id) when a trail's length/density is
        /// tweaked, so an already-equipped live trail can re-apply it on the fly
        /// instead of only picking it up on the next equip.</summary>
        public event Action<string> CustomisationChanged;

        /// <summary>Starter balance. The economy is server-authoritative now, so this
        /// is 0; the real balance is fetched from g0v0 and cached in <see cref="PointsBalance"/>.</summary>
        public const int StartingPoints = 0;

        /// <summary>Set by the store overlay (which has API access): records a purchase
        /// server-side and reconciles the balance from the response. Null = offline /
        /// pre-server, in which case spending stays local only.</summary>
        public Action<string, int> ServerPurchase;

        public ToriiCosmeticsManager(OsuConfigManager config)
        {
            this.config = config;
            EquippedTrailId = config.GetBindable<string>(OsuSetting.EquippedCursorTrail);
            EquippedNameColourId = config.GetBindable<string>(OsuSetting.EquippedNameColour);
            PointsBalance = config.GetBindable<int>(OsuSetting.ToriiPointsBalance);
            StorePotatoMode = config.GetBindable<bool>(OsuSetting.CosmeticStorePotatoMode);
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
            ServerPurchase?.Invoke(id, price);
            return true;
        }

        /// <summary>Unlock a cosmetic for free (e.g. redeemed via an access code).
        /// Marks it owned without spending points. Returns true if newly owned.</summary>
        public bool Grant(string id)
        {
            if (string.IsNullOrEmpty(id) || IsOwned(id))
                return false;

            var set = ownedSet();
            set.Add(id);
            config.SetValue(OsuSetting.OwnedCursorTrails, string.Join(",", set));
            InventoryChanged?.Invoke();
            return true;
        }

        /// <summary>Overwrite the local balance cache with the server's authoritative value.</summary>
        public void SyncBalance(int serverBalance) => PointsBalance.Value = serverBalance;

        /// <summary>Merge server-owned cosmetic ids into the local owned set (the server
        /// is the source of truth; local extras are never removed).</summary>
        public void SyncOwned(IEnumerable<string> serverOwned)
        {
            if (serverOwned == null)
                return;

            var set = ownedSet();
            bool changed = false;
            foreach (string id in serverOwned)
                if (!string.IsNullOrEmpty(id) && set.Add(id))
                    changed = true;

            if (changed)
            {
                config.SetValue(OsuSetting.OwnedCursorTrails, string.Join(",", set));
                InventoryChanged?.Invoke();
            }
        }

        /// <summary>Buy the account-wide length/density customisation unlock.</summary>
        public bool BuyAdjustUnlock(int price)
        {
            if (AdjustUnlocked || !CanAfford(price))
                return false;

            PointsBalance.Value -= price;
            config.SetValue(OsuSetting.CursorTrailAdjustUnlocked, true);
            InventoryChanged?.Invoke();
            ServerPurchase?.Invoke("customisation-unlock", price);
            return true;
        }

        // ── Store curation (admin) ──────────────────────────────────────────
        // Which catalog items an admin has pulled OUT of the store pool. Local
        // stand-in until the server owns the store config; persisted the same
        // comma-separated way as ownership. Only affects what is offered FOR
        // SALE — already-owned items still show in the inventory.

        /// <summary>Fires when the admin store curation changes.</summary>
        public event Action StoreCurationChanged;

        /// <summary>True if this catalog id is currently sellable (an admin has
        /// not pulled it from the store pool).</summary>
        public bool IsStoreEnabled(string id) => !storeDisabledSet().Contains(id);

        /// <summary>Admin: include / exclude a catalog id from the store pool.</summary>
        public void SetStoreEnabled(string id, bool enabled)
        {
            var set = storeDisabledSet();
            bool changed = enabled ? set.Remove(id) : set.Add(id);
            if (!changed)
                return;

            config.SetValue(OsuSetting.CosmeticStoreDisabled, string.Join(",", set));
            StoreCurationChanged?.Invoke();
        }

        /// <summary>Admin: include / exclude many ids at once, firing the change
        /// event a single time (used by the "show / hide all" bulk actions).</summary>
        public void SetStoreEnabledBatch(IEnumerable<string> ids, bool enabled)
        {
            var set = storeDisabledSet();
            bool changed = false;
            foreach (string id in ids)
                changed |= enabled ? set.Remove(id) : set.Add(id);

            if (!changed)
                return;

            config.SetValue(OsuSetting.CosmeticStoreDisabled, string.Join(",", set));
            StoreCurationChanged?.Invoke();
        }

        private HashSet<string> storeDisabledSet()
        {
            var set = new HashSet<string>();
            foreach (string s in config.Get<string>(OsuSetting.CosmeticStoreDisabled).Split(',', StringSplitOptions.RemoveEmptyEntries))
                set.Add(s);
            return set;
        }

        /// <summary>The catalog ids currently pulled from the store pool.</summary>
        public IReadOnlyCollection<string> StoreDisabledIds => storeDisabledSet();

        /// <summary>Overwrite the local disabled-set cache with the server's
        /// authoritative list. Does NOT fire <see cref="StoreCurationChanged"/>
        /// (so it never echoes the value straight back to the server) — callers
        /// refresh their UI directly after applying.</summary>
        public void ApplyServerDisabled(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids ?? Enumerable.Empty<string>());
            config.SetValue(OsuSetting.CosmeticStoreDisabled, string.Join(",", set));
        }

        // ── Equip ───────────────────────────────────────────────────────────

        public void Equip(string id) => EquippedTrailId.Value = id ?? string.Empty;

        public void Unequip() => EquippedTrailId.Value = string.Empty;

        // ── Name colours (ownership shared with Buy/IsOwned above) ───────────

        public void EquipNameColour(string id) => EquippedNameColourId.Value = id ?? string.Empty;

        public void UnequipNameColour() => EquippedNameColourId.Value = string.Empty;

        // ── Per-trail customisation (length / density multipliers) ──────────

        public (float length, float density, float size) GetCustomisation(string id)
        {
            foreach (string entry in config.Get<string>(OsuSetting.CursorTrailCustomisations).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split(':');
                if (parts.Length >= 3 && parts[0] == id
                    && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float len)
                    && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float dens))
                {
                    // Size is the newer 4th field; default to 1 for old entries.
                    float size = 1f;
                    if (parts.Length < 4 || !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out size) || size <= 0)
                        size = 1f;
                    return (len, dens, size);
                }
            }

            return (1f, 1f, 1f);
        }

        public void SetCustomisation(string id, float length, float density, float size)
        {
            var map = new Dictionary<string, (float l, float d, float s)>();
            foreach (string entry in config.Get<string>(OsuSetting.CursorTrailCustomisations).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = entry.Split(':');
                if (parts.Length >= 3
                    && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float l)
                    && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float d))
                {
                    float s = 1f;
                    if (parts.Length >= 4)
                        float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out s);
                    map[parts[0]] = (l, d, s <= 0 ? 1f : s);
                }
            }

            map[id] = (length, density, size);
            config.SetValue(OsuSetting.CursorTrailCustomisations,
                string.Join(";", map.Select(kv => $"{kv.Key}:{kv.Value.l.ToString(CultureInfo.InvariantCulture)}:{kv.Value.d.ToString(CultureInfo.InvariantCulture)}:{kv.Value.s.ToString(CultureInfo.InvariantCulture)}")));

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

            var (length, density, size) = GetCustomisation(id);
            t.SetLengthScale(length);
            t.SetDensityMultiplier(density);
            t.SetSizeMultiplier(size);
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
                var (length, density, size) = GetCustomisation(id);
                trail.SetLengthScale(length);
                trail.SetDensityMultiplier(density);
                trail.SetSizeMultiplier(size);
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
