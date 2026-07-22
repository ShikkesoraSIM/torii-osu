// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Localisation;
using osu.Game.Online;
using osu.Game.Online.Chat;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osuTK.Graphics;

namespace osu.Game.Screens.Select
{
    public partial class BeatmapTitleWedge
    {
        public partial class DifficultyDisplay : CompositeDrawable
        {
            private const float border_weight = 2;

            [Resolved]
            private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

            [Resolved]
            private IBindable<RulesetInfo> ruleset { get; set; } = null!;

            [Resolved]
            private IBindable<IReadOnlyList<Mod>> mods { get; set; } = null!;

            [Resolved]
            private BeatmapDifficultyCache difficultyCache { get; set; } = null!;

            private ModSettingChangeTracker? settingChangeTracker;

            private StarRatingDisplay starRatingDisplay = null!;
            private FillFlowContainer nameLine = null!;
            private OsuSpriteText difficultyText = null!;
            private OsuSpriteText mappedByText = null!;
            private OsuHoverContainer mapperLink = null!;
            private OsuSpriteText mapperText = null!;

            private GridContainer ratingAndNameContainer = null!;
            private DifficultyStatisticsDisplay countStatisticsDisplay = null!;
            private DifficultyStatisticsDisplay difficultyStatisticsDisplay = null!;

            private Box statisticsBackground = null!;
            private IDisposable? statisticsBackgroundThemeBinding;

            private CancellationTokenSource? cancellationSource;

            public DifficultyDisplay()
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                Masking = true;
                CornerRadius = 10;
                Shear = OsuGame.SHEAR;

