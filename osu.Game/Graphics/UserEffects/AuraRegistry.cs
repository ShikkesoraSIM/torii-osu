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
            // Per-ruleset Consul auras. All four match the same client-side
            // identifier "torii-advisor" and rely on RequiredPlaymodes to
            // discriminate which one renders for an osu! / taiko / catch /
            // mania advisor. Sit before Supporter / Goof in priority so a
            // mode-advisor's default visual is the specialised Consul aura
            // (instead of a generic supporter pink) when they own both.
            new OsuConsulAuraPreset(),
            new TaikoConsulAuraPreset(),
            new CatchConsulAuraPreset(),
            new ManiaConsulAuraPreset(),
            // Feature Architect (May 2026 Cohort) — manually granted to
            // community members whose feature requests landed between
            // 4 May and 4 June 2026. Cohort-stamped in the AuraId so a
            // future second cohort can ship as a separate preset.
            new FeatureArchitectAuraPreset(),
            // Single supporter aura (pink hearts) — granted only while the
            // user is currently in the active supporter window. Past donors
            // get the permanent "torii-donator" badge instead, which has
            // no aura attached.
            new SupporterAuraPreset(),
            // Founder (user.id <= 100) — torii-themed permanent aura for
            // the early-adopter cohort. Vermillion embers + occasional
            // torii-gate silhouette. Default priority sits between the
            // elite-tier auras and the recognition tier, so a Founder who
            // is also an admin still defaults to admin, but a Founder
            // who is just a regular player gets the Founder aura.
            new FounderAuraPreset(),
            new GoofAuraPreset(),
            // Bug-finder aura (mint bugs crawling along the baseline) —
            // recognition tier for community members who reported real
            // bugs. Highest DefaultPriority among the group fallbacks so
            // it never wins the tiebreak against any "elite" group the
            // same user happens to own (admin/dev/mod/qat/supporter/goof).
            new BugFinderAuraPreset(),
            // Summer 2026 seasonal aura — earned via the summer event group,
            // explicit-equip only (high DefaultPriority so it never auto-wins
            // the group fallback). Earned, never bought, like every aura.
            new SummerAuraPreset(),
            // Stardust — the ONLY points-purchasable aura. No owning group, so
            // it never appears in the entitled list or group fallback; it only
            // renders when a user explicitly equips it after buying. Deliberately
            // subtle so a bought aura never outshines an earned one.
            new StardustAuraPreset(),
            // Founder aura design VARIANTS — registered so the visual
            // test scene (TestSceneFounderVariants + AllSurfaces
            // personas in TestSceneAurasInRealUI) can resolve them by
            // AuraId via the normal UserAuraContainer pipeline.
            // DefaultPriority is intentionally 200 on every variant
            // so they never win the group-fallback against the real
            // FounderAuraPreset (priority 70) — real Founder users
            // without an explicit equipped pick get the baseline; the
            // variants only render when an APIUser explicitly equips
            // them by AuraId. After we ship the chosen variant as the
            // canonical FounderAuraPreset, the remaining variant
            // classes + these entries can be deleted.
            new FounderImperialGoldPreset(),
            new FounderSakuraGardenPreset(),
            new FounderLacqueredBoxPreset(),
            new FounderSunrisePillarPreset(),
            new FounderCrestOfHonorPreset(),
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

            foreach (var preset in all_presets.OrderBy(p => p.DefaultPriority))
            {
                if (isPresetEligibleForUser(preset, user))
                    yield return preset;
            }
        }

        // Returns the highest-priority preset whose owning groups overlap
        // the user's groups, or null when nothing matches.
        private static AuraPreset? resolveDefaultForGroups(APIUser user)
        {
            if (user.Groups == null || user.Groups.Length == 0)
                return null;

            AuraPreset? best = null;
            foreach (var preset in all_presets)
            {
                if (!isPresetEligibleForUser(preset, user))
                    continue;
                if (best == null || preset.DefaultPriority < best.DefaultPriority)
                    best = preset;
            }
            return best;
        }

        // Centralises the "is this user allowed to wear this preset?" check
        // so both the picker (GetEntitledAuras) and the fallback resolver
        // (resolveDefaultForGroups) agree. Honours both the identifier
        // intersect AND the optional per-preset RequiredPlaymodes filter.
        //
        // When RequiredPlaymodes is set, we require at least one of the
        // user's matching groups to ALSO carry one of the required
        // playmodes — used by the per-mode Consul auras to distinguish
        // an osu! Advisor from a Taiko Advisor (both groups share the
        // identifier "torii-advisor"; they differ only in Playmodes).
        private static bool isPresetEligibleForUser(AuraPreset preset, APIUser user)
        {
            if (user.Groups == null || user.Groups.Length == 0)
                return false;

            foreach (var group in user.Groups)
            {
                if (group.Identifier == null)
                    continue;
                if (!preset.OwningGroupIdentifiers.Contains(group.Identifier))
                    continue;

                // No playmode constraint — identifier match alone is enough.
                if (preset.RequiredPlaymodes == null || preset.RequiredPlaymodes.Count == 0)
                    return true;

                // Playmode constraint set — the matching group must carry
                // at least one of the required playmodes in its payload.
                // Groups without a Playmodes array can never satisfy a
                // playmode-filtered preset (no information to match on).
                if (group.Playmodes == null || group.Playmodes.Length == 0)
                    continue;

                foreach (var pm in group.Playmodes)
                {
                    if (preset.RequiredPlaymodes.Contains(pm))
                        return true;
                }
            }
            return false;
        }
    }
}
