// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Sprites;
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
    }

    /// <summary>
    /// A username colour cosmetic. Static styles (solid / gradient) are bought
    /// with points; the animated / flashy ones (<see cref="Earned"/>) are
    /// prestige: granted manually for doing something, not for-sale.
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

        /// <summary>Prestige colour: not purchasable, granted manually (server
        /// side later). Shown with an EARNED tag instead of a price.</summary>
        public bool Earned { get; }

        public CosmeticNameColour(string id, string name, CosmeticTier tier, int price, NameColourStyle style, Color4 primary, Color4 secondary = default, bool earned = false)
        {
            Id = id;
            Name = name;
            Tier = tier;
            Price = price;
            Style = style;
            Primary = primary;
            Secondary = secondary == default ? primary : secondary;
            Earned = earned;
        }

        /// <summary>Paint a username text in this colour. Call every frame for the
        /// animated (Rainbow) style; for the static styles it's idempotent.</summary>
        public void Apply(SpriteText text, double timeMs)
        {
            switch (Style)
            {
                case NameColourStyle.Solid:
                    text.Colour = Primary;
                    break;

                case NameColourStyle.Gradient:
                    text.Colour = ColourInfo.GradientHorizontal(Primary, Secondary);
                    break;

                case NameColourStyle.Rainbow:
                    // A two-hue gradient whose hue offset drifts over time, so the
                    // name shimmers through the spectrum left-to-right.
                    float hue = (float)((timeMs / 3500.0) % 1.0);
                    var a = (Color4)Colour4.FromHSV(hue, 0.85f, 1f);
                    var b = (Color4)Colour4.FromHSV((hue + 0.25f) % 1f, 0.85f, 1f);
                    text.Colour = ColourInfo.GradientHorizontal(a, b);
                    break;

                case NameColourStyle.Pulse:
                    // Breathe between the two colours.
                    float u = (float)((Math.Sin(timeMs / 700.0) + 1.0) / 2.0);
                    text.Colour = new Color4(
                        Primary.R + (Secondary.R - Primary.R) * u,
                        Primary.G + (Secondary.G - Primary.G) * u,
                        Primary.B + (Secondary.B - Primary.B) * u,
                        1f);
                    break;
            }
        }

        /// <summary>Reset a username text to the plain default colour.</summary>
        public static void Clear(SpriteText text) => text.Colour = Color4.White;
    }
}
