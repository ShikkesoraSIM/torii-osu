// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Audio.Track;
using osu.Framework.Extensions;
using osu.Framework.Threading;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Screens.Play.HUD
{
    public abstract partial class PerformancePointsCounter : RollingCounter<int>
    {
        public bool UsesFixedAnchor { get; set; }

        [Resolved]
        private ScoreProcessor scoreProcessor { get; set; }

        [Resolved]
        private GameplayState gameplayState { get; set; }

        [CanBeNull]
        private List<TimedDifficultyAttributes> timedAttributes;

        private readonly CancellationTokenSource loadCancellationSource = new CancellationTokenSource();

        private JudgementResult lastJudgement;
        private PerformanceCalculator performanceCalculator;
        private ScoreInfo scoreInfo;

        private Mod[] clonedMods;
        private bool isManiaRuleset;

        private readonly object maniaCalculationLock = new object();
        private ScheduledDelegate maniaPpRecalculationDebounce;
        private JudgementResult pendingManiaJudgement;
        private ScoreInfo pendingManiaScoreInfo;
        private DifficultyAttributes pendingManiaAttributes;
        private int pendingManiaRequestId;
        private int latestAppliedManiaRequestId;
        private bool maniaCalculationRunning;
        private double lastPpRecalculationTime;
        private int cachedTimedAttributeIndex;
        private double lastTimedAttributeQueryTime = double.MinValue;

        // Sunny mania performance calculation is significantly heavier than other rulesets.
        // Avoid calculating on every single judgement and offload heavy work from update thread.
        private const double mania_pp_recalculation_interval_ms = 200;

        [BackgroundDependencyLoader]
        private void load(BeatmapDifficultyCache difficultyCache)
        {
            if (gameplayState != null)
            {
                performanceCalculator = gameplayState.Ruleset.CreatePerformanceCalculator();
                clonedMods = gameplayState.Mods.Select(m => m.DeepClone()).ToArray();
                isManiaRuleset = gameplayState.Ruleset.RulesetInfo.ShortName == "mania";

                scoreInfo = new ScoreInfo(gameplayState.Score.ScoreInfo.BeatmapInfo, gameplayState.Score.ScoreInfo.Ruleset) { Mods = clonedMods };

                var gameplayWorkingBeatmap = new GameplayWorkingBeatmap(gameplayState.Beatmap);
                difficultyCache.GetTimedDifficultyAttributesAsync(gameplayWorkingBeatmap, gameplayState.Ruleset, clonedMods, loadCancellationSource.Token)
                               .ContinueWith(task => Schedule(() =>
                               {
                                   timedAttributes = task.GetResultSafely();
                                   cachedTimedAttributeIndex = 0;
                                   lastTimedAttributeQueryTime = double.MinValue;

                                   IsValid = true;

                                   if (lastJudgement != null)
                                       onJudgementChanged(lastJudgement);
                               }), TaskContinuationOptions.OnlyOnRanToCompletion);
            }
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (scoreProcessor != null)
            {
                scoreProcessor.NewJudgement += onJudgementChanged;
                scoreProcessor.JudgementReverted += onJudgementChanged;
            }

            if (gameplayState?.LastJudgementResult.Value != null)
                onJudgementChanged(gameplayState.LastJudgementResult.Value);
        }

        public virtual bool IsValid { get; set; }

        private void onJudgementChanged(JudgementResult judgement)
        {
            lastJudgement = judgement;

            if (isManiaRuleset)
            {
                pendingManiaJudgement = judgement;

                if (Time.Current - lastPpRecalculationTime < mania_pp_recalculation_interval_ms)
                {
                    if (maniaPpRecalculationDebounce?.State != ScheduledDelegate.RunState.Waiting)
                    {
                        maniaPpRecalculationDebounce = Scheduler.AddDelayed(() =>
                        {
                            if (pendingManiaJudgement != null)
                                queueManiaRecalculation(pendingManiaJudgement);
                        }, mania_pp_recalculation_interval_ms);
                    }

                    return;
                }

                queueManiaRecalculation(judgement);
                return;
            }

            recalculateCurrentPp(judgement);
        }

        private void queueManiaRecalculation(JudgementResult judgement)
        {
            var attrib = getAttributeAtTime(judgement);

            if (gameplayState == null || attrib == null || scoreProcessor == null)
            {
                IsValid = false;
                return;
            }

            var snapshot = new ScoreInfo(gameplayState.Score.ScoreInfo.BeatmapInfo, gameplayState.Score.ScoreInfo.Ruleset)
            {
                Mods = clonedMods
            };

            scoreProcessor.PopulateScore(snapshot);

            lock (maniaCalculationLock)
            {
                pendingManiaScoreInfo = snapshot;
                pendingManiaAttributes = attrib;
                pendingManiaRequestId++;

                if (maniaCalculationRunning)
                    return;

                maniaCalculationRunning = true;
            }

            Task.Run(runManiaRecalculationWorker);
        }

        private void runManiaRecalculationWorker()
        {
            try
            {
                while (!loadCancellationSource.IsCancellationRequested)
                {
                    ScoreInfo scoreSnapshot;
                    DifficultyAttributes attrib;
                    int requestId;

                    lock (maniaCalculationLock)
                    {
                        if (pendingManiaScoreInfo == null || pendingManiaAttributes == null)
                        {
                            maniaCalculationRunning = false;
                            return;
                        }

                        scoreSnapshot = pendingManiaScoreInfo;
                        attrib = pendingManiaAttributes;
                        requestId = pendingManiaRequestId;

                        pendingManiaScoreInfo = null;
                        pendingManiaAttributes = null;
                    }

                    int currentPp;

                    try
                    {
                        currentPp = (int)Math.Round(performanceCalculator?.Calculate(scoreSnapshot, attrib).Total ?? 0, MidpointRounding.AwayFromZero);
                    }
                    catch
                    {
                        continue;
                    }

                    Schedule(() =>
                    {
                        if (loadCancellationSource.IsCancellationRequested || requestId < latestAppliedManiaRequestId)
                            return;

                        latestAppliedManiaRequestId = requestId;
                        Current.Value = currentPp;
                        lastPpRecalculationTime = Time.Current;
                        IsValid = true;
                    });
                }

                lock (maniaCalculationLock)
                    maniaCalculationRunning = false;
            }
            catch
            {
                lock (maniaCalculationLock)
                    maniaCalculationRunning = false;
            }
        }

        private void recalculateCurrentPp(JudgementResult judgement)
        {
            var attrib = getAttributeAtTime(judgement);

            if (gameplayState == null || attrib == null || scoreProcessor == null)
            {
                IsValid = false;
                return;
            }

            scoreProcessor.PopulateScore(scoreInfo);
            Current.Value = (int)Math.Round(performanceCalculator?.Calculate(scoreInfo, attrib).Total ?? 0, MidpointRounding.AwayFromZero);
            lastPpRecalculationTime = Time.Current;
            IsValid = true;
        }

        [CanBeNull]
        private DifficultyAttributes getAttributeAtTime(JudgementResult judgement)
        {
            if (timedAttributes == null || timedAttributes.Count == 0)
                return null;

            double hitObjectEndTime = judgement.HitObject.GetEndTime();

            if (hitObjectEndTime < lastTimedAttributeQueryTime)
                cachedTimedAttributeIndex = 0;

            lastTimedAttributeQueryTime = hitObjectEndTime;

            while (cachedTimedAttributeIndex + 1 < timedAttributes.Count && timedAttributes[cachedTimedAttributeIndex + 1].Time <= hitObjectEndTime)
                cachedTimedAttributeIndex++;

            while (cachedTimedAttributeIndex > 0 && timedAttributes[cachedTimedAttributeIndex].Time > hitObjectEndTime)
                cachedTimedAttributeIndex--;

            return timedAttributes[cachedTimedAttributeIndex].Attributes;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (scoreProcessor != null)
            {
                scoreProcessor.NewJudgement -= onJudgementChanged;
                scoreProcessor.JudgementReverted -= onJudgementChanged;
            }

            maniaPpRecalculationDebounce?.Cancel();
            loadCancellationSource?.Cancel();

            lock (maniaCalculationLock)
            {
                pendingManiaScoreInfo = null;
                pendingManiaAttributes = null;
            }
        }

        // TODO: This class shouldn't exist, but requires breaking changes to allow DifficultyCalculator to receive an IBeatmap.
        private class GameplayWorkingBeatmap : WorkingBeatmap
        {
            private readonly IBeatmap gameplayBeatmap;

            public GameplayWorkingBeatmap(IBeatmap gameplayBeatmap)
                : base(gameplayBeatmap.BeatmapInfo, null)
            {
                this.gameplayBeatmap = gameplayBeatmap;
            }

            public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken cancellationToken)
                => gameplayBeatmap;

            protected override IBeatmap GetBeatmap() => gameplayBeatmap;

            public override Texture GetBackground() => throw new NotImplementedException();

            protected override Track GetBeatmapTrack() => throw new NotImplementedException();

            protected internal override ISkin GetSkin() => throw new NotImplementedException();

            public override Stream GetStream(string storagePath) => throw new NotImplementedException();
        }
    }
}
