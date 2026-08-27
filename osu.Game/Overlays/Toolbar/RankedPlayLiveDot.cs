// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>
    /// El puntito verde de "hay partida en curso".
    /// </summary>
    /// <remarks>
    /// Reemplaza al "1 vs" que estaba antes al lado del numero de la cola. Dos numeros
    /// pegados sin nada que los separe ("7  1 vs") no se leen: parecen un solo dato
    /// partido al medio y hay que frenar a descifrar cual es cual.
    ///
    /// Un punto que late resuelve eso sin texto: verde y latiendo es "hay algo pasando
    /// AHORA" en cualquier interfaz, y no compite con el numero de la cola, que es el
    /// dato accionable (si hay gente esperando, entras). Cuantas partidas hay
    /// exactamente es curiosidad, no decision: eso vive en el tooltip.
    /// </remarks>
    public partial class RankedPlayLiveDot : CompositeDrawable
    {
        private static readonly Color4 live_green = new Color4(86, 227, 128, 255);

        private Circle core = null!;
        private Circle halo = null!;

        public RankedPlayLiveDot()
        {
            AlwaysPresent = true;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                halo = new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Colour = live_green.Opacity(0.35f),
                    Scale = new Vector2(1.7f),
                    Alpha = 0.4f,
                },
                core = new Circle
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Colour = live_green,
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Late despacio. Rapido pareceria una alarma, y no hay nada que atender:
            // solo esta avisando que hay vida del otro lado.
            core.ScaleTo(1.15f, 620, Easing.OutQuint)
                .Then().ScaleTo(1f, 900, Easing.OutQuint)
                .Loop();

            halo.ScaleTo(2.2f, 620, Easing.OutQuint).FadeTo(0.15f, 620, Easing.OutQuint)
                .Then().ScaleTo(1.7f, 900, Easing.OutQuint).FadeTo(0.4f, 900, Easing.OutQuint)
                .Loop();
        }
    }
}
