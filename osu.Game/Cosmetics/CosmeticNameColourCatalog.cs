// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Extensions.Color4Extensions;
using osu.Game.Online.API.Requests.Responses;
using osuTK.Graphics;

namespace osu.Game.Cosmetics
{
    /// <summary>
    /// Catalog + resolver for username-colour cosmetics. Two sources, ONE system
    /// (so everything is documented and swappable):
    ///
    ///   - <see cref="Buyable"/> — static solids / gradients, bought with points.
    ///   - EARNED — granted by an API group, never for sale:
    ///       * Role colours: derived at runtime from the user's groups
    ///         (admin red, supporter pink, ...). This is the SAME source the
    ///         game already uses to tint your name; here it just becomes a
    ///         selectable, swappable cosmetic.
    ///       * <see cref="Special"/> — flashy animated ones gated behind a
    ///         specific group (placeholder mapping; server-configurable later),
    ///         mirroring how auras are gated in <c>AuraRegistry</c>.
    /// </summary>
    public static class CosmeticNameColourCatalog
    {
        public static readonly IReadOnlyList<CosmeticNameColour> Buyable = new[]
        {
            solid("name-crimson", "Crimson", 200, new Color4(235, 70, 80, 255)),
            solid("name-ocean", "Ocean", 200, new Color4(70, 160, 245, 255)),
            solid("name-mint", "Mint", 200, new Color4(95, 225, 170, 255)),
            solid("name-gold", "Gold", 200, new Color4(245, 200, 75, 255)),
            solid("name-violet", "Amethyst", 200, new Color4(180, 120, 240, 255)),
            solid("name-coral", "Coral", 200, new Color4(255, 130, 110, 255)),

            gradient("name-sunset", "Sunset", 800, new Color4(255, 170, 60, 255), new Color4(235, 60, 150, 255)),
            gradient("name-tide", "Tide", 800, new Color4(70, 200, 255, 255), new Color4(110, 100, 245, 255)),
            gradient("name-forest", "Forest", 800, new Color4(150, 230, 90, 255), new Color4(40, 175, 150, 255)),
            gradient("name-berry", "Berry", 800, new Color4(255, 110, 200, 255), new Color4(150, 90, 240, 255)),
        };

        /// <summary>Flashy animated colours, each gated behind a group (earned,
        /// never bought). The group mapping is a placeholder for now.</summary>
        public static readonly IReadOnlyList<CosmeticNameColour> Special = new[]
        {
            rainbow("name-rainbow", "Rainbow", "torii-admin", "torii-dev"),
            pulse("name-inferno", "Inferno", new Color4(255, 90, 40, 255), new Color4(255, 210, 80, 255), "torii-supporter"),
            pulse("name-glacier", "Glacier", new Color4(120, 220, 255, 255), new Color4(245, 250, 255, 255), "torii-qat", "torii-mod"),
            pulse("name-nebula", "Nebula", new Color4(170, 90, 240, 255), new Color4(255, 90, 200, 255), "torii-founder"),
        };

        private const string group_prefix = "name-group-";

        /// <summary>A solid name colour matching one of the user's groups (e.g.
        /// the admin red), so a role colour is a swappable cosmetic too.</summary>
        public static CosmeticNameColour GroupColourFor(APIUserGroup group)
        {
            if (group?.Identifier == null || string.IsNullOrEmpty(group.Colour))
                return null;

            Color4 colour;
            try
            {
                colour = Color4Extensions.FromHex(group.Colour);
            }
            catch
            {
                return null;
            }

            return new CosmeticNameColour(group_prefix + group.Identifier, group.Name ?? group.ShortName ?? "Role",
                CosmeticTier.Premium, 0, NameColourStyle.Solid, colour, owningGroups: new[] { group.Identifier });
        }

        /// <summary>Every EARNED colour the user is entitled to: their role
        /// colours plus the special ones their groups grant.</summary>
        public static IEnumerable<CosmeticNameColour> GetEntitledEarned(APIUser user)
        {
            if (user?.Groups == null || user.Groups.Length == 0)
                yield break;

            var groupIds = user.Groups.Where(g => g.Identifier != null).Select(g => g.Identifier).ToHashSet();

            foreach (var special in Special)
            {
                if (special.OwningGroups.Any(groupIds.Contains))
                    yield return special;
            }

            foreach (var group in user.Groups)
            {
                var c = GroupColourFor(group);
                if (c != null)
                    yield return c;
            }
        }

        /// <summary>Resolve any colour id (buyable, special, or a role colour
        /// derived from the user's groups).</summary>
        public static CosmeticNameColour GetById(string id, APIUser user)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            var b = Buyable.FirstOrDefault(c => c.Id == id);
            if (b != null)
                return b;

            var s = Special.FirstOrDefault(c => c.Id == id);
            if (s != null)
                return s;

            if (id.StartsWith(group_prefix, StringComparison.Ordinal) && user?.Groups != null)
            {
                string identifier = id.Substring(group_prefix.Length);
                var group = user.Groups.FirstOrDefault(g => g.Identifier == identifier);
                if (group != null)
                    return GroupColourFor(group);
            }

            return null;
        }

        private static CosmeticNameColour solid(string id, string name, int price, Color4 colour)
            => new CosmeticNameColour(id, name, CosmeticTier.Basic, price, NameColourStyle.Solid, colour);

        private static CosmeticNameColour gradient(string id, string name, int price, Color4 a, Color4 b)
            => new CosmeticNameColour(id, name, CosmeticTier.Special, price, NameColourStyle.Gradient, a, b);

        private static CosmeticNameColour rainbow(string id, string name, params string[] groups)
            => new CosmeticNameColour(id, name, CosmeticTier.Premium, 0, NameColourStyle.Rainbow, Color4.White, owningGroups: groups);

        private static CosmeticNameColour pulse(string id, string name, Color4 a, Color4 b, params string[] groups)
            => new CosmeticNameColour(id, name, CosmeticTier.Premium, 0, NameColourStyle.Pulse, a, b, owningGroups: groups);
    }
}
