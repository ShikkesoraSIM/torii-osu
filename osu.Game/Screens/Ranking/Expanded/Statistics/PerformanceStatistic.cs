// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Scoring;
using osu.Game.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Resources.Localisation.Web;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Ranking.Expanded.Statistics
{
    public partial class PerformanceStatistic : StatisticDisplay, IHasTooltip
    {
        public LocalisableString TooltipText { get; private set; }

        private readonly ScoreInfo score;

        private readonly Bindable<int> performance = new Bindable<int>();

        private readonly CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
        private IBindable<bool> ppDevVariantEnabled = null!;

        [Resolved(CanBeNull = true)]
        private IAPIProvider? api { get; set; }

        private RollingCounter<int> counter = null!;
        private PpDevVariantIndicatorIcon ppDevIndicator = null!;

        public PerformanceStatistic(ScoreInfo score)
            : base(BeatmapsetsStrings.ShowScoreboardHeaderspp)
        {
            this.score = score;
        }

        [BackgroundDependencyLoader]
        private void load(BeatmapDifficultyCache difficultyCache, CancellationToken? cancellationToken)
        {
            ppDevVariantEnabled = ToriiPpVariantState.UsePpDevVariantBindable.GetBoundCopy();
            bool forceClientRecalculation = isPpDevVariantActive();

            ppDevVariantEnabled.BindValueChanged(v =>
            {
                if (ppDevIndicator != null)
                    ppDevIndicator.FadeTo(isPpDevVariantActive() ? 1f : 0f, 120, Easing.OutQuint);
            }, true);

            if (!forceClientRecalculation && score.PP.HasValue)
            {
                setPerformanceValue(score, score.PP.Value);
                return;
            }

            if (isOnlineScore(score) && !forceClientRecalculation)
                return;

            Task.Run(async () =>
            {
                var attributes = await difficultyCache.GetDifficultyAsync(score.BeatmapInfo!, score.Ruleset, score.Mods, cancellationToken ?? default).ConfigureAwait(false);
                var performanceCalculator = score.Ruleset.CreateInstance().CreatePerformanceCalculator();

                // Performance calculation requires the beatmap and ruleset to be locally available.
                if (attributes?.DifficultyAttributes == null || performanceCalculator == null)
                {
                    if (score.PP.HasValue)
                        Schedule(() => setPerformanceValue(score, score.PP.Value));

                    return;
                }

                var result = await performanceCalculator.CalculateAsync(score, attributes.Value.DifficultyAttributes, cancellationToken ?? default).ConfigureAwait(false);

                Schedule(() => setPerformanceValue(score, result.Total));
            }, cancellationToken ?? default);
        }

        private static bool isOnlineScore(ScoreInfo scoreInfo) => scoreInfo.OnlineID > 0 || scoreInfo.LegacyOnlineID > 0;

        private void setPerformanceValue(ScoreInfo scoreInfo, double? pp)
        {
            if (pp.HasValue)
            {
                performance.Value = (int)Math.Round(pp.Value, MidpointRounding.AwayFromZero);
                LocalisableString? primaryTooltip = null;

                if (!scoreInfo.BeatmapInfo!.Status.GrantsPerformancePoints())
                {
                    Alpha = 1f;
                    primaryTooltip = ResultsScreenStrings.NoPPForUnrankedBeatmaps;
                }
                else if (hasUnrankedMods(scoreInfo))
                {
                    Alpha = 1f;
                    primaryTooltip = ResultsScreenStrings.NoPPForUnrankedMods;
                }
                else if (scoreInfo.Rank == ScoreRank.F)
                {
                    Alpha = 1f;
                    primaryTooltip = ResultsScreenStrings.NoPPForFailedScores;
                }

                Alpha = 1f;
                TooltipText = primaryTooltip ?? default;

                if (isPpDevVariantActive())
                {
                    const string ppDevTooltip = "Using Torii pp-dev calculations (same submitted score data, updated formula).";
                    TooltipText = primaryTooltip == null
                        ? ppDevTooltip
                        : LocalisableString.Interpolate($"{primaryTooltip}\n{ppDevTooltip}");
                }
            }
        }

        private static bool hasUnrankedMods(ScoreInfo scoreInfo)
        {
            IEnumerable<Mod> modsToCheck = scoreInfo.Mods;

            if (scoreInfo.IsLegacyScore)
                modsToCheck = modsToCheck.Where(m => m is not ModClassic);

            return modsToCheck.Any(m => !m.Ranked);
        }

        public override void Appear()
        {
            base.Appear();
            counter.Current.BindTo(performance);
        }

        protected override void Dispose(bool isDisposing)
        {
            cancellationTokenSource.Cancel();
            base.Dispose(isDisposing);
        }

        protected override Drawable CreateContent() => new FillFlowContainer
        {
            AutoSizeAxes = Axes.Both,
            Anchor = Anchor.TopCentre,
            Origin = Anchor.TopCentre,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(6, 0),
            Children = new Drawable[]
            {
                counter = new StatisticCounter
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                },
                ppDevIndicator = new PpDevVariantIndicatorIcon
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    Alpha = isPpDevVariantActive() ? 1f : 0f,
                },
            }
        };

        private bool isPpDevVariantActive()
            => ToriiRequestVariantExtensions.IsPpDevVariantActive(api);

        private partial class PpDevVariantIndicatorIcon : CompositeDrawable, IHasTooltip
        {
            public LocalisableString TooltipText => "Using latest pp-dev calculations.";

            public PpDevVariantIndicatorIcon()
            {
                AutoSizeAxes = Axes.Both;
                InternalChild = new SpriteIcon
                {
                    Icon = FontAwesome.Solid.Flask,
                    Size = new Vector2(10),
                    Colour = new Color4(132, 209, 255, 255),
                };
            }
        }
    }
}
