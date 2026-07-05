// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
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
    /// progreso sobrevive a salir de la screen). stroke + glow rojizo sutil.
    ///
    /// se bindea a SelectedScore (como ReplayDownloadButton): asi funciona igual en
    /// solo, playlist, multi y daily challenge — aparece cuando el score ya tiene
    /// OnlineID (en daily el score arranca null y se resuelve despues), y se
    /// colapsa (Alpha 0 = fuera del flow) mientras no haya un score renderizable.
    /// </summary>
    public partial class RenderVideoButton : GrayButton
    {
        public readonly Bindable<ScoreInfo?> Score = new Bindable<ScoreInfo?>();

        [Resolved(canBeNull: true)]
        private ReplayRenderOverlay renderOverlay { get; set; }

        public RenderVideoButton(ScoreInfo? score = null)
            : base(FontAwesome.Solid.Video)
        {
            Score.Value = score;
            Size = new osuTK.Vector2(50, 30);
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            TooltipText = "Render to video (o!rdr)";

            Background.Colour = new Color4(88, 22, 32, 255);
            Icon.Colour = new Color4(255, 160, 175, 255);

            // el glow tiene que seguir la MISMA curvatura que el boton visible
            // (OsuAnimatedButton usa CornerRadius 10 + exponent 2.5), sino se ve
            // cuadrado alrededor de un boton redondeado.
            Masking = true;
            CornerRadius = 10;
            CornerExponent = 2.5f;
            BorderThickness = 1.5f;
            BorderColour = new Color4(235, 80, 104, 200);
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = new Color4(235, 80, 104, 90),
                Radius = 8,
                Roundness = 10,
            };

            Action = () =>
            {
                if (Score.Value?.OnlineID > 0)
                    renderOverlay?.ShowFor(Score.Value);
            };

            Score.BindValueChanged(_ => updateVisibility(), true);
        }

        private void updateVisibility()
        {
            // solo scores subidos al server (con replay) son renderizables.
            bool renderable = Score.Value?.OnlineID > 0;
            this.FadeTo(renderable ? 1 : 0, 150, Easing.OutQuint);
        }
    }
}
