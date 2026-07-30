// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Localisation;
using osuTK;

namespace osu.Game.Overlays.ToriiWelcome
{
    /// <summary>
    /// torii: primer paso, puro contexto. lo unico que tiene que quedar claro aca es que esto es un server
    /// aparte y que lo que juegue es suyo y vive aca, mas tres cosas concretas que puede ir a probar.
    /// No hay nada configurable a proposito: si el primer paso pide decisiones, se pasa de largo leyendo.
    /// </summary>
    [LocalisableDescription(typeof(ToriiWelcomeStrings), nameof(ToriiWelcomeStrings.IntroTitle))]
    public partial class ScreenToriiIntro : WizardScreen
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            Content.Children = new Drawable[]
            {
                new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    Text = ToriiWelcomeStrings.IntroDescription,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(10),
                    Children = new Drawable[]
                    {
                        new FeatureRow(FontAwesome.Solid.Coins, ToriiWelcomeStrings.IntroPointsTitle, ToriiWelcomeStrings.IntroPointsDescription),
                        new FeatureRow(FontAwesome.Solid.Video, ToriiWelcomeStrings.IntroRenderTitle, ToriiWelcomeStrings.IntroRenderDescription),
                        new FeatureRow(FontAwesome.Solid.CommentDots, ToriiWelcomeStrings.IntroNotesTitle, ToriiWelcomeStrings.IntroNotesDescription),
                    },
                },
            };
        }

        /// <summary>
        /// Tarjetita de "esto existe": icono a la izquierda, titulo y descripcion apilados.
        /// Todo el texto va en flows porque las descripciones son de largo variable y traducibles.
        /// </summary>
        private partial class FeatureRow : CompositeDrawable
        {
            private readonly IconUsage icon;
            private readonly LocalisableString title;
            private readonly LocalisableString description;

            public FeatureRow(IconUsage icon, LocalisableString title, LocalisableString description)
            {
                this.icon = icon;
                this.title = title;
                this.description = description;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Masking = true;
                CornerRadius = 10;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colourProvider.Background5,
                    },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding { Horizontal = 15, Vertical = 12 },
                        ColumnDimensions = new[]
                        {
                            new Dimension(GridSizeMode.Absolute, 40),
                            new Dimension(),
                        },
                        RowDimensions = new[]
                        {
                            new Dimension(GridSizeMode.AutoSize),
                        },
                        Content = new[]
                        {
                            new Drawable[]
                            {
                                new SpriteIcon
                                {
                                    Anchor = Anchor.TopLeft,
                                    Origin = Anchor.TopLeft,
                                    Icon = icon,
                                    Size = new Vector2(20),
                                    Colour = colourProvider.Highlight1,
                                    Margin = new MarginPadding { Top = 2 },
                                },
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(4),
                                    Children = new Drawable[]
                                    {
                                        new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE, weight: FontWeight.Bold))
                                        {
                                            Text = title,
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Colour = colourProvider.Content1,
                                        },
                                        new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE - 2))
                                        {
                                            Text = description,
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Colour = colourProvider.Content2,
                                        },
                                    },
                                },
                            },
                        },
                    },
                };
            }
        }
    }
}
