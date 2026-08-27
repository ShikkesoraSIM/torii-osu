// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Graphics.Containers;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>
    /// Salir de la cola. Aparece al lado del boton grande mientras estas buscando.
    /// </summary>
    /// <remarks>
    /// Chico y sin texto porque no compite con nada: mientras buscas es la unica accion
    /// posible, y una cruz se entiende sin leer. Rojo apagado y no rojo fuerte, que salir
    /// de la cola no es destruir nada.
    /// </remarks>
    public partial class RankedPlayStopQueueButton : OsuClickableContainer
    {
        private Box background = null!;

        public RankedPlayStopQueueButton()
        {
            Masking = true;
            CornerRadius = 9;
            CornerExponent = 2.4f;
            Margin = new MarginPadding { Left = 8 };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(84, 46, 46, 255),
                },
                new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(13),
                    Icon = FontAwesome.Solid.Times,
                    Colour = new Color4(240, 150, 150, 255),
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(new Color4(120, 58, 58, 255), 120, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeColour(new Color4(84, 46, 46, 255), 200, Easing.OutQuint);
            base.OnHoverLost(e);
        }
    }
}
