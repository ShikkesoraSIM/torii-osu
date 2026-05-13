// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Skinning.Components.Mania
{
    /// <summary>
    /// VSRG-style "ratio" counter for osu!mania — Perfects (MAX / 300+)
    /// divided by Greats (the regular 300). It's a measure of timing
    /// precision beyond what plain accuracy can express: two players
    /// can both have 99% accuracy while one has a much higher PA ratio
    /// (more MAXes, fewer 300s) than the other, indicating tighter
    /// timing.
    ///
    /// Lives in <c>osu.Game.dll</c> (not the mania assembly) so the
    /// in-game skin layout editor's generic "HUD" toolbox section
    /// includes it. The toolbox enumerates types from the assembly of
    /// the active ruleset OR from <c>OsuGame</c>'s assembly when no
    /// ruleset is active — keeping mania-specific HUD pieces in the
    /// mania DLL hides them behind the per-ruleset section that most
    /// users never open.
    ///
    /// Display semantics:
    /// - Before ANY accuracy-affecting judgement has landed, the counter
    ///   shows "--". The very first hit transitions it to a real value
    ///   (or "MAX" — see below). This is the only "not enough data"
    ///   threshold; an earlier 15-judgement requirement was removed
    ///   after upstream feedback (the original concern that 0 greats
    ///   would produce NaN / 1.84e19 garbage was wrong — the
    ///   <c>greats == 0</c> branch already coerces to "MAX" cleanly,
    ///   so showing the live ratio from the first hit is fine).
    /// - With non-zero Greats, displays the ratio with two decimals
    ///   (e.g. <c>2.34</c>).
    /// - With non-zero Perfects but zero Greats, displays "MAX" — the
    ///   ratio is technically infinite but "∞" reads weird in a HUD,
    ///   "MAX" is the convention other VSRGs use.
    ///
    /// Implements <see cref="IToriiSkinComponent"/> so the toolbox can
    /// flag this entry visually as a Torii-custom addition (small
    /// torii-gate glyph + brand colour on the name) — distinguishes
    /// our additions from upstream lazer's at a glance.
    /// </summary>
    public abstract partial class ManiaRatioCounter : RollingCounter<double>, ISerialisableDrawable, IToriiSkinComponent
    {
        // Sentinel values flowed through the bindable so the formatter
        // can branch without needing extra state. Choosing double.NaN
        // for "no judgements yet" means the rolling counter doesn't try
        // to interpolate from a real ratio down to it (NaN comparisons
        // short-circuit the transform), which keeps the pre-first-hit
        // "--" stable instead of flickering.
        //
        // We previously also gated on a min_judgements_required = 15
        // threshold but it was load-bearing on a misunderstanding —
        // the worry was that 0 greats would yield NaN or a giant float
        // garbage value, but the `greats == 0 → sentinel_max` branch
        // below already coerces that case to "MAX" cleanly. Upstream
        // feedback flagged the 15 threshold as unnecessary and a bit
        // surprising (counter sat on "--" for the first second-or-so
        // of every map), so it was removed. The only remaining "not
        // enough data" state is `total == 0` — i.e. literally before
        // a single accuracy-affecting hit has landed.
        private const double sentinel_not_enough_data = double.NaN;
        private const double sentinel_max = double.PositiveInfinity;

        [Resolved]
        private ScoreProcessor scoreProcessor { get; set; } = null!;

        public bool UsesFixedAnchor { get; set; }

        // Snappy enough to feel responsive each judgement, slow enough
        // that we can see the value glide. Matches the feel of the
        // Argon accuracy counter rolling.
        protected override double RollingDuration => 200;

        protected ManiaRatioCounter()
        {
            // Boot in the "not enough data" state so the counter shows
            // "--" before the first judgement instead of "0.00".
            Current.Value = DisplayedCount = sentinel_not_enough_data;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            scoreProcessor.NewJudgement += onJudgement;
            scoreProcessor.JudgementReverted += onJudgement;

            // Scoring may already be in a sane state by the time we
            // load (e.g. if the HUD is recreated after the skin editor
            // closes). Run an initial recompute so the counter reflects
            // current state without waiting for the next hit.
            recomputeRatio();
        }

        private void onJudgement(JudgementResult _) => recomputeRatio();

        private void recomputeRatio()
        {
            // Read fresh from Statistics every time rather than tracking
            // a local count, so JudgementReverted (used for the scoring
            // rewind during replays / failure-rewind) is handled
            // correctly without us having to track the delta manually.
            int perfects = scoreProcessor.Statistics.GetValueOrDefault(HitResult.Perfect);
            int greats = scoreProcessor.Statistics.GetValueOrDefault(HitResult.Great);

            // Only "not enough data" state we still keep: literally zero
            // accuracy-affecting hits. Showing "MAX" before the player
            // has even started hitting notes (which is what the bare
            // `greats == 0 → sentinel_max` branch would do) reads as
            // misleading; "--" + "MAX flips on the first hit" reads as
            // honest. From the first judgement onwards we show the
            // live value.
            if (perfects + greats == 0)
            {
                Current.Value = sentinel_not_enough_data;
                return;
            }

            Current.Value = greats == 0 ? sentinel_max : (double)perfects / greats;
        }

        protected override LocalisableString FormatCount(double count)
        {
            if (double.IsNaN(count))
                return @"--";

            if (double.IsPositiveInfinity(count))
                return @"MAX";

            return count.ToString("0.00");
        }

        // Override the rolling transform so we don't try to animate
        // through NaN / Infinity (which would emit warning spam from
        // the framework's interpolation). When transitioning to or
        // from a sentinel, snap instantly.
        protected override void TransformCount(double currentValue, double newValue)
        {
            if (double.IsNaN(newValue) || double.IsInfinity(newValue)
                || double.IsNaN(currentValue) || double.IsInfinity(currentValue))
            {
                DisplayedCount = newValue;
                return;
            }

            base.TransformCount(currentValue, newValue);
        }

        protected override void Dispose(bool isDisposing)
        {
            if (scoreProcessor.IsNotNull())
            {
                scoreProcessor.NewJudgement -= onJudgement;
                scoreProcessor.JudgementReverted -= onJudgement;
            }

            base.Dispose(isDisposing);
        }
    }
}
