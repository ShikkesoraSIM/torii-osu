// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Skinning;
using osuTK;
using Newtonsoft.Json;

namespace osu.Game.Screens.Play.HUD
{
    /// <summary>
    /// The layout direction for performance projections.
    /// </summary>
    public enum ProjectionLayoutDirection
    {
        /// <summary>
        /// Horizontal layout (left to right).
        /// </summary>
        Horizontal,

        /// <summary>
        /// Vertical layout (top to bottom).
        /// </summary>
        Vertical
    }

    /// <summary>
    /// The sort order for PP projections.
    /// </summary>
    public enum ProjectionSortOrder
    {
        /// <summary>
        /// Lowest to highest (95% at top, 100% at bottom).
        /// </summary>
        LowestToHighest,

        /// <summary>
        /// Highest to lowest (100% at top, 95% at bottom).
        /// </summary>
        HighestToLowest
    }

    /// <summary>
    /// Displays an approximate PP projection row (95/97/99/100%) based on the beatmap's max PP with current mods.
    /// </summary>
    public partial class PerformanceProjectionDisplay : CompositeDrawable, ISerialisableDrawable
    {
        private FillFlowContainer content = null!;
        private FillFlowContainer projections = null!;
        private Container variantBadge = null!;

        [Resolved(canBeNull: true)]
        private GameplayState gameplayState { get; set; }

        [Resolved(canBeNull: true)]
        private IBindable<WorkingBeatmap> workingBeatmap { get; set; }

        [Resolved(canBeNull: true)]
        private IBindable<RulesetInfo> rulesetInfoBindable { get; set; }

        [Resolved(canBeNull: true)]
        private IBindable<IReadOnlyList<Mod>> modsBindable { get; set; }

        [Resolved]
        private BeatmapDifficultyCache difficultyCache { get; set; } = null!;

        private IBindable<bool> ppDevVariantEnabled = null!;

        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private CancellationTokenSource projectionCalculationSource = new CancellationTokenSource();
        private readonly object projectionCalculationLock = new object();

        private static readonly double[] projectionAccuracies = { 95, 97, 99, 100 };

        public bool UsesFixedAnchor { get; set; }

        /// <summary>
        /// Whether to show the 95% PP projection.
        /// </summary>
        [JsonProperty]
        [SettingSource("Show 95%", "Display the 95% accuracy PP projection", 1)]
        public Bindable<bool> Show95Percent { get; } = new Bindable<bool>(true);

        /// <summary>
        /// Whether to show the 97% PP projection.
        /// </summary>
        [JsonProperty]
        [SettingSource("Show 97%", "Display the 97% accuracy PP projection", 2)]
        public Bindable<bool> Show97Percent { get; } = new Bindable<bool>(true);

        /// <summary>
        /// Whether to show the 99% PP projection.
        /// </summary>
        [JsonProperty]
        [SettingSource("Show 99%", "Display the 99% accuracy PP projection", 3)]
        public Bindable<bool> Show99Percent { get; } = new Bindable<bool>(true);

        /// <summary>
        /// Whether to show the 100% PP projection.
        /// </summary>
        [JsonProperty]
        [SettingSource("Show 100%", "Display the 100% accuracy PP projection", 4)]
        public Bindable<bool> Show100Percent { get; } = new Bindable<bool>(true);

        /// <summary>
        /// The layout direction for the projections.
        /// </summary>
        [JsonProperty]
        [SettingSource("Layout Direction", "Choose between horizontal or vertical layout for the projections")]
        public Bindable<ProjectionLayoutDirection> LayoutDirection { get; } = new Bindable<ProjectionLayoutDirection>(ProjectionLayoutDirection.Horizontal);

        /// <summary>
        /// The sort order for the projections (affects both horizontal and vertical layouts).
        /// </summary>
        [JsonProperty]
        [SettingSource("Sort Order", "Choose the order of projections (lowest to highest or highest to lowest)", 5)]
        public Bindable<ProjectionSortOrder> SortOrder { get; } = new Bindable<ProjectionSortOrder>(ProjectionSortOrder.LowestToHighest);

