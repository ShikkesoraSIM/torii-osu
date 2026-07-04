// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.ReplayRender;
using osu.Game.Scoring;
using osuTK.Graphics;

namespace osu.Game.Screens.Ranking
{
    /// <summary>
    /// boton torii de la results screen: renderea el replay a video via o!rdr.
    /// abre el <see cref="ReplayRenderOverlay"/> (que vive en OsuGame, asi el
    /// progreso sobrevive a salir de la screen). glow rojizo sutil para
    /// distinguirlo de los botones stock sin gritar.
    /// </summary>
    public partial class RenderVideoButton : GrayButton
    {
        private readonly ScoreInfo score;

        [Resolved(canBeNull: true)]
        private ReplayRenderOverlay renderOverlay { get; set; }

        public RenderVideoButton(ScoreInfo score)
            : base(FontAwesome.Solid.Video)
        {
            this.score = score;
            Size = new osuTK.Vector2(50, 30);
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            TooltipText = "Render to video (o!rdr)";

            Background.Colour = new Color4(88, 22, 32, 255);
            Icon.Colour = new Color4(255, 160, 175, 255);

            // stroke + glow rojizo: EdgeEffect va sobre este container (GrayButton ya
            // maskea el contenido). sin tocar Anchor/Origin: el FillFlow horizontal de
            // la results screen exige el mismo anchor-X en todos los hijos.
            Masking = true;
            CornerRadius = 5;
            BorderThickness = 1.5f;
            BorderColour = new Color4(235, 80, 104, 200);
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = new Color4(235, 80, 104, 90),
                Radius = 6,
            };

            Action = () => renderOverlay?.ShowFor(score);
        }
    }
}
