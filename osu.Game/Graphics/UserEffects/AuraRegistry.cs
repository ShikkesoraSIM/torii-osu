// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Game.Graphics.UserEffects.Presets;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Graphics.UserEffects
{
    /// <summary>
    /// Central registry of every <see cref="AuraPreset"/> the client knows
    /// how to render. Responsible for:
    ///
    ///  1. Holding the canonical list of presets — adding a new aura is
    ///     literally one new <c>new XxxAuraPreset()</c> in the array below.
    ///  2. Mapping a server-supplied <c>APIUser.EquippedAura</c> string to
    ///     the matching <see cref="AuraPreset"/> instance.
    ///  3. Falling back to a "default" aura derived from the user's groups
    ///     when no explicit pick has been made yet (so users who joined
    ///     before the equip UI shipped still see their elite-group aura
    ///     out of the box).
    ///  4. Listing the auras a user is entitled to, used by the settings
    ///     picker for offline-friendly behaviour when the catalog endpoint
    ///     hasn't responded yet.
    ///
    /// All preset metadata (display name, description) for the picker UI
    /// comes from the SERVER catalog at <c>GET /api/v2/me/aura-catalog</c>.
    /// Presets here only describe behaviour + ownership. This keeps the
    /// server as the single source of truth for what auras "are".
    /// </summary>
    public static class AuraRegistry
    {
        // Canonical preset list. Adding a new aura: implement a new
        // AuraPreset subclass under Presets/ and add one new instance here.
        // Order matters only for ties in default-priority resolution.
        private static readonly IReadOnlyList<AuraPreset> all_presets = new AuraPreset[]
        {
            new AdminAuraPreset(),
            new DevAuraPreset(),
            new ModAuraPreset(),
            new QatAuraPreset(),
            // Single supporter aura (pink hearts) — granted only while the
            // user is currently in the active supporter window. Past donors
            // get the permanent "torii-donator" badge instead, which has
            // no aura attached.
            new SupporterAuraPreset(),
            new GoofAuraPreset(),
        };

        // Indexed by AuraId for O(1) lookup. Built once at static init.
        private static readonly Dictionary<string, AuraPreset> presets_by_id =
            all_presets.ToDictionary(p => p.AuraId);

        /// <summary>Every preset registered. Useful for tests / debug overlays.</summary>
        public static IReadOnlyList<AuraPreset> AllPresets => all_presets;

        /// <summary>Look up a preset by its stable id, or null if unknown
        /// (e.g. server added a new aura the client doesn't know about yet).</summary>
        public static AuraPreset? GetById(string? auraId)
        {
            if (auraId == null) return null;
            return presets_by_id.TryGetValue(auraId, out var p) ? p : null;
        }

        /// <summary>
        /// Resolve which aura preset (if any) should render around
        /// <paramref name="user"/>'s name.
        ///
        /// Priority order:
        ///   1. Explicit server-resolved <c>EquippedAura</c> — the server
        ///      has already validated ownership and applied sentinel logic,
        ///      so we trust it. Returns null if the value is unknown to
        ///      this client (forward-compat with newer auras).
        ///   2. Group-based fallback — pick the highest-priority preset
        ///      whose owning groups intersect the user's groups. Used when
        ///      the user has no explicit pick (server returns null), so
        ///      every elite user gets a default aura without configuring.
        ///   3. None.
        /// </summary>
        public static AuraPreset? ResolveForUser(APIUser? user)
        {
            if (user == null)
                return null;

            // Path 1: server already resolved this user's equipped aura.
            if (!string.IsNullOrEmpty(user.EquippedAura))
                return GetById(user.EquippedAura);

            // Path 2: no explicit pick — fall back to the user's groups.
            // Only relevant during the rollout window where some clients
            // fetched a user payload before this field shipped, or for
            // users never seen by an aware client. Otherwise the server
            // resolves this server-side and Path 1 always hits.
            return resolveDefaultForGroups(user);
        }

        /// <summary>
        /// All auras the given user is entitled to equip, ordered by
        /// <see cref="AuraPreset.DefaultPriority"/> ascending. Used by the
        /// settings picker as a quick local view; the authoritative source
        /// for the picker is still the server catalog endpoint.
        /// </summary>
        public static IEnumerable<AuraPreset> GetEntitledAuras(APIUser? user)
        {
            if (user?.Groups == null || user.Groups.Length == 0)
                yield break;

            var groupIds = new HashSet<string>(user.Groups
                .Where(g => g.Identifier != null)
                .Select(g => g.Identifier!));

            foreach (var preset in all_presets.OrderBy(p => p.DefaultPriority))
            {
                if (preset.OwningGroupIdentifiers.Any(id => groupIds.Contains(id)))
                    yield return preset;
            }
        }

        // Returns the highest-priority preset whose owning groups overlap
        // the user's groups, or null when nothing matches.
        private static AuraPreset? resolveDefaultForGroups(APIUser user)
        {
            if (user.Groups == null || user.Groups.Length == 0)
                return null;

            var groupIds = new HashSet<string>(user.Groups
                .Where(g => g.Identifier != null)
                .Select(g => g.Identifier!));

            AuraPreset? best = null;
            foreach (var preset in all_presets)
            {
                if (!preset.OwningGroupIdentifiers.Any(id => groupIds.Contains(id)))
                    continue;
                if (best == null || preset.DefaultPriority < best.DefaultPriority)
                    best = preset;
            }
            return best;
        }
    }
}
