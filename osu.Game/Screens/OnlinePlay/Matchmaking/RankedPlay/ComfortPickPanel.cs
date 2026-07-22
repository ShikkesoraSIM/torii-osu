// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay
{
    /// <summary>
    /// torii: gate del star-rating pick de ranked play. Una vez por season el jugador elige su
    /// dificultad comoda (con un piso anti-sandbag). Mientras no eligio, esto se muestra en vez del
    /// boton de queue; al elegir (o si ya eligio) llama <see cref="OnReady"/> para desbloquear.
    /// El pick siembra el MMR al promedio de esa banda de SR; subir MMR sube la dificultad sola.
    /// </summary>
    public partial class ComfortPickPanel : CompositeDrawable
    {
        /// <summary>Se llama cuando el pick esta hecho (recien elegido o ya existia esta season).</summary>
        public Action? OnReady { get; set; }

        private readonly int rulesetId;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        private Container content = null!;

        private readonly BindableNumber<double> pick = new BindableNumber<double>(1)
        {
            MinValue = 1,
            MaxValue = 12,
            Precision = 0.1,
        };

        private readonly Bindable<StarDifficulty> displayDifficulty = new Bindable<StarDifficulty>();

        private Box glow = null!;
        private RoundedButton confirmButton = null!;
        private OsuSpriteText subtitleText = null!;
        private bool submitting;

        public ComfortPickPanel(int rulesetId)
        {
            this.rulesetId = rulesetId;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChild = content = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Child = new LoadingSpinner(true)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    State = { Value = Visibility.Visible },
                }
            };

            fetchFloor();
        }

        private void fetchFloor()
        {
            var req = new GetComfortPickFloorRequest(rulesetId);
            req.Success += response => Schedule(() => onFloor(response));
            req.Failure += _ => Schedule(() =>
            {
                // sin backend / error: no bloqueamos la cola (mejor dejar jugar que trabar todo).
                OnReady?.Invoke();
            });
            api.Queue(req);
        }

        private void onFloor(APIComfortPickFloor response)
        {
            if (response.AlreadyPicked)
            {
                // ya eligio esta season -> desbloquear directo.
                OnReady?.Invoke();
                return;
            }

            double floor = Math.Max(1, response.Floor);
            double max = Math.Max(response.PickMax, floor + 1);

            pick.MinValue = floor;
            pick.MaxValue = max;
            pick.Value = floor;

            content.Child = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                CornerRadius = 16,
                Masking = true,
                BorderThickness = 1.5f,
                BorderColour = new Color4(1f, 1f, 1f, 0.12f),
                EdgeEffect = new osu.Framework.Graphics.Effects.EdgeEffectParameters
                {
                    Type = osu.Framework.Graphics.Effects.EdgeEffectType.Shadow,
                    Colour = Color4.Black.Opacity(0.4f),
                    Radius = 18,
                },
                Children = new Drawable[]
                {
                    new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(0.05f, 0.06f, 0.09f, 0.96f) },
                    // glow superior coloreado por la dificultad elegida (se actualiza con el slider).
                    glow = new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 150,
                        Blending = BlendingParameters.Additive,
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Horizontal = 30, Vertical = 26 },
                        Spacing = new Vector2(0, 6),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "Pick your comfortable difficulty",
                                Font = OsuFont.TorusAlternate.With(size: 28, weight: FontWeight.SemiBold),
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "once per season · sets which maps you play",
                                Font = OsuFont.Torus.With(size: 13),
                                Colour = new Color4(0.66f, 0.66f, 0.72f, 1f),
                                Margin = new MarginPadding { Bottom = 14 },
                            },
                            new StarRatingDisplay(default, StarRatingDisplaySize.Range, animated: true)
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Scale = new Vector2(1.5f),
                                Current = { BindTarget = displayDifficulty },
                                Margin = new MarginPadding { Bottom = 20 },
                            },
                            new Container
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                RelativeSizeAxes = Axes.X,
                                Width = 0.85f,
                                AutoSizeAxes = Axes.Y,
                                // margen abajo para que el bubble del valor no pise los labels/subtitle.
                                Margin = new MarginPadding { Bottom = 16 },
                                Child = new RoundedSliderBar<double>
                                {
                                    RelativeSizeAxes = Axes.X,
                                    Current = pick,
                                    KeyboardStep = 0.5f,
                                }
                            },
                            new Container
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                RelativeSizeAxes = Axes.X,
                                Width = 0.85f,
                                Height = 18,
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Text = $"floor {floor:0.#}★",
                                        Font = OsuFont.Torus.With(size: 12, weight: FontWeight.SemiBold),
                                        Colour = new Color4(0.6f, 0.6f, 0.66f, 1f),
                                    },
                                    new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreRight,
                                        Origin = Anchor.CentreRight,
                                        Text = $"{max:0.#}★",
                                        Font = OsuFont.Torus.With(size: 12, weight: FontWeight.SemiBold),
                                        Colour = new Color4(0.6f, 0.6f, 0.66f, 1f),
                                    },
                                }
                            },
                            subtitleText = new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = "your floor is set from your best plays, you can't sandbag below it",
                                Font = OsuFont.Torus.With(size: 12),
                                Colour = new Color4(0.55f, 0.55f, 0.6f, 1f),
                                Margin = new MarginPadding { Top = 8, Bottom = 4 },
                            },
                            confirmButton = new RoundedButton
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Width = 240,
                                Height = 44,
                                Text = "Confirm",
                                Action = submit,
                                Margin = new MarginPadding { Top = 8 },
                            },
                        }
                    }
                }
            };

            pick.BindValueChanged(v => onValueChanged(v.NewValue), true);
        }

        private void onValueChanged(double value)
        {
            displayDifficulty.Value = new StarDifficulty(value, 0);

            var diff = colours.ForStarDifficulty(value);
            glow.Colour = ColourInfo.GradientVertical(diff.Opacity(0.32f), diff.Opacity(0f));

            if (confirmButton != null)
                confirmButton.BackgroundColour = diff.Darken(0.2f);
        }

        private void submit()
        {
            if (submitting)
                return;

            submitting = true;
            confirmButton.Enabled.Value = false;
            subtitleText.Text = "saving...";

            var req = new SetComfortPickRequest(rulesetId, (float)pick.Value);
            req.Success += () => Schedule(() => OnReady?.Invoke());
            req.Failure += e => Schedule(() =>
            {
                submitting = false;
                confirmButton.Enabled.Value = true;
                // el server rechaza < piso (422) o si ya elegiste (409); re-fetcheamos el estado.
                subtitleText.Text = e.Message;
                fetchFloor();
            });
            api.Queue(req);
        }
    }
}
