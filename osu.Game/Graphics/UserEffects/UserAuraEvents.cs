// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Graphics.UserEffects
{
    /// <summary>
    /// Tiny pub/sub channel for "user X just changed which aura is rendered
    /// around their name". Picker → container, decoupled.
    /// </summary>
    /// <remarks>
    /// Why this exists: every <see cref="UserAuraContainer"/> resolves its
    /// preset ONCE at construction (or on <see cref="UserAuraContainer.SetUser"/>),
    /// reading <c>APIUser.EquippedAura</c>. When the local user picks a new
    /// aura via <c>ToriiAuraSettings</c>, the server confirms and the catalog
    /// is refreshed — but every existing container scattered around the UI
    /// (profile header, dashboard online list, chat lines, leaderboard rows,
    /// user panels…) is still rendering the old preset against the snapshot
    /// it captured at load time.
    /// <para/>
    /// The natural osu-framework signal would be <c>api.LocalUser.ValueChanged</c>,
    /// but that only fires when the BINDABLE'S REFERENCE changes (login /
    /// logout / account switch) — mutating a single field on the in-place
    /// <see cref="osu.Game.Online.API.Requests.Responses.APIUser"/> instance
    /// does not. Cloning a fresh <c>APIUser</c> just to fire the bindable
    /// would be a heavy throwaway allocation and could thrash any unrelated
    /// listeners that re-render on identity changes.
    /// <para/>
    /// Static event keeps the contract trivial: one writer (the picker), N
    /// readers (every <see cref="UserAuraContainer"/>). Containers subscribe
    /// once during BDL and unsubscribe on dispose so subscribers can't leak
    /// past their visual lifetime.
    /// </remarks>
    public static class UserAuraEvents
    {
        /// <summary>
        /// Fired after the server has accepted a new equipped-aura value for
        /// <paramref name="userId"/>. <paramref name="newEffectiveAuraId"/>
        /// is the RESOLVED aura id (what the server says should actually
        /// render, sentinels already collapsed) — listeners can hand it
        /// straight to <see cref="AuraRegistry.GetById"/>.
        /// </summary>
        public static event Action<int /* userId */, string? /* newEffectiveAuraId */>? UserAuraChanged;

        public static void NotifyUserAuraChanged(int userId, string? newEffectiveAuraId)
            => UserAuraChanged?.Invoke(userId, newEffectiveAuraId);
    }
}
