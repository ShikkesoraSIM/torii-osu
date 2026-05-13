// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Rulesets;
using osu.Game.Scoring;
using osu.Game.Screens.Select.Filter;
using osu.Game.Screens.Select.Leaderboards;
using osu.Game.Screens.SelectV2;
using osu.Game.Screens.Footer;
using osu.Game.Screens.Menu;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;
using Realms;

namespace osu.Game.Screens.StableSelect;

public partial class StableSoloSongSelect : osu.Game.Screens.SelectV2.SoloSongSelect
{
    public StableSoloSongSelect()
    {
        ShowOsuLogo = false;
    }

    private enum StableInfoTab
    {
        Details,
        Ranking
    }

    private StableSongSelectShell stableShell = null!;

    public override bool ShowFooter => false;

    public override IReadOnlyList<ScreenFooterButton> CreateFooterButtons() => Array.Empty<ScreenFooterButton>();

    protected override BeatmapCarousel CreateBeatmapCarousel() => new StableBeatmapCarousel
    {
        BleedTop = 54,
        BleedBottom = 24,
        RelativeSizeAxes = Axes.Both,
        RequestPresentBeatmap = RequestPresentBeatmap,
        RequestSelection = RequestCarouselSelection,
        RequestRecommendedSelection = RequestRecommendedCarouselSelection,
        NewItemsPresented = OnCarouselItemsPresented,
    };

    protected override void LoadComplete()
    {
        base.LoadComplete();

        MainContentContainer.Padding = new MarginPadding();
        Carousel.Y = -112;

        MainContentContainer.Add(stableShell = new StableSongSelectShell(FilterControl, Carousel, this)
        {
            RelativeSizeAxes = Axes.Both,
        });

        Beatmap.BindValueChanged(v => stableShell.UpdateBeatmap(v.NewValue), true);
        forceStableShell();
        Footer?.Hide();
        Footer?.StopTrackingLogo();
    }

    public override void OnEntering(ScreenTransitionEvent e)
    {
        base.OnEntering(e);
        forceStableShell();
        Footer?.Hide();
        Footer?.StopTrackingLogo();
    }

    public override void OnResuming(ScreenTransitionEvent e)
    {
        base.OnResuming(e);
        forceStableShell();
        Footer?.Hide();
        Footer?.StopTrackingLogo();
    }

    protected override void Update()
    {
        base.Update();

        if (!Carousel.VisuallyFocusSelected || TitleWedge.Alpha > 0 || DetailsArea.Alpha > 0 || FilterControl.Alpha > 0)
            forceStableShell();

        Footer?.Hide();
        Footer?.StopTrackingLogo();
    }

    protected override void LogoArriving(OsuLogo logo, bool resuming)
    {
        base.LogoArriving(logo, resuming);
        Footer?.StopTrackingLogo();
        logo.Hide();
        Footer?.Hide();
    }

    protected override void LogoSuspending(OsuLogo logo)
    {
        base.LogoSuspending(logo);
        Footer?.StopTrackingLogo();
        logo.Hide();
        Footer?.Hide();
    }

    protected override void LogoExiting(OsuLogo logo)
    {
        base.LogoExiting(logo);
        Footer?.StopTrackingLogo();
        logo.Hide();
        Footer?.Hide();
    }

    internal void OpenMods() => ToggleModSelectOverlayVisibility();

    private void forceStableShell()
    {
        Carousel.VisuallyFocusSelected = false;
        TitleWedge.Hide();
        DetailsArea.Hide();
        FilterControl.Hide();
    }

    private partial class StableSongSelectShell : CompositeDrawable
    {
        private readonly FilterControl backingFilter;
        private readonly BeatmapCarousel carousel;
        private readonly StableSoloSongSelect songSelect;

        private StableHeaderSearchTextBox searchBox = null!;
        private StableInfoPanel infoPanel = null!;
        private OsuSpriteText matchesText = null!;

        private IBindable<SortMode> sortMode = null!;
        private IBindable<GroupMode> groupMode = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        public StableSongSelectShell(FilterControl backingFilter, BeatmapCarousel carousel, StableSoloSongSelect songSelect)
        {
            this.backingFilter = backingFilter;
            this.carousel = carousel;
            this.songSelect = songSelect;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            sortMode = config.GetBindable<SortMode>(OsuSetting.SongSelectSortingMode);
            groupMode = config.GetBindable<GroupMode>(OsuSetting.SongSelectGroupMode);

            InternalChild = new DrawSizePreservingFillContainer
            {
                RelativeSizeAxes = Axes.Both,
                TargetDrawSize = new Vector2(1366, 768),
                Strategy = DrawSizePreservationStrategy.Minimum,
                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        infoPanel = new StableInfoPanel
                        {
                            Position = new Vector2(18, 28),
                        },
                        createTopRightControls(),
                        createBottomButtons(),
                    }
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            searchBox.Current.BindValueChanged(v => backingFilter.Search(v.NewValue), true);
        }

