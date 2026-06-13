// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>
    /// Resolves a catalog id (trail / name-colour / aura) to its kind, display
    /// name and a preview drawable. Shared by the unlock celebration popup and
    /// the admin code-grant picker, so "an id" always means the same thing.
    /// </summary>
    public static class CosmeticUnlock
    {
        public enum Kind
        {
            Unknown,
            Trail,
            NameColour,
            Aura,
        }

        public static Kind ResolveKind(string id, APIUser user)
        {
            if (CosmeticCatalog.Trails.Any(t => t.Id == id))
                return Kind.Trail;
            if (AuraRegistry.GetById(id) != null)
                return Kind.Aura;
            if (CosmeticNameColourCatalog.GetById(id, user) != null)
                return Kind.NameColour;
            return Kind.Unknown;
        }

        public static string DisplayName(string id, APIUser user)
        {
            var trail = CosmeticCatalog.Trails.FirstOrDefault(t => t.Id == id);
            if (trail != null)
                return trail.Name;
            if (AuraRegistry.GetById(id) != null)
                return AuraCard.DisplayNameFor(id);
            var nc = CosmeticNameColourCatalog.GetById(id, user);
            if (nc != null)
                return nc.Name;
            return id;
        }

        /// <summary>A short tag for the kind, e.g. "Cursor trail" / "Aura".</summary>
        public static string KindLabel(string id, APIUser user) => ResolveKind(id, user) switch
        {
            Kind.Trail => "Cursor trail",
            Kind.NameColour => "Name colour",
            Kind.Aura => "Aura",
            _ => "Cosmetic",
        };

        /// <summary>A centred preview drawable for the cosmetic, or a plain label
        /// when the id is unknown to this client.</summary>
        public static Drawable CreatePreview(string id, APIUser user)
        {
            var trail = CosmeticCatalog.Trails.FirstOrDefault(t => t.Id == id);
            if (trail != null)
            {
                return new CosmeticTrailPreview(trail)
                {
                    RelativeSizeAxes = Axes.Both,
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };
            }

            if (AuraRegistry.GetById(id) != null)
            {
                var sample = new APIUser
                {
                    Id = -1,
                    Username = string.IsNullOrEmpty(user?.Username) ? "You" : user.Username,
                    EquippedAura = id,
                };
                return new osu.Framework.Graphics.Containers.Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Child = UserAuraContainer.Wrap(sample, new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = sample.Username,
                        Font = OsuFont.GetFont(size: 28, weight: FontWeight.SemiBold),
                    }),
                };
            }

            var colour = CosmeticNameColourCatalog.GetById(id, user);
            if (colour != null)
            {
                return new NameColourText(colour, 30f)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };
            }

            return new OsuSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = id,
                Font = OsuFont.GetFont(size: 22, weight: FontWeight.SemiBold),
            };
        }

        /// <summary>Every catalog id an admin can attach to a code (trails, name
        /// colours, auras), each with a friendly label, for the grant picker.</summary>
        public static IEnumerable<(string id, string label)> AllGrantable(APIUser user)
        {
            foreach (var t in CosmeticCatalog.Trails)
                yield return (t.Id, $"Trail · {t.Name}");

            foreach (var c in CosmeticNameColourCatalog.Buyable)
                yield return (c.Id, $"Colour · {c.Name}");

            foreach (var a in AuraRegistry.AllPresets)
                yield return (a.AuraId, $"Aura · {AuraCard.DisplayNameFor(a.AuraId)}");
        }
    }
}
