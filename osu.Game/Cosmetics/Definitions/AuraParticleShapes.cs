// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Cosmetics.Definitions
{
    /// <summary>
    /// torii: formas de partícula para AURAS, whitelisteadas por nombre. A diferencia de las de trail
    /// (<see cref="CosmeticParticles"/>, con color/tamaño fijos), estas se construyen al TAMAÑO y COLOR
    /// que pide el <see cref="ParticleSpec"/> — un aura mezcla varios tipos, cada uno con su paleta.
    ///
    /// Son primitivas seguras (Box/Circle/CircularContainer/SpriteIcon) parametrizadas: la data solo
    /// elige un nombre + números, nunca geometría libre ni código. Cubre el vocabulario procedural de
    /// los 20 presets hardcodeados (sparkle de Stardust, blossom de Founder, hitcircle de Consul, ...).
    /// Los glyphs (corazón, escudo, hoja, bug, ...) van por el campo <c>icon</c> del spec, resueltos con
    /// la whitelist <see cref="ResolveGlyph"/>.
    /// </summary>
    public static class AuraParticleShapes
    {
        /// <summary>nombres de forma procedural disponibles (para el picker del Creator).</summary>
        public static readonly IReadOnlyList<string> Names = new[]
        {
            "circle", "box", "ring", "sparkleCross", "taperLine", "haloCore", "flower", "hitcircle",
        };

        /// <summary>
        /// Construye una forma procedural al tamaño/color/aspecto dados. <paramref name="size"/> es el
        /// lado base en px (ya escalado por ParticleScale); <paramref name="aspect"/> deforma
        /// circle/box/taperLine (ancho,alto) — las compuestas lo ignoran. Cae a circle si el nombre es
        /// desconocido (defensivo, data de comunidad).
        /// </summary>
        public static Drawable Build(string name, float size, Color4 colour, Vector2 aspect)
        {
            if (aspect.X <= 0 || aspect.Y <= 0)
                aspect = Vector2.One;

            Vector2 dims = new Vector2(size * aspect.X, size * aspect.Y);

            switch ((name ?? "circle").ToLowerInvariant())
            {
                case "box":
                case "taperline":
                    return new Box { Origin = Anchor.Centre, Size = dims, Colour = colour };

                case "ring":
                    return ring(size, colour);

                case "sparklecross":
                    return sparkleCross(size, colour);

                case "halocore":
                    return haloCore(size, colour);

                case "flower":
                    return flower(size, colour);

                case "hitcircle":
                    return hitcircle(size, colour);

                default:
                    return new Circle { Origin = Anchor.Centre, Size = dims, Colour = colour };
            }
        }

        public static bool Has(string name) => name != null && ((IList<string>)Names).Contains(name.ToLowerInvariant() switch
        {
            "sparklecross" => "sparkleCross",
            "taperline" => "taperLine",
            "halocore" => "haloCore",
            "hitcircle" => "hitcircle",
            var other => other,
        });

        // ---- formas compuestas ----

        // anillo hueco (outline real via CircularContainer con borde).
        private static Drawable ring(float size, Color4 colour) => new CircularContainer
        {
            Origin = Anchor.Centre,
            Size = new Vector2(size),
            Masking = true,
            BorderThickness = MathF.Max(1.5f, size * 0.16f),
            BorderColour = colour,
            Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
        };

        // chispa de 4 puntas: dos barras cruzadas + core blanco (el sparkle de Stardust).
        private static Drawable sparkleCross(float size, Color4 colour)
        {
            float arm = MathF.Max(1.5f, size * 0.14f);
            return new Container
            {
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new Box { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(arm, size), Colour = colour },
                    new Box { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(size, arm), Colour = colour },
                    new Circle { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(size * 0.42f), Colour = Color4.White },
                },
            };
        }

        // core lleno + halo tenue agrandado detrás (el "fake glow" recurrente sin shader).
        private static Drawable haloCore(float size, Color4 colour) => new Container
        {
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Circle { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(size * 1.7f), Colour = colour, Alpha = 0.2f },
                new Circle { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(size), Colour = colour },
            },
        };

        // flor de 5 pétalos (óvalos rotados) con un centro (blossom de las Founder).
        private static Drawable flower(float size, Color4 colour)
        {
            var container = new Container
            {
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
            };

            const int petals = 5;
            float petalW = size * 0.44f;
            float petalH = size * 0.72f;

            for (int i = 0; i < petals; i++)
            {
                float angle = i * 360f / petals;
                container.Add(new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(petalW, petalH),
                    Colour = colour,
                    Rotation = angle,
                    // empujar el pétalo hacia afuera desde el centro.
                    Position = offsetForAngle(angle, size * 0.24f),
                });
            }

            container.Add(new Circle
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(size * 0.3f),
                Colour = Color4.White,
            });

            return container;
        }

        // hitcircle: core lleno + anillo exterior (el hitcircle fake de OsuConsul).
        private static Drawable hitcircle(float size, Color4 colour) => new Container
        {
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new CircularContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(size),
                    Masking = true,
                    BorderThickness = MathF.Max(1.5f, size * 0.12f),
                    BorderColour = colour,
                    Child = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0, AlwaysPresent = true },
                },
                new Circle { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(size * 0.6f), Colour = colour, Alpha = 0.85f },
            },
        };

        private static Vector2 offsetForAngle(float degrees, float radius)
        {
            float rad = degrees * MathF.PI / 180f;
            return new Vector2(MathF.Sin(rad) * radius, -MathF.Cos(rad) * radius);
        }

        // ---- glyphs FontAwesome whitelisteados (para el campo `icon` del spec) ----
        // el vocabulario de íconos que usan los 20 presets; string estable -> IconUsage.
        private static readonly Dictionary<string, IconUsage> glyphs = new Dictionary<string, IconUsage>(StringComparer.OrdinalIgnoreCase)
        {
            ["heart"] = FontAwesome.Solid.Heart,
            ["shield"] = FontAwesome.Solid.ShieldAlt,
            ["leaf"] = FontAwesome.Solid.Leaf,
            ["bug"] = FontAwesome.Solid.Bug,
            ["star"] = FontAwesome.Solid.Star,
            ["bulb"] = FontAwesome.Solid.Lightbulb,
            ["music"] = FontAwesome.Solid.Music,
            ["apple"] = FontAwesome.Solid.AppleAlt,
            ["lemon"] = FontAwesome.Solid.Lemon,
            ["drum"] = FontAwesome.Solid.Drum,
            ["crown"] = FontAwesome.Solid.Crown,
            ["spa"] = FontAwesome.Solid.Spa,
            ["check"] = FontAwesome.Solid.Check,
            ["circle"] = FontAwesome.Regular.Circle,
            ["less-than"] = FontAwesome.Solid.LessThan,
            ["greater-than"] = FontAwesome.Solid.GreaterThan,
            ["slash"] = FontAwesome.Solid.Slash,
            ["asterisk"] = FontAwesome.Solid.Asterisk,
            ["equals"] = FontAwesome.Solid.Equals,
            ["plus"] = FontAwesome.Solid.Plus,
            ["snowflake"] = FontAwesome.Solid.Snowflake,
            ["fire"] = FontAwesome.Solid.Fire,
            ["moon"] = FontAwesome.Solid.Moon,
            ["sun"] = FontAwesome.Solid.Sun,
        };

        /// <summary>los nombres de glyph disponibles (para el picker del Creator).</summary>
        public static IReadOnlyCollection<string> GlyphNames => glyphs.Keys;

        /// <summary>resuelve un glyph por nombre; null si desconocido (el builder cae a una forma).</summary>
        public static IconUsage? ResolveGlyph(string name)
            => name != null && glyphs.TryGetValue(name, out var g) ? g : null;
    }
}
