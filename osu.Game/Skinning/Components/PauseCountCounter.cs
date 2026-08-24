// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Screens.Play;
using osuTK;

namespace osu.Game.Skinning.Components
{
    /// <summary>
    /// Torii: contador de pausas para el skin layout editor.
    ///
    /// En Torii cada pausa reduce el pp final un 7% (compuesto, aplicado por el
    /// server; ver <see cref="osu.Game.Scoring.ToriiPausePenalty"/>). Hasta ahora
    /// esa penalizacion era invisible durante el gameplay: el usuario pausaba,
    /// cobraba menos pp y nunca sabia por que. Este componente muestra cuantas
    /// pausas lleva la jugada actual, y por defecto se pinta de rojo apenas hay
    /// una para que se entienda que algo cambio.
    ///
    /// Implementa <see cref="IToriiSkinComponent"/> asi aparece en la seccion
    /// "Torii Exclusive Components" fijada arriba de todo en el toolbox del skin
    /// editor, con el glyph del torii al lado del nombre.
    ///
    /// Las pausas se registran en <c>SubmittingPlayer.Pause()</c> sobre
    /// <c>ScoreInfo.Pauses</c>; aca solo las leemos. En contextos sin gameplay
    /// (el propio editor de skins) no hay <see cref="GameplayState"/> y el
    /// contador muestra 0.
    /// </summary>
    public partial class PauseCountCounter : CompositeDrawable, ISerialisableDrawable, IToriiSkinComponent
    {
        public bool UsesFixedAnchor { get; set; }

        [SettingSource("Show label", "Show the small \"PAUSES\" header above the count.")]
        public Bindable<bool> ShowLabel { get; } = new BindableBool(true);

        [SettingSource("Label text", "What the header above the count reads.")]
        public Bindable<string> LabelText { get; } = new Bindable<string>(@"PAUSES");

        [SettingSource("Highlight when paused", "Turn the count red once at least one pause has been used (each pause costs 7% of the final pp).")]
        public Bindable<bool> HighlightWhenPaused { get; } = new BindableBool(true);

        [Resolved(canBeNull: true)]
        private GameplayState? gameplayState { get; set; }

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        private OsuSpriteText label = null!;
        private InnerCounter counter = null!;

        private int lastCount = -1;

        public PauseCountCounter()
        {
            AutoSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 1),
                Children = new Drawable[]
                {
                    label = new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = OsuFont.Torus.With(size: 12, weight: FontWeight.SemiBold),
                    },
                    counter = new InnerCounter
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ShowLabel.BindValueChanged(v => label.Alpha = v.NewValue ? 1 : 0, true);
            LabelText.BindValueChanged(t => label.Text = t.NewValue.ToUpperInvariant(), true);
            HighlightWhenPaused.BindValueChanged(_ => updateColour(), true);

            updateCount();
        }

        protected override void Update()
        {
            base.Update();
            updateCount();
        }

        private void updateCount()
        {
            int count = gameplayState?.Score.ScoreInfo.Pauses.Count ?? 0;

            if (count == lastCount)
                return;

            lastCount = count;
            counter.Current.Value = count;

            // pop cortito en cada pausa nueva, para que el ojo lo registre.
            if (count > 0)
                counter.ScaleTo(1.25f, 60, Easing.OutQuint).Then().ScaleTo(1f, 300, Easing.OutQuint);

            updateColour();
        }

        private void updateColour()
        {
            bool highlight = HighlightWhenPaused.Value && lastCount > 0;
            counter.FadeColour(highlight ? colours.Red1 : osuTK.Graphics.Color4.White, 150, Easing.OutQuint);
        }

        private partial class InnerCounter : RollingCounter<int>
        {
            protected override double RollingDuration => 250;

            protected override OsuSpriteText CreateSpriteText() => new OsuSpriteText
            {
                Font = OsuFont.Torus.With(size: 20, weight: FontWeight.SemiBold, fixedWidth: true),
            };
        }
    }
}