        public PerformanceProjectionDisplay()
        {
            AutoSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            ppDevVariantEnabled = ToriiPpVariantState.UsePpDevVariantBindable.GetBoundCopy();

            InternalChild = content = new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 2),
                Children = new Drawable[]
                {
                    variantBadge = new Container
                    {
                        AutoSizeAxes = Axes.Both,
                        Alpha = 0,
                        Child = new OsuSpriteText
                        {
                            Font = OsuFont.GetFont(size: 11, weight: FontWeight.SemiBold),
                            Text = "pp-dev",
                            Colour = new osuTK.Graphics.Color4(151, 255, 187, 255),
                        }
                    },
                    projections = new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(10, 0),
                    }
                }
            };

            // Bind to settings changes
            ppDevVariantEnabled.BindValueChanged(_ => loadProjections());
            Show95Percent.BindValueChanged(_ => loadProjections());
            Show97Percent.BindValueChanged(_ => loadProjections());
            Show99Percent.BindValueChanged(_ => loadProjections());
            Show100Percent.BindValueChanged(_ => loadProjections());
            LayoutDirection.BindValueChanged(_ => updateLayoutDirection());
            SortOrder.BindValueChanged(_ => loadProjections());

            loadProjections();
        }

        private void updateLayoutDirection()
        {
            if (projections == null) return;

            switch (LayoutDirection.Value)
            {
                case ProjectionLayoutDirection.Horizontal:
                    projections.Direction = FillDirection.Horizontal;
                    projections.Spacing = new Vector2(10, 0);
                    break;

                case ProjectionLayoutDirection.Vertical:
                    projections.Direction = FillDirection.Vertical;
                    projections.Spacing = new Vector2(0, 10);
                    break;
            }
        }

        private void loadProjections()
        {
            BeatmapInfo? beatmapInfo = null;
            RulesetInfo? rulesetInfo = null;
            Mod[] mods = Array.Empty<Mod>();

            if (gameplayState != null)
            {
                beatmapInfo = gameplayState.Beatmap.BeatmapInfo;
                rulesetInfo = gameplayState.Ruleset.RulesetInfo;
                mods = gameplayState.Mods.Select(m => m.DeepClone()).ToArray();
            }
            else if (workingBeatmap != null && rulesetInfoBindable != null && modsBindable != null
                     && workingBeatmap.Value != null && rulesetInfoBindable.Value != null)
            {
                beatmapInfo = workingBeatmap.Value.BeatmapInfo;
                rulesetInfo = rulesetInfoBindable.Value;
                mods = modsBindable.Value?.Select(m => m.DeepClone()).ToArray() ?? Array.Empty<Mod>();
            }
            else
                return;

            CancellationTokenSource requestTokenSource;

            lock (projectionCalculationLock)
            {
                projectionCalculationSource.Cancel();
                projectionCalculationSource.Dispose();
                projectionCalculationSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationSource.Token);
                requestTokenSource = projectionCalculationSource;
            }

            Task.Run(async () =>
            {
                try
                {
                    var starDifficulty = await difficultyCache.GetDifficultyAsync(beatmapInfo, rulesetInfo, mods, requestTokenSource.Token).ConfigureAwait(false);
                    var maxPP = starDifficulty?.PerformanceAttributes?.Total ?? 0;

                    if (requestTokenSource.IsCancellationRequested)
                        return;

                    Schedule(() =>
                    {
                        if (!requestTokenSource.IsCancellationRequested)
                            updateUI(maxPP);
                    });
                }
                catch (OperationCanceledException)
                {
                }
            }, requestTokenSource.Token);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Update layout direction after the component is fully loaded
            updateLayoutDirection();

            if (gameplayState == null && workingBeatmap != null)
            {
                workingBeatmap.BindValueChanged(_ => loadProjections(), true);
            }

            if (gameplayState == null && rulesetInfoBindable != null)
            {
                rulesetInfoBindable.BindValueChanged(_ => loadProjections(), true);
            }

            if (gameplayState == null && modsBindable != null)
            {
                modsBindable.BindValueChanged(_ => loadProjections(), true);
            }
        }

        private void updateUI(double maxPP)
        {
            projections.Clear();
            variantBadge.FadeTo(ppDevVariantEnabled.Value ? 1 : 0, 120, Easing.OutQuint);

            var accuracySettings = new Dictionary<double, Bindable<bool>>
            {
                { 95, Show95Percent },
                { 97, Show97Percent },
                { 99, Show99Percent },
                { 100, Show100Percent }
            };

            // Create list of enabled accuracies
            var enabledAccuracies = projectionAccuracies.Where(acc => accuracySettings[acc].Value).ToList();

            // Sort based on the current sort order setting (affects both horizontal and vertical layouts)
            if (SortOrder.Value == ProjectionSortOrder.HighestToLowest)
            {
                enabledAccuracies.Reverse();
            }

            foreach (var acc in enabledAccuracies)
            {
                double multiplier = Math.Pow(acc / 100.0, 3.0);
                double projected = Math.Max(0, Math.Round(maxPP * multiplier, MidpointRounding.AwayFromZero));

                projections.Add(new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, -2),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Font = OsuFont.Numeric.With(size: 14, weight: FontWeight.Bold),
                            Text = $"{acc:0}%"
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            Font = OsuFont.Numeric.With(size: 14, weight: FontWeight.Bold),
                            Text = $"{projected:N0}pp"
                        },
                    }
                });
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            cancellationSource.Cancel();
            lock (projectionCalculationLock)
            {
                projectionCalculationSource.Cancel();
                projectionCalculationSource.Dispose();
            }
            base.Dispose(isDisposing);
        }
    }
}

