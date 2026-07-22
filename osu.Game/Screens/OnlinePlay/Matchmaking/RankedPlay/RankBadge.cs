// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay
{
    /// <summary>
    /// torii: badge que muestra el rank del jugador (ej "Gold 2") derivado del rating de matchmaking.
    /// Una pildora redondeada uniforme: fondo dark-glass, borde del color del tier, icono + texto inline.
    /// Llamar <see cref="SetRating"/> para actualizarlo.
    /// </summary>
    public partial class RankBadge : CompositeDrawable
    {
        private Box background = null!;
        private Box accent = null!;
        private SpriteIcon icon = null!;
        private OsuSpriteText tierText = null!;
        private OsuSpriteText ratingText = null!;

        public RankBadge()
        {
            AutoSizeAxes = Axes.X;
            Height = 44;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Masking = true;
            CornerRadius = 10;
            BorderThickness = 2;

            InternalChildren = new Drawable[]
            {
                // base oscura + un tinte del color del tier muy suave encima (para que no sea gris plano).
                background = new Box { RelativeSizeAxes = Axes.Both },
                accent = new Box { RelativeSizeAxes = Axes.Both, Alpha = 0.14f },
                new FillFlowContainer
                {
                    // todos los hijos con el mismo Anchor.Y (CentreLeft): un flow horizontal exige el
                    // mismo anchor cross-axis o crashea cada frame. ver [[reference_briefingglass_fillflow_crash]].
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Spacing = new Vector2(10, 0),
                    Padding = new MarginPadding { Horizontal = 15, Vertical = 6 },
                    Children = new Drawable[]
                    {
                        icon = new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Size = new Vector2(20),
                        },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Vertical,
                            Spacing = new Vector2(0, 1),
                            Children = new Drawable[]
                            {
                                tierText = new OsuSpriteText
                                {
                                    Font = OsuFont.Torus.With(size: 18, weight: FontWeight.SemiBold),
                                },
                                ratingText = new OsuSpriteText
                                {
                                    Font = OsuFont.Torus.With(size: 11, weight: FontWeight.SemiBold),
                                    Colour = new Color4(0.72f, 0.72f, 0.78f, 1f),
                                },
                            }
                        }
                    }
                }
            };

            SetRating(null);
        }

        public void SetRating(int? rating, bool provisional = false)
        {
            background.Colour = new Color4(0.08f, 0.08f, 0.11f, 0.92f);

            if (provisional && rating != null)
            {
                // en placement: el rating existe (sembrado por el pick) pero el tier real se GANA
                // jugando. mostramos "Provisional" en gris, no un tier alto sin haber jugado.
                var grey = new Color4(0.62f, 0.64f, 0.7f, 1f);
                icon.Icon = FontAwesome.Solid.HourglassHalf;
                icon.Colour = grey;
                tierText.Text = "Provisional";
                tierText.Colour = grey;
                ratingText.Text = "in placement";
                accent.Colour = grey;
                BorderColour = grey.Opacity(0.55f);
                return;
            }

            var tier = RankedPlayRankTier.FromRating(rating);

            icon.Icon = tier.Icon;
            icon.Colour = tier.Colour.Lighten(0.2f);

            tierText.Text = tier.DisplayName;
            tierText.Colour = tier.Colour.Lighten(0.55f);

            ratingText.Text = rating != null ? $"{rating:N0} MMR" : "no games yet";

            // dark-glass uniforme + tinte y borde del tier: pilula consistente, sin cuadrados internos.
            accent.Colour = tier.Colour;
            BorderColour = tier.Colour.Opacity(0.75f);
        }
    }
}
