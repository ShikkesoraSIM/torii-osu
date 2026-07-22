// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics.Sprites;
using osuTK.Graphics;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay
{
    /// <summary>
    /// torii: traduce el rating de matchmaking (el mu de OpenSkill) a un rank con nombre y division,
    /// tipo "Gold 2" / "Silver 5". Es puramente de display, el sistema sigue corriendo sobre el rating
    /// numerico. Cada tier abarca un rango de rating dividido en 5 divisiones (la 5 es el piso del tier,
    /// la 1 el techo, justo antes de promocionar al siguiente).
    /// </summary>
    public readonly struct RankedPlayRankTier
    {
        public readonly string Name;
        public readonly int Division;
        public readonly Color4 Colour;
        public readonly IconUsage Icon;

        /// <summary>Nombre completo listo para mostrar, ej "Gold 2" (Master no lleva division).</summary>
        public string DisplayName => HasDivision ? $"{Name} {Division}" : Name;

        public bool HasDivision => Division > 0;

        /// <summary>
        /// Orden del tier para comparar promociones/demociones: Unranked = -1, Bronze = 0 ...
        /// Master = 5. Un TierOrder mayor = mejor tier. La division NO entra aca (es dentro del
        /// mismo tier); para "subiste de tier" comparar TierOrder, no DisplayName.
        /// </summary>
        public int TierOrder
        {
            get
            {
                for (int i = 0; i < tiers.Length; i++)
                {
                    if (tiers[i].name == Name)
                        return i;
                }

                return -1;
            }
        }

        public RankedPlayRankTier(string name, int division, Color4 colour, IconUsage icon)
        {
            Name = name;
            Division = division;
            Colour = colour;
            Icon = icon;
        }

        private const int divisions_per_tier = 5;

        // (nombre, rating base del tier, color, icono). Master es open-ended (sin techo/divisiones).
        // el ancho de cada tier no-master es `tier_span`.
        private const int tier_span = 180;

        private static readonly (string name, int baseRating, Color4 colour, IconUsage icon)[] tiers =
        {
            ("Bronze", 700, new Color4(0.72f, 0.45f, 0.20f, 1f), FontAwesome.Solid.ShieldAlt),
            ("Silver", 880, new Color4(0.75f, 0.78f, 0.82f, 1f), FontAwesome.Solid.ShieldAlt),
            ("Gold", 1060, new Color4(0.98f, 0.80f, 0.20f, 1f), FontAwesome.Solid.Award),
            ("Platinum", 1240, new Color4(0.30f, 0.85f, 0.78f, 1f), FontAwesome.Solid.Medal),
            ("Diamond", 1420, new Color4(0.45f, 0.72f, 1f, 1f), FontAwesome.Solid.Gem),
            ("Master", 1600, new Color4(1f, 0.35f, 0.72f, 1f), FontAwesome.Solid.Crown),
        };

        /// <summary>
        /// Rank para un rating dado. null = "Unranked" (todavia sin partidas / sin dato).
        /// </summary>
        public static RankedPlayRankTier FromRating(int? rating)
        {
            if (rating == null)
                return new RankedPlayRankTier("Unranked", 0, new Color4(0.55f, 0.55f, 0.58f, 1f), FontAwesome.Solid.QuestionCircle);

            int value = rating.Value;

            // Master: open-ended, sin divisiones.
            var master = tiers[^1];
            if (value >= master.baseRating)
                return new RankedPlayRankTier(master.name, 0, master.colour, master.icon);

            // por debajo del piso de Bronze: Bronze 5.
            if (value < tiers[0].baseRating)
                return new RankedPlayRankTier(tiers[0].name, divisions_per_tier, tiers[0].colour, tiers[0].icon);

            for (int i = 0; i < tiers.Length - 1; i++)
            {
                var t = tiers[i];
                if (value < t.baseRating + tier_span)
                {
                    int offset = value - t.baseRating;
                    // 0..(span-1) -> division 5..1 (mas rating = division mas baja = mejor).
                    int division = divisions_per_tier - Math.Clamp(offset * divisions_per_tier / tier_span, 0, divisions_per_tier - 1);
                    return new RankedPlayRankTier(t.name, division, t.colour, t.icon);
                }
            }

            // fallback defensivo (no deberia caer aca): Diamond 1.
            var d = tiers[^2];
            return new RankedPlayRankTier(d.name, 1, d.colour, d.icon);
        }

        /// <summary>
        /// Progreso (0..1) dentro del tier actual hacia el siguiente, para la barra del hero card.
        /// Master es open-ended -> satura en 1. Debajo de Bronze -> 0.
        /// </summary>
        public static float ProgressWithinTier(int? rating)
        {
            if (rating == null)
                return 0f;

            int value = rating.Value;

            var master = tiers[^1];
            if (value >= master.baseRating)
                return 1f;

            if (value < tiers[0].baseRating)
                return 0f;

            for (int i = 0; i < tiers.Length - 1; i++)
            {
                var t = tiers[i];
                if (value < t.baseRating + tier_span)
                    return Math.Clamp((value - t.baseRating) / (float)tier_span, 0f, 1f);
            }

            return 1f;
        }

        /// <summary>
        /// El siguiente tier (para el label derecho de la barra de progreso). Master/Unranked se
        /// devuelven a si mismos (no hay siguiente). La division devuelta es el piso del tier.
        /// </summary>
        public RankedPlayRankTier Next()
        {
            int order = TierOrder;

            if (order < 0 || order >= tiers.Length - 1)
                return this;

            var n = tiers[order + 1];
            bool nextIsMaster = order + 1 == tiers.Length - 1;
            return new RankedPlayRankTier(n.name, nextIsMaster ? 0 : divisions_per_tier, n.colour, n.icon);
        }
    }
}
