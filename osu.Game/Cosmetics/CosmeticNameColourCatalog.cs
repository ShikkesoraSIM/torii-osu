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
    /// Catalog + resolver for username-colour cosmetics. Two distinct kinds:
    ///
    ///   - <see cref="Buyable"/> — static solids / gradients, bought with points
    ///     (earned by playing). These are the ONLY name colours sold in the store.
    ///
    ///   - ROLE colours — derived at runtime from the user's API groups
    ///     (admin red, supporter pink, ...). NOT sold anywhere: you have them
    ///     because of your role, they only appear in your Inventory, and they
    ///     render with the special <see cref="NameColourStyle.Halo"/> (a soft
    ///     glow that hugs the letters in the role colour, like the profile)
    ///     so a role colour always reads as special, never a flat solid.
    ///     Same entitlement model as auras (group identifier match).
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

        private const string group_prefix = "name-group-";

        /// <summary>
        /// Priority order for deriving a user's ROLE (title-group) colour when they
        /// have no bought colour equipped: the first of these groups they belong to
        /// with a colour wins. Single source of truth — <see cref="Online.ToriiColourHelper"/>
        /// resolves the flat leaderboard colour from this same order, so the flat
        /// colour and the rich halo never diverge.
        /// </summary>
        public static readonly IReadOnlyList<string> TitlePriority = new[]
        {
            "torii-admin", "torii-dev", "torii-mod", "torii-qat", "torii-pooler",
            "torii-tournament", "torii-advisor", "torii-alumni", "torii-supporter",
            "torii-goof",
        };

        /// <summary>The halo role colour matching one of the user's groups (e.g.
        /// the admin red). Earned, never sold, swappable like any cosmetic.</summary>
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
                CosmeticTier.Premium, 0, NameColourStyle.Halo, colour, owningGroups: new[] { group.Identifier });
        }

        /// <summary>Every EARNED (role) colour the user is entitled to, one per
        /// group of theirs that carries a colour. Inventory-only.</summary>
        public static IEnumerable<CosmeticNameColour> GetEntitledEarned(APIUser user)
        {
            if (user?.Groups == null || user.Groups.Length == 0)
                yield break;

            foreach (var group in user.Groups)
            {
                var c = GroupColourFor(group);
                if (c != null)
                    yield return c;
            }
        }

        /// <summary>The user's highest-priority ROLE colour (as a <see cref="NameColourStyle.Halo"/>
        /// cosmetic), derived from their groups by <see cref="TitlePriority"/> order — admin
        /// beats dev beats mod, etc. Null if they belong to no prioritised group with a colour.
        /// Used as the fallback when no bought colour is equipped, so an admin's name glows red
        /// wherever it appears without them having to equip anything.</summary>
        public static CosmeticNameColour GetTopEarned(APIUser? user)
        {
            if (user?.Groups == null || user.Groups.Length == 0)
                return null;

            foreach (string id in TitlePriority)
            {
                var group = user.Groups.FirstOrDefault(g => g.Identifier == id);
                if (group == null)
                    continue;

                var c = GroupColourFor(group);
                if (c != null)
                    return c;
            }

            return null;
        }

        /// <summary>Resolve any colour id (a buyable one, or a role colour
        /// derived from the user's groups).</summary>
        public static CosmeticNameColour GetById(string id, APIUser user)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            var b = Buyable.FirstOrDefault(c => c.Id == id);
            if (b != null)
                return b;

            if (id.StartsWith(group_prefix, StringComparison.Ordinal) && user?.Groups != null)
            {
                string identifier = id.Substring(group_prefix.Length);
                var group = user.Groups.FirstOrDefault(g => g.Identifier == identifier);
                if (group != null)
                    return GroupColourFor(group);
            }

            return null;
        }

        /// <summary>True if this id is one of the buyable (points-bought) name colours,
        /// as opposed to a role colour (<c>name-group-*</c>) or nothing. Only buyable
        /// colours are broadcast server-side; role colours resolve from API groups.</summary>
        public static bool IsBuyable(string id) =>
            !string.IsNullOrEmpty(id) && Buyable.Any(c => c.Id == id);

        private static CosmeticNameColour solid(string id, string name, int price, Color4 colour)
            => new CosmeticNameColour(id, name, CosmeticTier.Basic, price, NameColourStyle.Solid, colour);

        private static CosmeticNameColour gradient(string id, string name, int price, Color4 a, Color4 b)
            => new CosmeticNameColour(id, name, CosmeticTier.Special, price, NameColourStyle.Gradient, a, b);
    }
}
