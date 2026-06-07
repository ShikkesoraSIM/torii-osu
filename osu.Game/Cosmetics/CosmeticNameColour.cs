// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

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
    }

    /// <summary>
    /// A purchasable username colour cosmetic: solid, gradient, or an animated
    /// rainbow. Bought with points (earned by playing) like cursor trails.
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

        public CosmeticNameColour(string id, string name, CosmeticTier tier, int price, NameColourStyle style, Color4 primary, Color4 secondary = default)
        {
            Id = id;
            Name = name;
            Tier = tier;
            Price = price;
            Style = style;
            Primary = primary;
            Secondary = secondary == default ? primary : secondary;
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
            }
        }

        /// <summary>Reset a username text to the plain default colour.</summary>
        public static void Clear(SpriteText text) => text.Colour = Color4.White;
    }
}
