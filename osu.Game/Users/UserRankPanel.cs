// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.Profile.Header.Components;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Rulesets;
using osuTK;

namespace osu.Game.Users
{
    /// <summary>
    /// User card that shows user's global and country ranks in the bottom.
    /// Meant to be used in the toolbar login overlay.
    /// </summary>
    public partial class UserRankPanel : UserPanel
    {
        private const int padding = 10;
        private const int main_content_height = 80;

        private ProfileValueDisplay globalRankDisplay = null!;
        private ProfileValueDisplay countryRankDisplay = null!;
        private ProfileValueDisplay ppDisplay = null!;
        private TotalPlayTime playtimeDisplay = null!;
        private LoadingLayer loadingLayer = null!;
        private Container modIndicatorContainer = null!;
        private OsuSpriteText modIndicatorText = null!;

        public UserRankPanel(APIUser user)
            : base(user)
        {
            AutoSizeAxes = Axes.Y;
            CornerRadius = 10;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            BorderColour = ColourProvider?.Light1 ?? Colours.GreyVioletLighter;
        }

        [Resolved]
        private LocalUserStatisticsProvider? statisticsProvider { get; set; }

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private UserStatisticsWatcher? userStatisticsWatcher { get; set; }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (statisticsProvider != null)
                statisticsProvider.StatisticsUpdated += onStatisticsUpdated;

            // Listen for score-based statistics updates for more immediate refresh after plays
            if (userStatisticsWatcher != null)
            {
                userStatisticsWatcher.LatestUpdate.BindValueChanged(scoreUpdate =>
                {
                    if (scoreUpdate.NewValue != null)
                    {
                        // Refresh display when a score-based update is received
                        updateDisplay();
                    }
                });
            }

            // Listen for ruleset changes and update display accordingly
            ruleset.BindValueChanged(rulesetChanged =>
            {
                updateDisplay();

                // If switching to a special ruleset that doesn't have statistics yet,
                // try to fetch them from the API
                if (IsSpecialRuleset(rulesetChanged.NewValue) && statisticsProvider?.GetStatisticsFor(rulesetChanged.NewValue) == null)
                {
                    statisticsProvider?.RefetchStatistics(rulesetChanged.NewValue);
                }
            }, true);
        }

        private void onStatisticsUpdated(UserStatisticsUpdate update)
        {
            // Update display for any ruleset update to ensure we catch all statistic changes
            // This ensures stats refresh after every play regardless of current ruleset
            updateDisplay();
        }

        private void updateDisplay()
        {
            // Get statistics for the current ruleset
            var statistics = statisticsProvider?.GetStatisticsFor(ruleset.Value);

            // If no statistics found for special ruleset, try to get from base ruleset
            if (statistics == null && IsSpecialRuleset(ruleset.Value))
            {
                var baseRuleset = ruleset.Value.CreateNormalRuleset();
                statistics = statisticsProvider?.GetStatisticsFor(baseRuleset);
            }

            loadingLayer.State.Value = statistics == null ? Visibility.Visible : Visibility.Hidden;
            globalRankDisplay.Content = statistics?.GlobalRank?.ToLocalisableString("\\##,##0") ?? "-";
            countryRankDisplay.Content = statistics?.CountryRank?.ToLocalisableString("\\##,##0") ?? "-";
            ppDisplay.Content = statistics?.PP?.ToLocalisableString("#,##0") ?? "0";
            playtimeDisplay.UserStatistics.Value = statistics;

            // Update mod indicator for special rulesets (Relax/Autopilot)
            string? modName = GetModNameForRuleset(ruleset.Value);
            if (modName != null)
            {
                modIndicatorText.Text = modName;
                modIndicatorContainer.Show();
            }
            else
            {
                modIndicatorContainer.Hide();
            }
        }

        /// <summary>
        /// Gets the mod name for a special ruleset (Relax/Autopilot), or null if not a special ruleset.
        /// </summary>
        private static string? GetModNameForRuleset(RulesetInfo ruleset)
        {
            return ruleset.ShortName switch
            {
                RulesetInfo.OSU_RELAX_MODE_SHORTNAME => "Relax",
                RulesetInfo.OSU_AUTOPILOT_MODE_SHORTNAME => "Autopilot",
                RulesetInfo.TAIKO_RELAX_MODE_SHORTNAME => "Relax",
                RulesetInfo.CATCH_RELAX_MODE_SHORTNAME => "Relax",
                _ => null
            };
        }

        /// <summary>
        /// Determines if the current ruleset is a special mode (Relax/Autopilot).
        /// </summary>
        private static bool IsSpecialRuleset(RulesetInfo ruleset) => GetModNameForRuleset(ruleset) != null;

