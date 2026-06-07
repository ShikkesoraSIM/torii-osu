// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics.Sprites;
using osuTK.Graphics;

namespace osu.Game.Cosmetics
{
    public enum NameColourStyle
    {
        /// <summary>One flat colour.</summary>
        Solid,

        /// <summary>Left-to-right two-colour gradient.</summary>
        Gradient,

        /// <summary>Animated: a shifting two-hue rainbow gradient.</summary>
        Rainbow,

        /// <summary>Animated: breathes between two colours.</summary>
        Pulse,

        /// <summary>Role colour: the name in the colour with a soft glow that
        /// hugs the letters in the same colour (like the profile), so it reads
        /// special and never a flat solid. EARNED-only, for role colours.</summary>
        Halo,
    }

    /// <summary>
    /// A username colour cosmetic. There are two kinds:
    ///   - BUYABLE (<see cref="OwningGroups"/> null): static solids / gradients
    ///     bought with points (earned by playing).
    ///   - EARNED (<see cref="OwningGroups"/> set): granted by one of those API
    ///     groups, NOT for sale. This covers both your role colours (admin red,
    ///     supporter pink, ... derived from your actual groups) and special
    ///     animated ones gated behind a group, exactly like the aura system.
    /// </summary>
    public sealed class CosmeticNameColour
    {
        public string Id { get; }
        public string Name { get; }
        public CosmeticTier Tier { get; }
        public int Price { get; }
        public NameColourStyle Style { get; }
        public Color4 Primary { get; }
        public Color4 Secondary { get; }

        /// <summary>API group identifiers that grant this colour (e.g.
        /// "torii-admin"). Null = buyable with points. Mirrors
        /// <c>AuraPreset.OwningGroupIdentifiers</c>.</summary>
        public IReadOnlyList<string> OwningGroups { get; }

        /// <summary>Granted by a group (not purchasable). Shown with an EARNED
        /// tag instead of a price.</summary>
        public bool Earned => OwningGroups != null && OwningGroups.Count > 0;

        public CosmeticNameColour(string id, string name, CosmeticTier tier, int price, NameColourStyle style, Color4 primary, Color4 secondary = default, IReadOnlyList<string> owningGroups = null)
        {
            Id = id;
            Name = name;
            Tier = tier;
            Price = price;
            Style = style;
            Primary = primary;
            Secondary = secondary == default ? primary : secondary;
            OwningGroups = owningGroups;
        }

        /// <summary>Paint a plain username text in this colour. Call every frame for
        /// the animated styles; for the static ones it's idempotent. No glow (a plain
        /// SpriteText can't render one) — role colours that want the profile glow use
        /// the <see cref="GlowingFreeWidthSpriteText"/> overload below.</summary>
        public void Apply(SpriteText text, double timeMs) => text.Colour = resolveTextColour(timeMs);

        /// <summary>Paint a username in this colour WITH the profile-style glow. Role
        /// (Halo) colours get the colour on the text plus a matching additive glow at
        /// 0.6 alpha — the exact treatment TopHeaderContainer uses for the profile
        /// name, so a role colour looks identical everywhere it's shown. Buyable
        /// colours render with no glow (transparent), same as before.</summary>
        public void Apply(GlowingFreeWidthSpriteText text, double timeMs)
        {
            text.TextColour = resolveTextColour(timeMs);
            text.GlowColour = Style == NameColourStyle.Halo
                ? Primary.Opacity(0.6f)
                : new Color4(0, 0, 0, 0);
        }

        /// <summary>The text colour for this cosmetic at <paramref name="timeMs"/>
        /// (gradients + animated styles included). Shared by both Apply overloads.</summary>
        private ColourInfo resolveTextColour(double timeMs)
        {
            switch (Style)
            {
                case NameColourStyle.Gradient:
                    return ColourInfo.GradientHorizontal(Primary, Secondary);

                case NameColourStyle.Rainbow:
                    // A two-hue gradient whose hue offset drifts over time, so the
                    // name shimmers through the spectrum left-to-right.
                    float hue = (float)((timeMs / 3500.0) % 1.0);
                    var a = (Color4)Colour4.FromHSV(hue, 0.85f, 1f);
                    var b = (Color4)Colour4.FromHSV((hue + 0.25f) % 1f, 0.85f, 1f);
                    return ColourInfo.GradientHorizontal(a, b);

                case NameColourStyle.Pulse:
                    // Breathe between the two colours.
                    float u = (float)((Math.Sin(timeMs / 700.0) + 1.0) / 2.0);
                    return new Color4(
                        Primary.R + (Secondary.R - Primary.R) * u,
                        Primary.G + (Secondary.G - Primary.G) * u,
                        Primary.B + (Secondary.B - Primary.B) * u,
                        1f);

                case NameColourStyle.Solid:
                case NameColourStyle.Halo:
                default:
                    return Primary;
            }
        }

        /// <summary>Reset a plain username text to the default colour.</summary>
        public static void Clear(SpriteText text) => text.Colour = Color4.White;

        /// <summary>Reset a glowing username to the default colour with no glow.</summary>
        public static void Clear(GlowingFreeWidthSpriteText text)
        {
            text.TextColour = Color4.White;
            text.GlowColour = new Color4(0, 0, 0, 0);
        }
    }
}
