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
            // Batch 2. All procedural on purpose: a new asset travels behind the CI resources pin, a shape
            // drawn in code scales to any size, tints itself and can't come out blank from a stale pin.
            "crystal", "gear", "crescent", "droplet", "feather", "blade", "star4", "star6",
            "hexagon", "butterfly", "mushroom", "koi", "bolt", "arc", "torii", "eye",
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

                case "crystal":
                    return crystal(size, colour);

                case "gear":
                    return gear(size, colour);

                case "crescent":
                    return crescent(size, colour);

                case "droplet":
                    return droplet(size, colour);

                case "feather":
                    return feather(size, colour);

                case "blade":
                    return blade(size, colour);

                case "star4":
                    return spikedStar(size, colour, 4, 0.28f);

                case "star6":
                    return spikedStar(size, colour, 6, 0.42f);

                case "hexagon":
                    return polygon(size, colour, 6);

                case "butterfly":
                    return butterfly(size, colour);

                case "mushroom":
                    return mushroom(size, colour);

                case "koi":
                    return koi(size, colour);

                case "bolt":
                    return bolt(size, colour);

                case "arc":
                    return arc(size, colour);

                case "torii":
                    return torii(size, colour);

                case "eye":
                    return eye(size, colour);

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
            "star4" => "star4",
            "star6" => "star6",
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

        // ────────────────────────────────────────────────────────────────────
        // Batch 2 shapes.
        //
        // Everything is Circle / Box / Triangle rotated and positioned: less handy than a PNG but it
        // tints itself, scales without pixelating and doesn't depend on the CI resources pin.
        //
        // Rule repeated below: Origin = Centre on the container and AutoSizeAxes = Both, because the
        // emitter positions by the centre.
        // ────────────────────────────────────────────────────────────────────

        /// <summary>Faceted gem: a rhombus with a light face and a dark one.</summary>
        private static Drawable crystal(float size, Color4 colour) => new Container
        {
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.72f), Rotation = 45f, Colour = colour,
                },
                // The facet is what separates a gem from a rotated square.
                new Box
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.34f), Rotation = 45f,
                    Position = new Vector2(-size * 0.12f, -size * 0.12f),
                    Colour = Color4.White, Alpha = 0.55f,
                },
            },
        };

        /// <summary>Gear: core with square teeth around it and a hole.</summary>
        private static Drawable gear(float size, Color4 colour)
        {
            var c = new Container { Origin = Anchor.Centre, AutoSizeAxes = Axes.Both };

            const int teeth = 8;
            for (int i = 0; i < teeth; i++)
            {
                float angle = i * 360f / teeth;
                c.Add(new Box
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.22f, size * 0.34f),
                    Rotation = angle,
                    Position = offsetForAngle(angle, size * 0.42f),
                    Colour = colour,
                });
            }

            c.Add(new Circle
            {
                Anchor = Anchor.Centre, Origin = Anchor.Centre,
                Size = new Vector2(size * 0.74f), Colour = colour,
            });

            // The hole is painted in the aura's usual background colour instead of masked out: a masking
            // CircularContainer costs an extra draw call per particle, and these spawn by the dozen.
            c.Add(new Circle
            {
                Anchor = Anchor.Centre, Origin = Anchor.Centre,
                Size = new Vector2(size * 0.26f), Colour = Color4.Black, Alpha = 0.55f,
            });

            return c;
        }

        /// <summary>Crescent moon: a full disc with another one offset on top.</summary>
        private static Drawable crescent(float size, Color4 colour) => new Container
        {
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size), Colour = colour,
                },
                // The bite is translucent black rather than a real cutout, same reason as the gear hole.
                new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.86f),
                    Position = new Vector2(size * 0.3f, -size * 0.08f),
                    Colour = Color4.Black, Alpha = 0.85f,
                },
            },
        };

        /// <summary>Gota: circulo abajo, punta triangular arriba.</summary>
        private static Drawable droplet(float size, Color4 colour) => new Container
        {
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Triangle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.66f, size * 0.7f),
                    Position = new Vector2(0, -size * 0.3f),
                    Colour = colour,
                },
                new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.72f),
                    Position = new Vector2(0, size * 0.14f),
                    Colour = colour,
                },
            },
        };

        /// <summary>Feather: ovals shrinking toward the tip, over a shaft.</summary>
        private static Drawable feather(float size, Color4 colour)
        {
            var c = new Container { Origin = Anchor.Centre, AutoSizeAxes = Axes.Both };

            c.Add(new Box
            {
                Anchor = Anchor.Centre, Origin = Anchor.Centre,
                Size = new Vector2(size * 0.07f, size * 1.1f), Colour = colour,
            });

            const int barbs = 5;
            for (int i = 0; i < barbs; i++)
            {
                float t = i / (float)(barbs - 1);
                float w = size * (0.44f - 0.28f * t);
                float y = size * (0.34f - 0.62f * t);

                // One pair per barb, one on each side, leaning toward the tip.
                c.Add(new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(w, size * 0.2f),
                    Position = new Vector2(-w * 0.5f, y), Rotation = -28f, Colour = colour,
                });
                c.Add(new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(w, size * 0.2f),
                    Position = new Vector2(w * 0.5f, y), Rotation = 28f, Colour = colour,
                });
            }

            return c;
        }

        /// <summary>Lanceolate leaf with a light vein.</summary>
        private static Drawable blade(float size, Color4 colour) => new Container
        {
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.46f, size * 1.15f), Colour = colour,
                },
                new Box
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.05f, size * 0.9f),
                    Colour = Color4.White, Alpha = 0.4f,
                },
            },
        };

        /// <summary>Sharp-pointed star. `waist` is how thin the tip gets.</summary>
        private static Drawable spikedStar(float size, Color4 colour, int points, float waist)
        {
            var c = new Container { Origin = Anchor.Centre, AutoSizeAxes = Axes.Both };

            for (int i = 0; i < points; i++)
            {
                float angle = i * 360f / points;
                c.Add(new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * waist, size),
                    Rotation = angle, Colour = colour,
                });
            }

            c.Add(new Circle
            {
                Anchor = Anchor.Centre, Origin = Anchor.Centre,
                Size = new Vector2(size * 0.26f), Colour = Color4.White, Alpha = 0.8f,
            });

            return c;
        }

        /// <summary>Regular polygon, built from triangles out of the centre.</summary>
        private static Drawable polygon(float size, Color4 colour, int sides)
        {
            var c = new Container { Origin = Anchor.Centre, AutoSizeAxes = Axes.Both };

            for (int i = 0; i < sides; i++)
            {
                float angle = i * 360f / sides;
                c.Add(new Triangle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.62f, size * 0.56f),
                    Rotation = angle + 180f,
                    Position = offsetForAngle(angle, size * 0.26f),
                    Colour = colour,
                });
            }

            return c;
        }

        /// <summary>Butterfly: two wings per side, the upper one larger, and the body.</summary>
        private static Drawable butterfly(float size, Color4 colour)
        {
            var c = new Container { Origin = Anchor.Centre, AutoSizeAxes = Axes.Both };

            foreach (int side in new[] { -1, 1 })
            {
                c.Add(new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.52f, size * 0.44f),
                    Position = new Vector2(side * size * 0.28f, -size * 0.16f),
                    Rotation = side * 18f, Colour = colour,
                });
                c.Add(new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.38f, size * 0.32f),
                    Position = new Vector2(side * size * 0.24f, size * 0.18f),
                    Rotation = -side * 14f, Colour = colour, Alpha = 0.85f,
                });
            }

            c.Add(new Circle
            {
                Anchor = Anchor.Centre, Origin = Anchor.Centre,
                Size = new Vector2(size * 0.1f, size * 0.62f),
                Colour = Color4.White, Alpha = 0.5f,
            });

            return c;
        }

        /// <summary>Mushroom: cap, stem and a couple of spots.</summary>
        private static Drawable mushroom(float size, Color4 colour) => new Container
        {
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.26f, size * 0.5f),
                    Position = new Vector2(0, size * 0.26f),
                    Colour = Color4.White, Alpha = 0.75f,
                },
                new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size, size * 0.68f),
                    Position = new Vector2(0, -size * 0.12f), Colour = colour,
                },
                new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.18f),
                    Position = new Vector2(-size * 0.2f, -size * 0.18f),
                    Colour = Color4.White, Alpha = 0.7f,
                },
                new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.13f),
                    Position = new Vector2(size * 0.22f, -size * 0.1f),
                    Colour = Color4.White, Alpha = 0.7f,
                },
            },
        };

        /// <summary>Fish in profile: oval body and triangle tail.</summary>
        private static Drawable koi(float size, Color4 colour) => new Container
        {
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Triangle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.46f, size * 0.5f),
                    Position = new Vector2(-size * 0.44f, 0), Rotation = -90f,
                    Colour = colour, Alpha = 0.85f,
                },
                new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size, size * 0.42f), Colour = colour,
                },
                new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.12f),
                    Position = new Vector2(size * 0.3f, -size * 0.06f),
                    Colour = Color4.White, Alpha = 0.9f,
                },
            },
        };

        /// <summary>Lightning bolt: two opposite-leaning bars that cross.</summary>
        private static Drawable bolt(float size, Color4 colour) => new Container
        {
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.2f, size * 0.62f),
                    Position = new Vector2(size * 0.1f, -size * 0.24f),
                    Rotation = 22f, Colour = colour,
                },
                new Box
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.2f, size * 0.62f),
                    Position = new Vector2(-size * 0.1f, size * 0.24f),
                    Rotation = -22f, Colour = colour,
                },
            },
        };

        /// <summary>Thin arc: an open half moon, useful as a wake.</summary>
        private static Drawable arc(float size, Color4 colour)
        {
            var c = new Container { Origin = Anchor.Centre, AutoSizeAxes = Axes.Both };

            // A real arc is pieces laid on a circumference. 7 is enough to read curved rather than jagged.
            const int chunks = 7;
            for (int i = 0; i < chunks; i++)
            {
                float t = i / (float)(chunks - 1);
                float angle = -70f + t * 140f;
                c.Add(new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.16f),
                    Position = offsetForAngle(angle, size * 0.46f),
                    Colour = colour,
                    // The ends thin out so the arc has direction.
                    Alpha = 0.35f + 0.65f * (1f - MathF.Abs(t - 0.5f) * 2f),
                });
            }

            return c;
        }

        /// <summary>The torii gate, the house symbol.</summary>
        private static Drawable torii(float size, Color4 colour) => new Container
        {
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                // Curved lintel on top (kasagi), a bar below it (shimaki) and the two pillars.
                new Box
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 1.1f, size * 0.16f),
                    Position = new Vector2(0, -size * 0.42f), Colour = colour,
                },
                new Box
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.86f, size * 0.11f),
                    Position = new Vector2(0, -size * 0.2f), Colour = colour,
                },
                new Box
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.13f, size * 0.86f),
                    Position = new Vector2(-size * 0.32f, size * 0.1f), Colour = colour,
                },
                new Box
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.13f, size * 0.86f),
                    Position = new Vector2(size * 0.32f, size * 0.1f), Colour = colour,
                },
            },
        };

        /// <summary>Eye: light almond with iris and pupil.</summary>
        private static Drawable eye(float size, Color4 colour) => new Container
        {
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 1.1f, size * 0.56f),
                    Colour = Color4.White, Alpha = 0.9f,
                },
                new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.46f), Colour = colour,
                },
                new Circle
                {
                    Anchor = Anchor.Centre, Origin = Anchor.Centre,
                    Size = new Vector2(size * 0.2f), Colour = Color4.Black, Alpha = 0.8f,
                },
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
            // Batch 2. FontAwesome already ships with the game, so growing this costs no assets and doesn't
            // touch the CI resources pin.
            ["ghost"] = FontAwesome.Solid.Ghost,
            ["feather"] = FontAwesome.Solid.Feather,
            ["dragon"] = FontAwesome.Solid.Dragon,
            ["fish"] = FontAwesome.Solid.Fish,
            ["frog"] = FontAwesome.Solid.Frog,
            ["dove"] = FontAwesome.Solid.Dove,
            ["spider"] = FontAwesome.Solid.Spider,
            ["hippo"] = FontAwesome.Solid.Hippo,
            ["cat"] = FontAwesome.Solid.Cat,
            ["dog"] = FontAwesome.Solid.Dog,
            ["seedling"] = FontAwesome.Solid.Seedling,
            ["tree"] = FontAwesome.Solid.Tree,
            ["cannabis"] = FontAwesome.Solid.Cannabis,
            ["feather-alt"] = FontAwesome.Solid.FeatherAlt,
            ["water"] = FontAwesome.Solid.Water,
            ["wind"] = FontAwesome.Solid.Wind,
            ["bolt"] = FontAwesome.Solid.Bolt,
            ["meteor"] = FontAwesome.Solid.Meteor,
            ["rainbow"] = FontAwesome.Solid.Rainbow,
            ["cloud"] = FontAwesome.Solid.Cloud,
            ["icicles"] = FontAwesome.Solid.Icicles,
            ["gem"] = FontAwesome.Solid.Gem,
            ["ring"] = FontAwesome.Solid.Ring,
            ["dice"] = FontAwesome.Solid.Dice,
            ["chess-knight"] = FontAwesome.Solid.ChessKnight,
            ["puzzle"] = FontAwesome.Solid.PuzzlePiece,
            ["rocket"] = FontAwesome.Solid.Rocket,
            ["satellite"] = FontAwesome.Solid.Satellite,
            ["atom"] = FontAwesome.Solid.Atom,
            ["flask"] = FontAwesome.Solid.Flask,
            ["microscope"] = FontAwesome.Solid.Microscope,
            ["cog"] = FontAwesome.Solid.Cog,
            ["bell"] = FontAwesome.Solid.Bell,
            ["anchor"] = FontAwesome.Solid.Anchor,
            ["compass"] = FontAwesome.Solid.Compass,
            ["key"] = FontAwesome.Solid.Key,
            ["scroll"] = FontAwesome.Solid.Scroll,
            ["book"] = FontAwesome.Solid.Book,
            ["mask"] = FontAwesome.Solid.Mask,
            ["hat-wizard"] = FontAwesome.Solid.HatWizard,
            ["skull"] = FontAwesome.Solid.Skull,
            ["bone"] = FontAwesome.Solid.Bone,
            ["candy"] = FontAwesome.Solid.CandyCane,
            ["cookie"] = FontAwesome.Solid.Cookie,
            ["ice-cream"] = FontAwesome.Solid.IceCream,
            ["pizza"] = FontAwesome.Solid.PizzaSlice,
            ["coffee"] = FontAwesome.Solid.Coffee,
            ["guitar"] = FontAwesome.Solid.Guitar,
            ["headphones"] = FontAwesome.Solid.Headphones,
            ["gamepad"] = FontAwesome.Solid.Gamepad,
            ["trophy"] = FontAwesome.Solid.Trophy,
            ["medal"] = FontAwesome.Solid.Medal,
            ["fire-alt"] = FontAwesome.Solid.FireAlt,
            ["burn"] = FontAwesome.Solid.Burn,
            ["star-half"] = FontAwesome.Solid.StarHalf,
            ["certificate"] = FontAwesome.Solid.Certificate,
            ["yin-yang"] = FontAwesome.Solid.YinYang,
            ["torii-gate"] = FontAwesome.Solid.ToriiGate,
        };

        /// <summary>los nombres de glyph disponibles (para el picker del Creator).</summary>
        public static IReadOnlyCollection<string> GlyphNames => glyphs.Keys;

        /// <summary>resuelve un glyph por nombre; null si desconocido (el builder cae a una forma).</summary>
        public static IconUsage? ResolveGlyph(string name)
            => name != null && glyphs.TryGetValue(name, out var g) ? g : null;
    }
}
