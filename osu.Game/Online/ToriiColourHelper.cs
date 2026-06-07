// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Game.Graphics.UserEffects;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Users;

namespace osu.Game.Online
{
    /// <summary>
    /// Resolves the colour a username should be painted: the local user's
    /// equipped name-colour cosmetic if they have one, otherwise their
    /// highest-priority Torii title-group colour. Surfaces that colour usernames
    /// (the song-select + gameplay leaderboards) call this, so routing the
    /// equipped colour through here makes the local player's choice show up there
    /// too, even though those surfaces only hold a stripped per-row user object.
    /// </summary>
    public static class ToriiColourHelper
    {
        private static readonly string[] torii_title_priority =
        {
            "torii-admin", "torii-dev", "torii-mod", "torii-qat", "torii-pooler",
            "torii-tournament", "torii-advisor", "torii-alumni", "torii-supporter",
            "torii-goof",
        };

        /// <summary>
        /// Wired at startup: the local user's EQUIPPED name colour (flat), or null
        /// when they have none equipped. Kept as a provider so this static helper
        /// can read live config without a hard dependency on the cosmetics manager.
        /// </summary>
        public static Func<Colour4?>? LocalEquippedNameColourProvider;

        /// <summary>The colour for a username: the local user's equipped name
        /// colour if set, else their highest-priority Torii title-group colour,
        /// else null.</summary>
        public static Colour4? GetTopColour(IUser? user)
        {
            if (user == null)
                return null;

            // Local user: their explicit equipped colour wins. Fall back to the
            // role colour resolved from their FULL data, because the per-row user
            // object handed to us here is usually stripped of groups.
            var local = UserAuraContainer.LocalUserProvider?.Invoke();

            if (local != null && user.OnlineID == local.OnlineID)
            {
                var equipped = LocalEquippedNameColourProvider?.Invoke();
                if (equipped != null)
                    return equipped;

                return topGroupColour(local);
            }

            return topGroupColour(user as APIUser);
        }

        /// <summary>Overload for call sites that already hold an <see cref="APIUser"/>.</summary>
        public static Colour4? GetTopColour(APIUser? user) => GetTopColour((IUser?)user);

        private static Colour4? topGroupColour(APIUser? user)
        {
            if (user?.Groups is not { Length: > 0 } groups)
                return null;

            foreach (string id in torii_title_priority)
            {
                var match = groups.FirstOrDefault(g => g.Identifier == id);
                if (match?.Colour != null)
                    return Color4Extensions.FromHex(match.Colour);
            }

            return null;
        }
    }
}