        protected override Drawable CreateLayout()
        {
            FillFlowContainer details;

            var layout = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Children = new Drawable[]
                {
                    new Container
                    {
                        Name = "Main content",
                        RelativeSizeAxes = Axes.X,
                        Height = main_content_height,
                        CornerRadius = 10,
                        Masking = true,
                        Children = new Drawable[]
                        {
                            new UserCoverBackground
                            {
                                RelativeSizeAxes = Axes.Both,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                User = User,
                                Alpha = 0.3f
                            },
                            new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding(padding),
                                ColumnDimensions = new[]
                                {
                                    new Dimension(GridSizeMode.AutoSize),
                                    new Dimension(),
                                },
                                RowDimensions = new[]
                                {
                                    new Dimension()
                                },
                                Content = new[]
                                {
                                    new Drawable[]
                                    {
                                        CreateAvatar().With(avatar =>
                                        {
                                            avatar.Size = new Vector2(60);
                                            avatar.Masking = true;
                                            avatar.CornerRadius = 6;
                                        }),
                                        new GridContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            Padding = new MarginPadding { Left = padding },
                                            ColumnDimensions = new[]
                                            {
                                                new Dimension()
                                            },
                                            RowDimensions = new[]
                                            {
                                                new Dimension(GridSizeMode.AutoSize),
                                                new Dimension()
                                            },
                                            Content = new[]
                                            {
                                                new Drawable[]
                                                {
                                                    details = new FillFlowContainer
                                                    {
                                                        AutoSizeAxes = Axes.Both,
                                                        Direction = FillDirection.Horizontal,
                                                        Spacing = new Vector2(6),
                                                        Children = new Drawable[]
                                                        {
                                                            CreateFlag(),
                                                            modIndicatorContainer = new Container
                                                            {
                                                                AutoSizeAxes = Axes.Both,
                                                                Masking = true,
                                                                CornerRadius = 4,
                                                                Alpha = 0,
                                                                AlwaysPresent = false,
                                                                Children = new Drawable[]
                                                                {
                                                                    new Box
                                                                    {
                                                                        RelativeSizeAxes = Axes.Both,
                                                                        Colour = ColourProvider?.Light1 ?? Colours.GreyVioletLighter
                                                                    },
                                                                    modIndicatorText = new OsuSpriteText
                                                                    {
                                                                        Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                                                                        Padding = new MarginPadding { Horizontal = 6, Vertical = 2 },
                                                                        Colour = ColourProvider?.Content1 ?? Colours.GreyVioletLight
                                                                    }
                                                                }
                                                            },
                                                            // supporter icon is being added later
                                                        }
                                                    }
                                                },
                                                new Drawable[]
                                                {
                                                    // Torii: wrap username in aura container (no-op for non-elite users).
                                                    UserAuraContainer.Wrap(User, CreateUsername().With(username =>
                                                    {
                                                        username.Anchor = Anchor.CentreLeft;
                                                        username.Origin = Anchor.CentreLeft;
                                                    }))
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    },
                    new GridContainer
                    {
                        Name = "Bottom content",
                        Margin = new MarginPadding { Top = main_content_height },
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding(padding),
                        ColumnDimensions = new[]
                        {
                            new Dimension(),
                            new Dimension()
                        },
                        RowDimensions = new[] { new Dimension(GridSizeMode.AutoSize), new Dimension(GridSizeMode.AutoSize) },
                        Content = new[]
                        {
                            new Drawable[]
                            {
                                globalRankDisplay = new ProfileValueDisplay(true)
                                {
                                    Title = UsersStrings.ShowRankGlobalSimple,
                                    Margin = new MarginPadding { Bottom = padding }
                                    // TODO: implement highest rank tooltip
                                    // `RankHighest` resides in `APIUser`, but `api.LocalUser` doesn't update
                                    // maybe move to `UserStatistics` in api, so `UserStatisticsWatcher` can update the value
                                },
                                countryRankDisplay = new ProfileValueDisplay(true)
                                {
                                    Title = UsersStrings.ShowRankCountrySimple
                                }
                            },
                            new Drawable[]
                            {
                                ppDisplay = new ProfileValueDisplay
                                {
                                    Title = "pp"
                                },
                                playtimeDisplay = new TotalPlayTime()
                            }
                        }
                    },
                    loadingLayer = new LoadingLayer(true),
                }
            };

            if (User.IsSupporter)
            {
                details.Add(new SupporterIcon
                {
                    Height = 26,
                    SupportLevel = User.SupportLevel
                });
            }

            return layout;
        }

        protected override bool OnHover(HoverEvent e)
        {
            BorderThickness = 2;
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            BorderThickness = 0;
            base.OnHoverLost(e);
        }

        protected override Drawable? CreateBackground() => null;

        protected override void Dispose(bool isDisposing)
        {
            if (statisticsProvider.IsNotNull())
                statisticsProvider.StatisticsUpdated -= onStatisticsUpdated;

            if (userStatisticsWatcher != null)
                userStatisticsWatcher.LatestUpdate.UnbindAll();

            base.Dispose(isDisposing);
        }
    }
}
