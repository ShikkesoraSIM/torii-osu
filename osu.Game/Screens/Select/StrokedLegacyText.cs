// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// torii: texto legacy (estilo osu!stable) con un drop shadow suave mas un contorno fino casi opaco
    /// alrededor de los glifos, asi se lee como el texto de la UI de stable sobre fondos claros/cargados.
    /// el stroke se estampa en 8 direcciones con un offset sub-pixel (constante, NO escalado por el
    /// tamano de fuente) asi queda apretado y definido sin engordar el texto; la copia real (de
    /// adelante) lleva una sombra sutil encima. todas las copias comparten el mismo atlas de fuente bitmap asi entran en el
    /// mismo draw call. casi drop-in de <see cref="OsuSpriteText"/>: seteas Text, Font, Colour, Anchor/Origin como siempre.
    /// </summary>
    public partial class StrokedLegacyText : CompositeDrawable
    {
        // offsets unitarios en 8 direcciones (lados + esquinas) asi cada glifo queda con el anillo completo.
        private static readonly Vector2[] directions =
        {
            new Vector2(-1, -1), new Vector2(0, -1), new Vector2(1, -1),
            new Vector2(-1, 0), new Vector2(1, 0),
            new Vector2(-1, 1), new Vector2(0, 1), new Vector2(1, 1),
        };

        private readonly OsuSpriteText main;
        private readonly OsuSpriteText[] strokes = new OsuSpriteText[directions.Length];

        /// <summary>grosor absoluto del stroke en px locales. fino: un contorno apretado, no un halo.</summary>
        public float StrokeWidth { get; init; } = 0.35f;

        public LocalisableString Text
        {
            set
            {
                main.Text = value;
                foreach (var s in strokes)
                    s.Text = value;
            }
        }

        public FontUsage Font
        {
            set
            {
                main.Font = value;
                foreach (var s in strokes)
                    s.Font = value;
            }
        }

        /// <summary>color del texto de adelante. por defecto blanco.</summary>
        public new Color4 Colour
        {
            set => main.Colour = value;
        }

        /// <summary>color del anillo del stroke. por defecto negro casi opaco, como stable.</summary>
        public Color4 StrokeColour
        {
            set
            {
                foreach (var s in strokes)
                    s.Colour = value;
            }
        }

        public StrokedLegacyText()
        {
            AutoSizeAxes = Axes.Both;

            var children = new OsuSpriteText[directions.Length + 1];

            for (int i = 0; i < directions.Length; i++)
            {
                children[i] = strokes[i] = new OsuSpriteText
                {
                    Shadow = false,
                    // contorno fino y suave: se nota el borde pero sin quedar negro/cargado.
                    Colour = new Color4(0f, 0f, 0f, 0.5f),
                    Position = directions[i],
                };
            }

            children[directions.Length] = main = new OsuSpriteText
            {
                // drop shadow apenas, mas suave que antes asi no carga el texto de negro.
                Shadow = true,
                ShadowColour = new Color4(0f, 0f, 0f, 0.3f),
                ShadowOffset = new Vector2(0f, 0.08f),
                Colour = Color4.White,
            };

            InternalChildren = children;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // aplicamos el stroke width final ahora que ya se seteo el init prop StrokeWidth.
            for (int i = 0; i < directions.Length; i++)
                strokes[i].Position = directions[i] * StrokeWidth;
        }
    }
}
