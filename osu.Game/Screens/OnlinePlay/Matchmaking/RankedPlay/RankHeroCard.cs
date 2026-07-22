// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay
{
    /// <summary>
    /// torii: showcase del rank del jugador para la cola. Reemplaza la vieja curva de distribucion
    /// (densa y poco util) por un crest grande del tier + barra de progreso al siguiente tier + un
    /// "mejor que X%" chico como unica data que importa. Entra con animacion; en placement muestra
    /// un estado provisional sin barra (el tier todavia no esta ganado).
    /// </summary>
    public partial class RankHeroCard : CompositeDrawable
    {
        private Container crest = null!;
        private Circle crestFill = null!;
        private SpriteIcon crestIcon = null!;
        private OsuSpriteText tierText = null!;
        private OsuSpriteText mmrText = null!;

        private Container progressRow = null!;
        private Box progressFill = null!;
        private OsuSpriteText currentTierLabel = null!;
        private OsuSpriteText nextTierLabel = null!;
        private OsuSpriteText percentText = null!;

        private bool hasAppeared;
        private Color4 glowColour = Color4.White;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = new FillFlowContainer
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 10),
                Children = new Drawable[]
                {
                    crest = new Container
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Size = new Vector2(78),
                        // Masking SIEMPRE (requisito de EdgeEffect) + circular. Margen abajo para que
                        // el glow no se meta arriba del texto "Provisional"/tier de abajo.
                        Masking = true,
                        CornerRadius = 39,
                        Margin = new MarginPadding { Bottom = 8 },
                        Children = new Drawable[]
                        {
                            crestFill = new Circle
                            {
                                RelativeSizeAxes = Axes.Both,
                            },
                            crestIcon = new SpriteIcon
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Size = new Vector2(38),
                            },
                        }
                    },
                    tierText = new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = OsuFont.Torus.With(size: 34, weight: FontWeight.Bold),
                        UseFullGlyphHeight = false,
                    },
                    mmrText = new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = OsuFont.GetFont(size: 15, weight: FontWeight.SemiBold),
                        Colour = new Color4(0.68f, 0.68f, 0.74f, 1f),
                        UseFullGlyphHeight = false,
                    },
                    progressRow = new Container
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        RelativeSizeAxes = Axes.X,
                        Height = 30,
                        Padding = new MarginPadding { Horizontal = 24, Top = 8 },
                        Children = new Drawable[]
                        {
                            new Container
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                RelativeSizeAxes = Axes.X,
                                Height = 8,
                                Masking = true,
                                CornerRadius = 4,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = new Color4(1f, 1f, 1f, 0.12f),
                                    },
                                    progressFill = new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Width = 0f,
                                    },
                                }
                            },
                            currentTierLabel = new OsuSpriteText
                            {
                                Anchor = Anchor.BottomLeft,
                                Origin = Anchor.BottomLeft,
                                Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                                Colour = new Color4(0.6f, 0.6f, 0.66f, 1f),
                                UseFullGlyphHeight = false,
                            },
                            nextTierLabel = new OsuSpriteText
                            {
                                Anchor = Anchor.BottomRight,
                                Origin = Anchor.BottomRight,
                                Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                                Colour = new Color4(0.6f, 0.6f, 0.66f, 1f),
                                UseFullGlyphHeight = false,
                            },
                        }
                    },
                    percentText = new OsuSpriteText
                    {
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                        Colour = new Color4(0.6f, 0.6f, 0.66f, 1f),
                        UseFullGlyphHeight = false,
                    },
                }
            };
        }

        /// <param name="rating">Mu actual, o null si no hay dato / sin partidas.</param>
        /// <param name="provisional">Si sigue en placement (tier no final todavia).</param>
        /// <param name="betterThanFraction">Fraccion 0..1 de jugadores por debajo del jugador, o null.</param>
        public void SetData(int? rating, bool provisional = false, double? betterThanFraction = null)
        {
            var tier = RankedPlayRankTier.FromRating(rating);

            if (provisional && rating != null)
            {
                var grey = new Color4(0.62f, 0.64f, 0.7f, 1f);
                crestFill.Colour = new Color4(0.16f, 0.16f, 0.2f, 1f);
                crestIcon.Icon = FontAwesome.Solid.HourglassHalf;
                crestIcon.Colour = grey;
                crest.BorderThickness = 0;
                tierText.Text = "Provisional";
                tierText.Colour = grey;
                mmrText.Text = "in placement";
                progressRow.Hide();
                percentText.Text = "play placement games to lock your rank";
                percentText.Colour = grey.Opacity(0.9f);
                glowColour = grey;
                appear();
                return;
            }

            crestFill.Colour = tier.Colour.Darken(0.2f);
            crestIcon.Icon = tier.Icon;
            crestIcon.Colour = Color4.White;

            crest.Masking = true;
            crest.CornerRadius = 39;
            crest.BorderThickness = 3;
            crest.BorderColour = tier.Colour.Lighten(0.3f);

            tierText.Text = tier.DisplayName;
            tierText.Colour = tier.Colour.Lighten(0.35f);
            glowColour = tier.Colour.Lighten(0.35f);

            mmrText.Text = rating != null ? $"{rating:N0} MMR" : "no games yet";
            mmrText.Colour = new Color4(0.68f, 0.68f, 0.74f, 1f);

            // barra de progreso al siguiente tier. Master es open-ended y Unranked no tiene barra.
            var next = tier.Next();
            bool showBar = rating != null && next.TierOrder > tier.TierOrder;

            if (showBar)
            {
                progressRow.Show();
                progressFill.Colour = tier.Colour;
                currentTierLabel.Text = tier.Name;
                currentTierLabel.Colour = tier.Colour.Opacity(0.85f);
                nextTierLabel.Text = next.Name;
                nextTierLabel.Colour = next.Colour.Opacity(0.85f);

                float progress = RankedPlayRankTier.ProgressWithinTier(rating);
                progressFill.ResizeWidthTo(progress, hasAppeared ? 500 : 900, Easing.OutQuint);
            }
            else if (rating != null && tier.Name == "Master")
            {
                // Master: sin siguiente, mostramos la barra llena como "techo".
                progressRow.Show();
                progressFill.Colour = tier.Colour;
                currentTierLabel.Text = "Master";
                currentTierLabel.Colour = tier.Colour.Opacity(0.85f);
                nextTierLabel.Text = "max";
                nextTierLabel.Colour = tier.Colour.Opacity(0.85f);
                progressFill.ResizeWidthTo(1f, hasAppeared ? 500 : 900, Easing.OutQuint);
            }
            else
            {
                progressRow.Hide();
            }

            if (betterThanFraction != null && rating != null)
            {
                percentText.Text = $"better than {betterThanFraction.Value:P0} of players";
                percentText.Colour = new Color4(0.6f, 0.6f, 0.66f, 1f);
            }
            else
            {
                percentText.Text = string.Empty;
            }

            appear();
        }

        private void appear()
        {
            if (hasAppeared)
                return;

            hasAppeared = true;

            this.FadeInFromZero(400, Easing.OutQuint);
            crest.ScaleTo(0.6f).ScaleTo(1f, 650, Easing.OutElastic);

            crest.EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = glowColour.Opacity(0f),
                Radius = 16,
            };
            crest.TweenEdgeEffectTo(new EdgeEffectParameters
            {
                Type = EdgeEffectType.Glow,
                Colour = glowColour.Opacity(0.4f),
                Radius = 16,
            }, 500, Easing.OutQuint);
        }

        public void Clear()
        {
            hasAppeared = false;
            progressFill.Width = 0f;
            this.FadeOut(150);
        }
    }
}
