// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Users;

namespace osu.Game.Online
{
    /// <summary>
    /// Helpers for resolving the highest-priority Torii title colour for a given user.
    /// </summary>
    public static class ToriiColourHelper
    {
        private static readonly string[] torii_title_priority =
        {
            "torii-admin", "torii-dev", "torii-mod", "torii-qat", "torii-pooler",
            "torii-tournament", "torii-advisor", "torii-alumni", "torii-supporter",
            "torii-goof",
        };

        /// <summary>Returns the colour of the user's highest-priority Torii title group, or null if they have none.</summary>
        public static Colour4? GetTopColour(APIUser? user)
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

        /// <summary>Same as <see cref="GetTopColour(APIUser?)"/> but accepts the base <see cref="IUser"/> interface (casts internally).</summary>
        public static Colour4? GetTopColour(IUser? user) => GetTopColour(user as APIUser);
    }
}
