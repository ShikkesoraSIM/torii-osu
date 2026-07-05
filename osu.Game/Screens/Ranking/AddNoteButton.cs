// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Overlays.ScoreNotes;
using osu.Game.Scoring;
using osuTK.Graphics;

namespace osu.Game.Screens.Ranking
{
    /// <summary>
    /// boton torii de la results screen: agregarle una nota a la play (texto + imagen).
    /// gemelo del de render pero con glow AZUL. solo aparece en scores PROPIOS ya
    /// subidos al server (la nota se valida contra el dueño del score). mismo patron
    /// bindeado a SelectedScore para funcionar en solo/playlist/multi/daily.
    /// </summary>
    public partial class AddNoteButton : GrayButton
    {
        public readonly Bindable<ScoreInfo?> Score = new Bindable<ScoreInfo?>();

        [Resolved(canBeNull: true)]
        private ScoreNoteOverlay noteOverlay { get; set; }

        [Resolved]
        private IAPIProvider api { get; set; }

        public AddNoteButton(ScoreInfo? score = null)
            : base(FontAwesome.Solid.CommentDots)
        {
            Score.Value = score;
            Size = new osuTK.Vector2(50, 30);
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            TooltipText = "Add a note to this play";

            Background.Colour = new Color4(22, 42, 82, 255);
            Icon.Colour = new Color4(160, 205, 255, 255);

            // misma curvatura que el boton (OsuAnimatedButton: radius 10, exp 2.5) para
            // que el glow siga la curva y no se vea cuadrado.
            Masking = true;
            CornerRadius = 10;
            CornerExponent = 2.5f;
            BorderThickness = 1.5f;
            BorderColour = new Color4(90, 160, 245, 200);
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = new Color4(90, 160, 245, 90),
                Radius = 8,
                Roundness = 10,
            };

            Action = () =>
            {
                if (isOwnOnlineScore())
                    noteOverlay?.ShowFor(Score.Value);
            };

            Score.BindValueChanged(_ => updateVisibility(), true);
        }

        private bool isOwnOnlineScore()
        {
            var score = Score.Value;
            return score?.OnlineID > 0 && score.UserID == api.LocalUser.Value?.Id;
        }

        private void updateVisibility() => this.FadeTo(isOwnOnlineScore() ? 1 : 0, 150, Easing.OutQuint);
    }
}
