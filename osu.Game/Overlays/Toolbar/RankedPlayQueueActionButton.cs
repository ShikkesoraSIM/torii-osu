// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osuTK.Graphics;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>
    /// El boton grande del panel: entrar a ranked play.
    /// </summary>
    /// <remarks>
    /// Es la unica accion del panel y por eso ocupa el ancho entero arriba de todo,
    /// antes que la cola y las partidas. El resto del panel es contexto para decidir;
    /// esto es lo que se hace despues de decidir, y tiene que estar donde el pulgar
    /// ya esta.
    /// </remarks>
    public partial class RankedPlayQueueActionButton : OsuClickableContainer
    {
        private static readonly Color4 ranked_orange = new Color4(255, 146, 43, 255);

        private Box background = null!;
        private Box hoverGlow = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Child = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 9,
                CornerExponent = 2.4f,
                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ranked_orange.Opacity(0.9f),
                    },
                    hoverGlow = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White.Opacity(0.15f),
                        Blending = BlendingParameters.Additive,
                        Alpha = 0,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold),
                        Text = @"Queue",
                        // Texto oscuro sobre naranja: en blanco sobre este naranja el
                        // contraste queda flojo y se lee peor de lo que parece.
                        Colour = new Color4(38, 24, 10, 255),
                    },
                },
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            hoverGlow.FadeIn(80, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            hoverGlow.FadeOut(160, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            background.ScaleTo(0.98f, 80, Easing.OutQuint);
            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            background.ScaleTo(1f, 200, Easing.OutElastic);
            base.OnMouseUp(e);
        }
    }
}