                InternalChildren = new Drawable[]
                {
                    new WedgeBackground { Glass = true }, // torii dark glass
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Children = new Drawable[]
                        {
                            new ShearAligningWrapper(ratingAndNameContainer = new GridContainer
                            {
                                Shear = -OsuGame.SHEAR,
                                AlwaysPresent = true,
                                RelativeSizeAxes = Axes.X,
                                Height = 20,
                                Margin = new MarginPadding { Vertical = 5f },
                                Padding = new MarginPadding { Left = SongSelect.WEDGE_CONTENT_MARGIN },
                                RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                                ColumnDimensions = new[]
                                {
                                    new Dimension(GridSizeMode.AutoSize),
                                    new Dimension(GridSizeMode.Absolute, 6),
                                    new Dimension(),
                                },
                                Content = new[]
                                {
                                    new[]
                                    {
                                        starRatingDisplay = new StarRatingDisplay(default, animated: true)
                                        {
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                        },
                                        Empty(),
                                        nameLine = new FillFlowContainer
                                        {
                                            Anchor = Anchor.CentreLeft,
                                            Origin = Anchor.CentreLeft,
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Direction = FillDirection.Horizontal,
                                            Margin = new MarginPadding { Bottom = 2f },
                                            Children = new Drawable[]
                                            {
                                                difficultyText = new TruncatingSpriteText
                                                {
                                                    Anchor = Anchor.BottomLeft,
                                                    Origin = Anchor.BottomLeft,
                                                    Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold),
                                                },
                                                mappedByText = new OsuSpriteText
                                                {
                                                    Anchor = Anchor.BottomLeft,
                                                    Origin = Anchor.BottomLeft,
                                                    Text = " mapped by ",
                                                    Font = OsuFont.Style.Body,
                                                },
                                                mapperLink = new MapperLinkContainer
                                                {
                                                    AutoSizeAxes = Axes.Both,
                                                    Anchor = Anchor.BottomLeft,
                                                    Origin = Anchor.BottomLeft,
                                                    Child = mapperText = new TruncatingSpriteText
                                                    {
                                                        Shadow = true,
                                                        Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold),
                                                    },
                                                },
                                            },
                                        },
                                    }
                                },
                            }),
                            new ShearAligningWrapper(new Container
                            {
                                Shear = -OsuGame.SHEAR,
                                RelativeSizeAxes = Axes.X,
                                Height = 53,
                                Padding = new MarginPadding { Bottom = border_weight, Right = border_weight },
                                Child = new Container
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Masking = true,
                                    CornerRadius = 10 - border_weight,
                                    Shear = OsuGame.SHEAR,
                                    Children = new Drawable[]
                                    {
                                        statisticsBackground = new Box
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                        },
                                        new GridContainer
                                        {
                                            RelativeSizeAxes = Axes.X,
                                            AutoSizeAxes = Axes.Y,
                                            Padding = new MarginPadding { Left = SongSelect.WEDGE_CONTENT_MARGIN, Right = 20f, Vertical = 7.5f },
                                            Shear = -OsuGame.SHEAR,
                                            RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize) },
                                            ColumnDimensions = new[]
                                            {
                                                new Dimension(),
                                                new Dimension(GridSizeMode.Absolute, 30),
                                                new Dimension(GridSizeMode.AutoSize),
                                            },
                                            Content = new[]
                                            {
                                                new[]
                                                {
                                                    countStatisticsDisplay = new DifficultyStatisticsDisplay
                                                    {
                                                        RelativeSizeAxes = Axes.X,
                                                    },
                                                    Empty(),
                                                    difficultyStatisticsDisplay = new DifficultyStatisticsDisplay(autoSize: true),
                                                }
                                            },
                                        }
                                    },
                                }
                            }),
                        }
                    },
                };

                statisticsBackgroundThemeBinding = statisticsBackground.BindThemeColour(colourProvider, p => p.Background5.Opacity(0.8f));
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                // it is not uncommon for the beatmap and the ruleset to change in conjunction during a single update frame.
                // in that process, it is possible for the global bindable triad (beatmap / ruleset / mods) to briefly be partially invalid in combination (e.g. mods invalid for given ruleset).
                // `updateDisplay()` will initiate a difficulty calculation, and if it is allowed to run in that invalid intermediate state, it will loudly fail.
                // therefore, all changes that may initiate a difficulty calculation are debounced until the next frame to ensure the global bindable state is fully consistent -
                // and it's what you'd want to do anyway for performance reasons.
                beatmap.BindValueChanged(_ => Scheduler.AddOnce(updateDisplay));
                ruleset.BindValueChanged(_ => Scheduler.AddOnce(updateDisplay));

                mods.BindValueChanged(m =>
                {
                    settingChangeTracker?.Dispose();

                    updateDifficultyStatistics();

                    if (m.NewValue.Any())
                    {
                        settingChangeTracker = new ModSettingChangeTracker(m.NewValue);
                        settingChangeTracker.SettingChanged += _ => updateDifficultyStatistics();
                    }
                }, true);

                updateDisplay();
            }

            [Resolved]
            private ILinkHandler? linkHandler { get; set; }

            private void updateDisplay()
            {
                cancellationSource?.Cancel();
                cancellationSource = new CancellationTokenSource();

                if (beatmap.IsDefault)
                {
                    ratingAndNameContainer.FadeOut(300, Easing.OutQuint);
                    countStatisticsDisplay.FadeOut(300, Easing.OutQuint);
                }
                else
                {
                    ratingAndNameContainer.FadeIn(300, Easing.OutQuint);
                    difficultyText.Text = beatmap.Value.BeatmapInfo.DifficultyName;
                    mapperLink.Action = () => linkHandler?.HandleLink(new LinkDetails(LinkAction.OpenUserProfile, beatmap.Value.Metadata.Author));
                    mapperText.Text = beatmap.Value.Metadata.Author.Username;
                }

                // torii: el wedge del mapa seleccionado SI calcula mod-aware via difficultyCache (es uno
                // solo, debounced + cacheado). arranca en el SR guardado al instante y lo refina con los mods.
                // las filas del carousel quedan en el SR guardado (nomod) para no recalcular por panel.
                starRatingDisplay.Current = (Bindable<StarDifficulty>)difficultyCache.GetBindableDifficulty(beatmap.Value.BeatmapInfo, cancellationSource.Token, SongSelect.DIFFICULTY_CALCULATION_DEBOUNCE);

                updateCountStatistics();
                updateDifficultyStatistics();
            }

            private void updateCountStatistics()
            {
                if (beatmap.IsDefault)
                {
                    countStatisticsDisplay.FadeOut(300, Easing.OutQuint);
                    return;
                }

                var statistics = getCountStatisticsFromMetadata(beatmap.Value.BeatmapInfo);

                if (statistics.Count == 0)
                {
                    countStatisticsDisplay.FadeOut(300, Easing.OutQuint);
                    return;
                }

                countStatisticsDisplay.FadeIn(200, Easing.OutQuint);
                countStatisticsDisplay.Statistics = statistics;
            }

            private static IReadOnlyList<StatisticDifficulty.Data> getCountStatisticsFromMetadata(BeatmapInfo beatmapInfo)
            {
                int total = beatmapInfo.TotalObjectCount;

                if (total < 0)
                    return Array.Empty<StatisticDifficulty.Data>();

                // OnlineInfo no esta poblado fuera del import, asi que los conteos salen de la metadata
                // guardada: objetos sin duracion vs objetos con duracion (sliders/spinners, hold notes, etc).
                // etiquetamos por el ruleset NATIVO del mapa (no el seleccionado) para no mal-etiquetar converts.
                int duration = Math.Max(beatmapInfo.EndTimeObjectCount, 0);
                int regular = Math.Max(total - duration, 0);
                int sum = Math.Max(1, total);

                var labels = getCountLabels(beatmapInfo.Ruleset.ShortName);

                if (duration <= 0)
                    return new[] { createStatistic(labels.Regular, total, 1) };

                return new[]
                {
                    createStatistic(labels.Regular, regular, regular / (float)sum),
                    createStatistic(labels.Duration, duration, duration / (float)sum),
                };
            }

            private static (LocalisableString Regular, LocalisableString Duration) getCountLabels(string nativeRuleset)
            {
                switch (nativeRuleset)
                {
                    case @"taiko":
                        return (BeatmapStatisticStrings.Hits, BeatmapStatisticStrings.Drumrolls);

                    case @"fruits":
                    case @"catch":
                        return (BeatmapStatisticStrings.Fruits, BeatmapStatisticStrings.JuiceStreams);

                    case @"mania":
                        return (BeatmapStatisticStrings.Notes, BeatmapStatisticStrings.HoldNotes);

                    default:
                        return (BeatmapStatisticStrings.Circles, BeatmapStatisticStrings.Sliders);
                }
            }

            private static StatisticDifficulty.Data createStatistic(LocalisableString label, int count, float barLength)
            {
                return new StatisticDifficulty.Data(label, barLength, barLength, 1, count.ToString());
            }

            private void updateDifficultyStatistics() => Scheduler.AddOnce(() =>
            {
                if (beatmap.IsDefault || ruleset.Value == null)
                {
                    difficultyStatisticsDisplay.Statistics = Array.Empty<StatisticDifficulty.Data>();
                    return;
                }

                Ruleset rulesetInstance = ruleset.Value.CreateInstance();

                var displayAttributes = rulesetInstance.GetBeatmapAttributesForDisplay(beatmap.Value.BeatmapInfo, mods.Value).ToList();
                difficultyStatisticsDisplay.Statistics = displayAttributes.Select(a => new StatisticDifficulty.Data(a)).ToList();
            });

            protected override void Update()
            {
                base.Update();

                difficultyText.MaxWidth = Math.Max(nameLine.DrawWidth - mappedByText.DrawWidth - mapperText.DrawWidth - 20, 0);

                // Use difficulty colour until it gets too dark to be visible against dark backgrounds.
                Color4 col = starRatingDisplay.DisplayedStars.Value >= OsuColour.STAR_DIFFICULTY_DEFINED_COLOUR_CUTOFF ? starRatingDisplay.DisplayedDifficultyTextColour : starRatingDisplay.DisplayedDifficultyColour;

                difficultyText.Colour = col;
                mappedByText.Colour = col;
                countStatisticsDisplay.AccentColour = col;
                difficultyStatisticsDisplay.AccentColour = col;
            }

            protected override void Dispose(bool isDisposing)
            {
                statisticsBackgroundThemeBinding?.Dispose();
                statisticsBackgroundThemeBinding = null;
                base.Dispose(isDisposing);
            }

            private partial class MapperLinkContainer : OsuHoverContainer
            {
                private OverlayColourProvider? overlayColourProvider;

                [BackgroundDependencyLoader]
                private void load(OverlayColourProvider? overlayColourProvider, OsuColour colours)
                {
                    this.overlayColourProvider = overlayColourProvider;

                    TooltipText = ContextMenuStrings.ViewProfile;
                    IdleColour = overlayColourProvider?.Light2 ?? colours.Blue;
                }

                protected override void LoadComplete()
                {
                    base.LoadComplete();

                    if (overlayColourProvider != null)
                        overlayColourProvider.ColoursChanged += onColoursChanged;
                }

                private void onColoursChanged() => IdleColour = overlayColourProvider!.Light2;

                protected override void Dispose(bool isDisposing)
                {
                    if (overlayColourProvider != null)
                        overlayColourProvider.ColoursChanged -= onColoursChanged;
                    base.Dispose(isDisposing);
                }
            }
        }
    }
}
