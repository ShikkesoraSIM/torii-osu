// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Cosmetics
{
    /// <summary>
    /// Shape factories for <see cref="CosmeticParticleTrail"/>. Each returns a
    /// fresh, fully-styled particle drawable for one emission; the trail then
    /// positions, animates and expires it. The int is the running emission index
    /// so a factory can vary colour/shape down the stream (e.g. rainbow).
    ///
    /// These are deliberately built from primitives + a couple of safe icons so
    /// every trail has its OWN form and personality, not just a recoloured dot.
    /// </summary>
    public static class CosmeticParticles
    {
        private static readonly Color4 white = new Color4(255, 255, 255, 255);
        private static readonly Color4 gold = new Color4(255, 225, 120, 255);
        private static readonly Color4 pink = new Color4(255, 135, 195, 255);
        private static readonly Color4 rose = new Color4(255, 90, 140, 255);
        private static readonly Color4 ice = new Color4(220, 245, 255, 255);
        private static readonly Color4 cyan = new Color4(120, 215, 255, 255);

        private static readonly Color4[] sakura = { new Color4(255, 200, 222, 255), new Color4(255, 158, 195, 255), new Color4(232, 110, 170, 255) };
        private static readonly Color4[] flame = { new Color4(255, 235, 120, 255), new Color4(255, 150, 40, 255), new Color4(230, 60, 30, 255) };
        private static readonly Color4[] notes = { new Color4(120, 200, 255, 255), new Color4(255, 170, 90, 255), new Color4(180, 140, 255, 255) };

        /// <summary>Gold five-point star.</summary>
        public static Drawable Star(int index) => new SpriteIcon
        {
            Origin = Anchor.Centre,
            Icon = FontAwesome.Solid.Star,
            Size = new Vector2(14),
            Colour = gold,
        };

        /// <summary>Soft heart, pink/rose alternating.</summary>
        public static Drawable Heart(int index) => new SpriteIcon
        {
            Origin = Anchor.Centre,
            Icon = FontAwesome.Solid.Heart,
            Size = new Vector2(12),
            Colour = index % 2 == 0 ? pink : rose,
        };

        /// <summary>Icy six-arm snowflake.</summary>
        public static Drawable Snowflake(int index) => new SpriteIcon
        {
            Origin = Anchor.Centre,
            Icon = FontAwesome.Solid.Snowflake,
            Size = new Vector2(13),
            Colour = ice,
        };

        /// <summary>Music note, colour-cycled.</summary>
        public static Drawable Note(int index) => new SpriteIcon
        {
            Origin = Anchor.Centre,
            Icon = FontAwesome.Solid.Music,
            Size = new Vector2(12),
            Colour = notes[index % notes.Length],
        };

        /// <summary>A single drifting cherry-blossom petal (a tinted oval).</summary>
        public static Drawable Petal(int index) => new Circle
        {
            Origin = Anchor.Centre,
            Size = new Vector2(6, 10),
            Colour = sakura[index % sakura.Length],
            Rotation = index * 47 % 360,
        };

        /// <summary>Hollow bubble ring, varied size.</summary>
        public static Drawable Bubble(int index)
        {
            float size = 9 + index % 3 * 4;
            return new CircularContainer
            {
                Origin = Anchor.Centre,
                Size = new Vector2(size),
                Masking = true,
                BorderThickness = 2f,
                BorderColour = cyan,
                Child = new Box { RelativeSizeAxes = Axes.Both, Colour = cyan, Alpha = 0.12f },
            };
        }

        /// <summary>A teardrop flame blob (warm, flickers hot→cool by index).</summary>
        public static Drawable Flame(int index) => new Circle
        {
            Origin = Anchor.Centre,
            Size = new Vector2(8, 12),
            Colour = flame[index % flame.Length],
        };

        /// <summary>Four-point twinkle: a bright core with a thin cross.</summary>
        public static Drawable Sparkle(int index)
        {
            Color4 c = index % 2 == 0 ? white : gold;
            return new Container
            {
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new Box { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(2.2f, 12), Colour = c },
                    new Box { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(12, 2.2f), Colour = c },
                    new Circle { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(4.5f), Colour = white },
                },
            };
        }

        /// <summary>Rainbow twinkle: hue cycles down the emission stream.</summary>
        public static Drawable RainbowSparkle(int index)
        {
            var hue = index * 0.07f % 1f;
            Color4 c = Colour4.FromHSV(hue, 0.85f, 1f);
            return new Container
            {
                Origin = Anchor.Centre,
                AutoSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new Box { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(2.4f, 13), Colour = c },
                    new Box { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(13, 2.4f), Colour = c },
                    new Circle { Anchor = Anchor.Centre, Origin = Anchor.Centre, Size = new Vector2(5f), Colour = white },
                },
            };
        }
    }
}