        protected override void Update()
        {
            base.Update();
            matchesText.Text = $"{carousel.MatchedBeatmapsCount} matches";
        }

        public void UpdateBeatmap(WorkingBeatmap workingBeatmap) => infoPanel.Beatmap = workingBeatmap;

        private Drawable createTopRightControls()
        {
            var sortDropdown = new StableDropdown<SortMode>
            {
                RelativeSizeAxes = Axes.X,
                Current = { BindTarget = sortMode },
            };
            sortDropdown.Items = Enum.GetValues<SortMode>();

            var groupDropdown = new StableDropdown<GroupMode>
            {
                RelativeSizeAxes = Axes.X,
                Current = { BindTarget = groupMode },
            };
            groupDropdown.Items = Enum.GetValues<GroupMode>();

            return new Container
            {
                Anchor = Anchor.TopRight,
                Origin = Anchor.TopRight,
                Position = new Vector2(-26, 34),
                Size = new Vector2(454, 140),
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 24,
                        EdgeEffect = new EdgeEffectParameters
                        {
                            Type = EdgeEffectType.Glow,
                            Radius = 18,
                            Colour = colourProvider.Highlight1.Opacity(0.16f),
                        },
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = new Color4(23, 20, 34, 214),
                            },
                            new Box
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 2,
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Colour = colourProvider.Highlight1.Opacity(0.85f),
                            },
                        }
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Padding = new MarginPadding { Top = 14, Left = 18, Right = 18, Bottom = 15 },
                        Spacing = new Vector2(12),
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Text = "search...",
                                        Font = OsuFont.GetFont(size: 24, weight: FontWeight.Bold),
                                        Colour = new Color4(230, 233, 255, 255),
                                    },
                                    matchesText = new OsuSpriteText
                                    {
                                        Text = "0 matches",
                                        Font = OsuFont.GetFont(size: 15, weight: FontWeight.SemiBold),
                                        Colour = new Color4(219, 190, 78, 255),
                                        Margin = new MarginPadding { Left = 14, Top = 8 },
                                    },
                                }
                            },
                            searchBox = new StableHeaderSearchTextBox
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 44,
                                PlaceholderText = "type to search",
                            },
                            new GridContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                ColumnDimensions = new[]
                                {
                                    new Dimension(GridSizeMode.Absolute, 50),
                                    new Dimension(GridSizeMode.Absolute, 10),
                                    new Dimension(),
                                    new Dimension(GridSizeMode.Absolute, 16),
                                    new Dimension(GridSizeMode.Absolute, 58),
                                    new Dimension(GridSizeMode.Absolute, 10),
                                    new Dimension(),
                                },
                                Content = new[]
                                {
                                    new Drawable[]
                                    {
                                        new StableLabel("Sort"),
                                        new Container(),
                                        sortDropdown,
                                        new Container(),
                                        new StableLabel("Group"),
                                        new Container(),
                                        groupDropdown,
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        private Drawable createBottomButtons()
        {
            return new FillFlowContainer
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Position = new Vector2(28, -12),
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(10),
                Children = new Drawable[]
                {
                    new StableActionButton("Back", "Esc", colourProvider.Highlight1, () => songSelect.Exit(), emphasised: true)
                    {
                        Width = 168,
                    },
                    new StableActionButton("Mods", "F1", new Color4(149, 224, 98, 255), songSelect.OpenMods)
                    {
                        Width = 116,
                    },
                    new StableActionButton("Random", "F2", new Color4(106, 184, 255, 255), () => carousel.NextRandom())
                    {
                        Width = 116,
                    }
                }
            };
        }
    }

    private partial class StableInfoPanel : CompositeDrawable
    {
        private readonly Bindable<StableInfoTab> currentTab = new Bindable<StableInfoTab>(StableInfoTab.Details);
        private readonly Bindable<BeatmapLeaderboardScope> leaderboardScope = new Bindable<BeatmapLeaderboardScope>(BeatmapLeaderboardScope.Global);
        private readonly Bindable<LeaderboardSortMode> leaderboardSort = new Bindable<LeaderboardSortMode>(LeaderboardSortMode.Score);

        private WorkingBeatmap? beatmap;

        private PanelSetBackground background = null!;
        private BeatmapSetOnlineStatusPill statusPill = null!;
        private StableLegacyTopRankBadge localRankBadge = null!;
        private OsuSpriteText titleText = null!;
        private OsuSpriteText artistText = null!;
        private OsuSpriteText mapperText = null!;
        private OsuSpriteText difficultyText = null!;
        private OsuSpriteText lengthText = null!;
        private OsuSpriteText bpmText = null!;
        private StableStatTile circlesTile = null!;
        private StableStatTile slidersTile = null!;
        private StableStatTile csTile = null!;
        private StableStatTile arTile = null!;
        private StableStatTile odTile = null!;
        private StableStatTile hpTile = null!;
        private StableStatTile starsTile = null!;
        private Container detailsContent = null!;
        private Container rankingContent = null!;
        private StableLeaderboardPanel leaderboard = null!;
        private TruncatingSpriteText sourceValueText = null!;
        private TruncatingSpriteText tagsValueText = null!;

        public WorkingBeatmap? Beatmap
        {
            get => beatmap;
            set
            {
                beatmap = value;

                if (IsLoaded)
                    updateDisplay();
            }
        }

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Size = new Vector2(608, 500);

            InternalChildren = new Drawable[]
            {
                new Container
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 208,
                    Masking = true,
                    CornerRadius = 26,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Radius = 22,
                        Colour = colourProvider.Highlight1.Opacity(0.18f),
                    },
                    Children = new Drawable[]
                    {
                        background = new PanelSetBackground
                        {
                            RelativeSizeAxes = Axes.Both,
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0.18f), Color4.Black.Opacity(0.72f)),
                        },
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Width = 0.76f,
                            Colour = ColourInfo.GradientHorizontal(new Color4(16, 22, 41, 205), Color4.Transparent),
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Vertical,
                            Padding = new MarginPadding { Top = 18, Left = 22, Right = 22 },
                            Spacing = new Vector2(5),
                            Children = new Drawable[]
                            {
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Horizontal,
                                    Children = new Drawable[]
                                    {
                                        statusPill = new BeatmapSetOnlineStatusPill
                                        {
                                            Animated = false,
                                            TextSize = 14,
                                        },
                                        new Container { RelativeSizeAxes = Axes.X },
                                        localRankBadge = new StableLegacyTopRankBadge
                                        {
                                            Anchor = Anchor.CentreRight,
                                            Origin = Anchor.CentreRight,
                                            Margin = new MarginPadding { Top = -4 },
                                        }
                                    }
                                },
                                titleText = new OsuSpriteText
                                {
                                    Font = OsuFont.GetFont(size: 32, weight: FontWeight.Bold),
                                    Colour = Color4.White,
                                },
                                artistText = new OsuSpriteText
                                {
                                    Font = OsuFont.GetFont(size: 22, weight: FontWeight.SemiBold),
                                    Colour = new Color4(231, 234, 255, 255),
                                },
                                mapperText = new OsuSpriteText
                                {
                                    Font = OsuFont.GetFont(size: 16, weight: FontWeight.Regular),
                                    Colour = new Color4(201, 206, 232, 255),
                                },
                                new FillFlowContainer
                                {
                                    Direction = FillDirection.Horizontal,
                                    AutoSizeAxes = Axes.Both,
                                    Spacing = new Vector2(16),
                                    Margin = new MarginPadding { Top = 10 },
                                    Children = new Drawable[]
                                    {
                                        lengthText = createMetaText(),
                                        bpmText = createMetaText(),
                                        difficultyText = createMetaText(),
                                    }
                                }
                            }
                        },
                        new FillFlowContainer
                        {
                            Anchor = Anchor.BottomLeft,
                            Origin = Anchor.BottomLeft,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Padding = new MarginPadding { Left = 22, Right = 22, Bottom = 18 },
                            Spacing = new Vector2(16),
                            Children = new Drawable[]
                            {
                                starsTile = new StableStatTile("Stars"),
                                circlesTile = new StableStatTile("Circles"),
                                slidersTile = new StableStatTile("Sliders"),
                                csTile = new StableStatTile("CS"),
                                arTile = new StableStatTile("AR"),
                                odTile = new StableStatTile("OD"),
                                hpTile = new StableStatTile("HP"),
                            }
                        }
                    }
                },
                new Container
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = 274,
                    Masking = true,
                    CornerRadius = 18,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = new Color4(27, 22, 37, 225),
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Padding = new MarginPadding { Left = 12, Top = 10 },
                            Spacing = new Vector2(16),
                            Children = new Drawable[]
                            {
                                new StableTabButton("Details", currentTab, StableInfoTab.Details),
                                new StableTabButton("Ranking", currentTab, StableInfoTab.Ranking),
                            }
                        },
                        detailsContent = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Top = 48, Left = 18, Right = 18, Bottom = 10 },
                            Child = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(3),
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Text = "Source",
                                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                                        Colour = new Color4(255, 234, 120, 255),
                                    },
                                    sourceValueText = new TruncatingSpriteText
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Font = OsuFont.GetFont(size: 17, weight: FontWeight.Bold),
                                        Colour = Color4.White,
                                    },
                                    new Container { Height = 8 },
                                    new OsuSpriteText
                                    {
                                        Text = "Tags",
                                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                                        Colour = new Color4(255, 234, 120, 255),
                                    },
                                    tagsValueText = new TruncatingSpriteText
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        Font = OsuFont.GetFont(size: 14, weight: FontWeight.Regular),
                                        Colour = new Color4(180, 190, 220, 255),
                                    },
                                }
                            }
                        },
                        rankingContent = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Padding = new MarginPadding { Top = 44, Left = 14, Right = 14, Bottom = 10 },
                            Alpha = 0,
                            Children = new Drawable[]
                            {
                                new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Horizontal,
                                    Spacing = new Vector2(12),
                                    Children = new Drawable[]
                                    {
                                        new StableCompactLabel("Scope"),
                                        createScopeDropdown(),
                                        new StableCompactLabel("Sort"),
                                        createSortDropdown(),
                                    }
                                },
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Padding = new MarginPadding { Top = 38 },
                                    Child = leaderboard = new StableLeaderboardPanel
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Scope = { BindTarget = leaderboardScope },
                                        Sorting = { BindTarget = leaderboardSort },
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            currentTab.BindValueChanged(tab =>
            {
                bool isRanking = tab.NewValue == StableInfoTab.Ranking;
                detailsContent.FadeTo(isRanking ? 0 : 1, 140, Easing.OutQuint);
                rankingContent.FadeTo(isRanking ? 1 : 0, 140, Easing.OutQuint);

                if (isRanking)
                    leaderboard.RefreshScores();
            }, true);

            updateDisplay();
        }

        private void updateDisplay()
        {
            if (!IsLoaded)
                return;

            if (beatmap == null)
            {
                background.Beatmap = null;
                localRankBadge.Beatmap = null;
                leaderboard.Beatmap = null;
                statusPill.Status = BeatmapOnlineStatus.None;
                titleText.Text = "stable song select";
                artistText.Text = "Select a difficulty";
                mapperText.Text = string.Empty;
                sourceValueText.Text = string.Empty;
                tagsValueText.Text = string.Empty;
                difficultyText.Text = string.Empty;
                lengthText.Text = string.Empty;
                bpmText.Text = string.Empty;
                setStats("0.00", "0", "0", "-", "-", "-", "-");
                return;
            }

            var info = beatmap.BeatmapInfo;
            var metadata = info.Metadata;
            var difficulty = info.Difficulty;

            background.Beatmap = beatmap;
            localRankBadge.Beatmap = info;
            leaderboard.Beatmap = info;
            statusPill.Status = beatmap.BeatmapSetInfo?.Status ?? BeatmapOnlineStatus.None;
            titleText.Text = metadata.Title;
            artistText.Text = metadata.Artist;
            mapperText.Text = $"mapped by {metadata.Author.Username}";
            sourceValueText.Text = string.IsNullOrEmpty(metadata.Source) ? "(no source)" : metadata.Source;
            tagsValueText.Text = string.IsNullOrEmpty(metadata.Tags) ? "(no tags)" : metadata.Tags;
            difficultyText.Text = info.DifficultyName;
            lengthText.Text = TimeSpan.FromMilliseconds(info.Length).ToString(@"mm\:ss");
            bpmText.Text = $"{Math.Round(info.BPM):0} BPM";

            int circles = Math.Max(0, info.TotalObjectCount - info.EndTimeObjectCount);
            int sliders = Math.Max(0, info.EndTimeObjectCount);

            setStats(
                info.StarRating >= 0 ? info.StarRating.ToString("0.00") : "--",
                circles.ToString(),
                sliders.ToString(),
                difficulty.CircleSize.ToString("0.##"),
                difficulty.ApproachRate.ToString("0.##"),
                difficulty.OverallDifficulty.ToString("0.##"),
                difficulty.DrainRate.ToString("0.##"));
        }

        private StableDropdown<BeatmapLeaderboardScope> createScopeDropdown()
        {
            var dropdown = new StableDropdown<BeatmapLeaderboardScope>
            {
                Width = 150,
                Current = { BindTarget = leaderboardScope },
            };

            dropdown.Items = Enum.GetValues<BeatmapLeaderboardScope>();
            return dropdown;
        }

        private StableDropdown<LeaderboardSortMode> createSortDropdown()
        {
            var dropdown = new StableDropdown<LeaderboardSortMode>
            {
                Width = 150,
                Current = { BindTarget = leaderboardSort },
            };

            dropdown.Items = Enum.GetValues<LeaderboardSortMode>();

            leaderboardScope.BindValueChanged(scope =>
            {
                bool canCustomSort = scope.NewValue == BeatmapLeaderboardScope.Local;
                dropdown.FadeTo(canCustomSort ? 1 : 0.45f, 120, Easing.OutQuint);

                if (!canCustomSort)
                    leaderboardSort.Value = LeaderboardSortMode.Score;
            }, true);

            dropdown.Current.BindValueChanged(sort =>
            {
                if (leaderboardScope.Value != BeatmapLeaderboardScope.Local && sort.NewValue != LeaderboardSortMode.Score)
                    dropdown.Current.Value = LeaderboardSortMode.Score;
            });

            return dropdown;
        }

        private void setStats(string stars, string circles, string sliders, string cs, string ar, string od, string hp)
        {
            starsTile.Value = stars;
            circlesTile.Value = circles;
            slidersTile.Value = sliders;
            csTile.Value = cs;
            arTile.Value = ar;
            odTile.Value = od;
            hpTile.Value = hp;
        }

        private static OsuSpriteText createMetaText() => new OsuSpriteText
        {
            Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
            Colour = new Color4(250, 251, 255, 255),
        };
    }

    private partial class StableStatTile : FillFlowContainer
    {
        private readonly OsuSpriteText valueText;

        public string Value
        {
            set => valueText.Text = value;
        }

        public StableStatTile(LocalisableString label)
        {
            AutoSizeAxes = Axes.Both;
            Direction = FillDirection.Vertical;
            Spacing = new Vector2(2);
            Children = new Drawable[]
            {
                new OsuSpriteText
                {
                    Text = label,
                    Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                    Colour = new Color4(255, 234, 120, 255),
                },
                valueText = new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 21, weight: FontWeight.Bold),
                    Colour = Color4.White,
                }
            };
        }
    }

    private partial class StableTabButton : ClickableContainer
    {
        private readonly Bindable<StableInfoTab> currentTab;
        private readonly StableInfoTab representedTab;
        private readonly Box underline;
        private readonly OsuSpriteText text;

        public StableTabButton(string title, Bindable<StableInfoTab> currentTab, StableInfoTab representedTab)
        {
            this.currentTab = currentTab;
            this.representedTab = representedTab;
            Width = 110;
            Height = 28;
            Action = () => currentTab.Value = representedTab;

            InternalChildren = new Drawable[]
            {
                text = new OsuSpriteText
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Text = title,
                    Font = OsuFont.GetFont(size: 19, weight: FontWeight.Bold),
                },
                underline = new Box
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    RelativeSizeAxes = Axes.X,
                    Width = 0.55f,
                    Height = 3,
                    Alpha = 0,
                    Colour = new Color4(255, 94, 214, 255),
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            currentTab.BindValueChanged(_ => updateState(), true);
        }

        protected override bool OnHover(HoverEvent e)
        {
            updateState();
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            base.OnHoverLost(e);
            updateState();
        }

        private void updateState()
        {
            bool active = currentTab.Value == representedTab;
            text.Colour = active ? Color4.White : new Color4(191, 194, 219, 255);
            underline.FadeTo(active ? 1 : 0.15f, 120, Easing.OutQuint);
        }
    }

    private partial class StableLeaderboardPanel : CompositeDrawable
    {
        private readonly Bindable<BeatmapLeaderboardScope> scope = new Bindable<BeatmapLeaderboardScope>(BeatmapLeaderboardScope.Global);
        private readonly Bindable<LeaderboardSortMode> sorting = new Bindable<LeaderboardSortMode>(LeaderboardSortMode.Score);

        private BeatmapInfo? beatmap;
        private FillFlowContainer scoreRows = null!;
        private OsuSpriteText stateText = null!;

        public IBindable<BeatmapLeaderboardScope> Scope => scope;

        public IBindable<LeaderboardSortMode> Sorting => sorting;

        public BeatmapInfo? Beatmap
        {
            get => beatmap;
            set
            {
                beatmap = value;

                if (IsLoaded)
                    RefreshScores();
            }
        }

        [Resolved]
        private LeaderboardManager leaderboardManager { get; set; } = null!;

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Right = 6 },
                            Child = new OsuScrollContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                ScrollbarVisible = false,
                                Child = scoreRows = new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(8),
                            }
                        }
                    },
                    stateText = new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = OsuFont.GetFont(size: 24, weight: FontWeight.Bold),
                        Colour = new Color4(236, 239, 255, 255),
                    }
                }
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            leaderboardManager.Scores.BindValueChanged(scores => updateContent(scores.NewValue), true);
            scope.BindValueChanged(_ => RefreshScores(), true);
            sorting.BindValueChanged(_ => RefreshScores());
            ruleset.BindValueChanged(_ => RefreshScores());
        }

        public void RefreshScores()
        {
            if (!IsLoaded)
                return;

            if (beatmap == null)
            {
                showState("Select a difficulty");
                return;
            }

            var effectiveSorting = scope.Value == BeatmapLeaderboardScope.Local ? sorting.Value : LeaderboardSortMode.Score;

            showState("Loading scores...");
            leaderboardManager.FetchWithCriteria(new LeaderboardCriteria(beatmap, ruleset.Value, scope.Value, null, effectiveSorting), forceRefresh: true);
        }

        private void updateContent(LeaderboardScores? scores)
        {
            if (scores == null)
            {
                showState("Loading scores...");
                return;
            }

            if (scores.FailState is LeaderboardFailState failState)
            {
                showState(failState switch
                {
                    LeaderboardFailState.NotLoggedIn => "Sign in to see online records",
                    LeaderboardFailState.NotSupporter => "Supporter needed for this scope",
                    LeaderboardFailState.NoTeam => "Join a team to view team scores",
                    LeaderboardFailState.NetworkFailure => "Could not load scores",
                    LeaderboardFailState.BeatmapUnavailable => "Map has no online leaderboard",
                    LeaderboardFailState.RulesetUnavailable => "Ruleset leaderboard unavailable",
                    _ => "No records yet!"
                });
                return;
            }

            var visibleScores = scores.TopScores.Take(6).ToArray();

            if (visibleScores.Length == 0)
            {
                showState("No records yet!");
                return;
            }

            stateText.Hide();
            scoreRows.Show();
            scoreRows.Clear();

            for (int i = 0; i < visibleScores.Length; i++)
                scoreRows.Add(new StableLeaderboardRow(visibleScores[i], i + 1));
        }

        private void showState(string text)
        {
            scoreRows.Clear();
            scoreRows.Hide();
            stateText.Text = text;
            stateText.Show();
        }
    }

    private partial class StableLeaderboardRow : CompositeDrawable
    {
        private readonly ScoreInfo score;
        private readonly int position;
        private Sprite rankSprite = null!;

        [Resolved]
        private ISkinSource source { get; set; } = null!;

        public StableLeaderboardRow(ScoreInfo score, int position)
        {
            this.score = score;
            this.position = position;

            RelativeSizeAxes = Axes.X;
            Height = 58;

            string mods = string.Concat(score.Mods.Select(m => m.Acronym).Where(a => !string.IsNullOrEmpty(a) && a != "NM"));

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 12,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(42, 34, 58, 228),
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = 4,
                        Colour = position == 1 ? new Color4(255, 209, 78, 255) : new Color4(255, 109, 216, 255),
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Padding = new MarginPadding { Left = 14, Right = 12, Top = 8, Bottom = 8 },
                        Spacing = new Vector2(8),
                        Children = new Drawable[]
                        {
                            new Container
                            {
                                Width = 34,
                                RelativeSizeAxes = Axes.Y,
                                Child = new OsuSpriteText
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    Text = $"#{position}",
                                    Font = OsuFont.GetFont(size: 21, weight: FontWeight.Bold),
                                    Colour = Color4.White,
                                }
                            },
                            new Container
                            {
                                Width = 34,
                                RelativeSizeAxes = Axes.Y,
                                Child = rankSprite = new Sprite
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                    FillMode = FillMode.Fit,
                                    Size = new Vector2(28),
                                }
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Child = new GridContainer
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    ColumnDimensions = new[]
                                    {
                                        new Dimension(),
                                        new Dimension(GridSizeMode.Absolute, 132),
                                    },
                                    Content = new[]
                                    {
                                        new Drawable[]
                                        {
                                            new FillFlowContainer
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                                Direction = FillDirection.Vertical,
                                                Spacing = new Vector2(2),
                                                Children = new Drawable[]
                                                {
                                                    new TruncatingSpriteText
                                                    {
                                                        RelativeSizeAxes = Axes.X,
                                                        Text = score.User.Username,
                                                        Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                                                        Colour = Color4.White,
                                                    },
                                                    new TruncatingSpriteText
                                                    {
                                                        RelativeSizeAxes = Axes.X,
                                                        Text = $"{score.Accuracy * 100:0.00}% • {score.MaxCombo:N0}x • {(string.IsNullOrEmpty(mods) ? "NM" : mods)}",
                                                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                                                        Colour = new Color4(198, 204, 229, 255),
                                                    }
                                                }
                                            },
                                            new Container
                                            {
                                                RelativeSizeAxes = Axes.Y,
                                                Width = 132,
                                                Child = new FillFlowContainer
                                                {
                                                    Anchor = Anchor.CentreRight,
                                                    Origin = Anchor.CentreRight,
                                                    AutoSizeAxes = Axes.Both,
                                                    Direction = FillDirection.Vertical,
                                                    Spacing = new Vector2(1),
                                                    Children = new Drawable[]
                                                    {
                                                        new OsuSpriteText
                                                        {
                                                            Anchor = Anchor.TopRight,
                                                            Origin = Anchor.TopRight,
                                                            Text = score.TotalScore.ToString("N0"),
                                                            Font = OsuFont.GetFont(size: 22, weight: FontWeight.Bold),
                                                            Colour = Color4.White,
                                                        },
                                                        new OsuSpriteText
                                                        {
                                                            Anchor = Anchor.TopRight,
                                                            Origin = Anchor.TopRight,
                                                            Text = score.PP.HasValue ? $"{score.PP.Value:0.#}pp" : "0pp",
                                                            Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                                                            Colour = new Color4(255, 220, 120, 255),
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            rankSprite.Texture = source.GetTexture($"ranking-{score.Rank}-small");
        }
    }

    private partial class StableActionButton : ClickableContainer
    {
        private readonly Box background;
        private readonly Box accentBar;
        private readonly Color4 accent;
        private readonly Color4 baseColour;
        private readonly float accentBarAlpha;

        public StableActionButton(string label, string hint, Color4 accent, Action action, bool emphasised = false)
        {
            this.accent = accent;
            accentBarAlpha = emphasised ? 0 : 0.85f;
            baseColour = emphasised ? accent.Opacity(0.92f) : new Color4(39, 31, 52, 244);
            Height = 56;
            Action = action;

            InternalChild = new Container
            {
                RelativeSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = 8,
                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = baseColour,
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientVertical(Color4.White.Opacity(0.06f), Color4.Black.Opacity(0.16f)),
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 1,
                        Colour = Color4.White.Opacity(0.18f),
                    },
                    accentBar = new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 4,
                        Anchor = Anchor.BottomCentre,
                        Origin = Anchor.BottomCentre,
                        Colour = accent.Opacity(accentBarAlpha),
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = label,
                                Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                                Colour = Color4.White,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Text = hint,
                                Font = OsuFont.GetFont(size: 10, weight: FontWeight.SemiBold),
                                Colour = Color4.White.Opacity(0.42f),
                            }
                        }
                    }
                }
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(baseColour.Lighten(0.08f), 80, Easing.OutQuint);
            accentBar.FadeTo(1, 80, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            base.OnHoverLost(e);
            background.FadeColour(baseColour, 80, Easing.OutQuint);
            accentBar.FadeTo(accentBarAlpha, 80, Easing.OutQuint);
        }
    }

    private partial class StableLabel : CompositeDrawable
    {
        public StableLabel(LocalisableString text)
        {
            AutoSizeAxes = Axes.Both;
            InternalChild = new OsuSpriteText
            {
                Text = text,
                Font = OsuFont.GetFont(size: 18, weight: FontWeight.Bold),
                Colour = new Color4(243, 244, 255, 255),
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
            };
        }
    }

    private partial class StableCompactLabel : CompositeDrawable
    {
        public StableCompactLabel(LocalisableString text)
        {
            AutoSizeAxes = Axes.Both;
            InternalChild = new OsuSpriteText
            {
                Text = text,
                Font = OsuFont.GetFont(size: 16, weight: FontWeight.Bold),
                Colour = new Color4(243, 244, 255, 255),
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Margin = new MarginPadding { Top = 10 },
            };
        }
    }

    private partial class StableHeaderSearchTextBox : OsuTextBox
    {
        protected override float LeftRightPadding => 18;

        public StableHeaderSearchTextBox()
        {
            CornerRadius = 14;
            Masking = true;
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            BackgroundUnfocused = new Color4(52, 42, 64, 235);
            BackgroundFocused = new Color4(64, 49, 78, 245);
            BackgroundCommit = BorderColour = colourProvider.Highlight1;
        }
    }

    private partial class StableDropdown<T> : Dropdown<T>
        where T : struct, Enum
    {
        protected override DropdownHeader CreateHeader() => new StableDropdownHeader();

        protected override DropdownMenu CreateMenu() => new StableDropdownMenu
        {
            MaxHeight = 260,
        };

        private partial class StableDropdownMenu : DropdownMenu
        {
            private Color4 hoverColour;
            private Color4 selectionColour;

            public StableDropdownMenu()
            {
                CornerRadius = 12;
                MaskingContainer.CornerRadius = 10;
                Masking = true;
                Alpha = 0;
                ItemsContainer.Padding = new MarginPadding(6);
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                BackgroundColour = new Color4(31, 25, 43, 245);
                hoverColour = colourProvider.Highlight1.Opacity(0.24f);
                selectionColour = colourProvider.Highlight1.Opacity(0.38f);
            }

            protected override void AnimateOpen() => this.FadeIn(120, Easing.OutQuint);

            protected override void AnimateClose() => this.FadeOut(120, Easing.OutQuint);

            protected override void UpdateSize(Vector2 newSize)
            {
                Width = newSize.X;
                this.ResizeHeightTo(newSize.Y, 120, Easing.OutQuint);
            }

            protected override osu.Framework.Graphics.UserInterface.Menu CreateSubMenu() => new BasicMenu(Direction.Vertical);

            protected override ScrollContainer<Drawable> CreateScrollContainer(Direction direction) => new OsuScrollContainer(direction)
            {
                ScrollbarVisible = false,
            };

            protected override DrawableDropdownMenuItem CreateDrawableDropdownMenuItem(MenuItem item) => new StableDropdownMenuItem(item)
            {
                BackgroundColour = new Color4(54, 45, 70, 0),
                BackgroundColourHover = hoverColour,
                BackgroundColourSelected = selectionColour,
            };

            private partial class StableDropdownMenuItem : DrawableDropdownMenuItem
            {
                public StableDropdownMenuItem(MenuItem item)
                    : base(item)
                {
                    Foreground.Padding = new MarginPadding { Horizontal = 12, Vertical = 8 };
                    Masking = true;
                    CornerRadius = 8;
                }

                protected override Drawable CreateContent() => new OsuSpriteText
                {
                    Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold),
                    Colour = new Color4(240, 241, 255, 255),
                };
            }
        }

        private partial class StableDropdownHeader : DropdownHeader
        {
            private readonly OsuSpriteText label;
            private readonly SpriteIcon chevron;

            protected override LocalisableString Label
            {
                get => label.Text;
                set => label.Text = value;
            }

            public StableDropdownHeader()
            {
                AutoSizeAxes = Axes.None; // base DropdownHeader sets AutoSizeAxes = Axes.Y; override before setting Height
                Height = 38;
                Margin = new MarginPadding();
                CornerRadius = 10;
                Background.CornerRadius = 10;
                Background.Masking = true;
                Foreground.Padding = new MarginPadding { Left = 14, Right = 12, Top = 7, Bottom = 7 };

                Foreground.Children = new Drawable[]
                {
                    label = new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Font = OsuFont.GetFont(size: 16, weight: FontWeight.Bold),
                        Colour = new Color4(239, 241, 255, 255),
                    },
                    chevron = new SpriteIcon
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Icon = FontAwesome.Solid.ChevronDown,
                        Size = new Vector2(10),
                        Colour = new Color4(219, 223, 245, 255),
                    }
                };
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                BackgroundColour = new Color4(46, 38, 61, 240);
                BackgroundColourHover = new Color4(60, 48, 78, 245);

                Background.Child = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.White,
                };

                Background.BorderColour = colourProvider.Highlight1.Opacity(0.6f);
                Background.BorderThickness = 1;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                updateExpandedState();
            }

            protected override bool OnHover(HoverEvent e)
            {
                chevron.ScaleTo(1.08f, 80, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                base.OnHoverLost(e);
                chevron.ScaleTo(1f, 80, Easing.OutQuint);
            }

            private void updateExpandedState()
            {
                bool open = SearchBar.State.Value == Visibility.Visible;
                chevron.RotateTo(open ? 180 : 0, 120, Easing.OutQuint);
            }

            protected override DropdownSearchBar CreateSearchBar() => new StableDropdownSearchBar();

            private partial class StableDropdownSearchBar : DropdownSearchBar
            {
                protected override void PopIn() => this.FadeIn(80, Easing.OutQuint);

                protected override void PopOut() => this.FadeOut(80, Easing.OutQuint);

                protected override TextBox CreateTextBox() => new OsuTextBox
                {
                    PlaceholderText = "type to search",
                };
            }
        }
    }

    private partial class StableLegacyTopRankBadge : CompositeDrawable
    {
        private readonly Sprite sprite;
        private BeatmapInfo? beatmap;
        private IDisposable? scoreSubscription;

        public BeatmapInfo? Beatmap
        {
            get => beatmap;
            set
            {
                beatmap = value;

                if (IsLoaded)
                    updateSubscription();
            }
        }

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private ISkinSource source { get; set; } = null!;

        public StableLegacyTopRankBadge()
        {
            AutoSizeAxes = Axes.Both;
            Alpha = 0;

            InternalChild = sprite = new Sprite
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Scale = new Vector2(0.9f),
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            ruleset.BindValueChanged(_ => updateSubscription(), true);
        }

        private void updateSubscription()
        {
            scoreSubscription?.Dispose();

            if (beatmap == null)
            {
                Alpha = 0;
                return;
            }

            scoreSubscription = realm.RegisterForNotifications(r =>
                    r.GetAllLocalScoresForUser(api.LocalUser.Value.Id)
                     .Filter($@"{nameof(ScoreInfo.BeatmapInfo)}.{nameof(BeatmapInfo.ID)} == $0"
                             + $" && {nameof(ScoreInfo.Ruleset)}.{nameof(RulesetInfo.ShortName)} == $1", beatmap.ID, ruleset.Value.ShortName),
                localScoresChanged);
        }

        private void localScoresChanged(IRealmCollection<ScoreInfo> sender, ChangeSet? changes)
        {
            if (changes?.HasCollectionChanges() == false)
                return;

            ScoreInfo? topScore = sender.MaxBy(info => (info.TotalScore, -info.Date.UtcDateTime.Ticks));

            if (topScore?.Rank is not ScoreRank rank)
            {
                sprite.Texture = null;
                this.FadeOut(120);
                return;
            }

            sprite.Texture = source.GetTexture($"ranking-{rank}-small");

            if (sprite.Texture == null)
            {
                this.FadeOut(120);
                return;
            }

            this.FadeIn(120);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            scoreSubscription?.Dispose();
        }
    }
}





