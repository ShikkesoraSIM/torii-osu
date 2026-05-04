// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.Mania.Skinning
{
    /// <summary>
    /// VSRG-style "ratio" counter for osu!mania — Perfects (MAX / 300+)
    /// divided by Greats (the regular 300). It's a measure of timing
    /// precision beyond what plain accuracy can express: two players
    /// can both have 99% accuracy while one has a much higher PA ratio
    /// (more MAXes, fewer 300s) than the other, indicating tighter
    /// timing.
    ///
    /// Display semantics:
    /// - Below <see cref="min_judgements_required"/> total Perfect+Great
    ///   hits, the counter shows "--". This kills the wild value swings
    ///   at the very start of a map (a single Perfect with no Greats
    ///   reads as ∞ for example) so players don't get a misleading
    ///   number until enough samples have landed.
    /// - With at least <see cref="min_judgements_required"/> hits and
    ///   non-zero Greats, displays the ratio with two decimals (e.g.
    ///   <c>2.34</c>).
    /// - With non-zero Perfects but zero Greats, displays "MAX" — the
    ///   ratio is technically infinite but "∞" reads weird in a HUD,
    ///   "MAX" is the convention other VSRGs use.
    ///
    /// This base class is purely the data + formatting / animation
    /// machinery. Visual variants live next to the other skin pieces:
    /// <see cref="Default.DefaultManiaRatioCounter"/> for the Torii /
    /// Argon / Triangles look, <see cref="Legacy.LegacyManiaRatioCounter"/>
    /// for skins using bitmap score digits.
    ///
    /// Implements <see cref="ISerialisableDrawable"/> so the in-game
    /// skin layout editor lists this component in its "Add component"
    /// menu and lets the user reposition it freely.
    /// </summary>
    public abstract partial class ManiaRatioCounter : RollingCounter<double>, ISerialisableDrawable
    {
        /// <summary>
        /// Minimum number of accuracy-affecting hits we need before the
        /// counter shows a number. Everything below this threshold
        /// renders as "--". Picked at the request of the original
        /// feature suggestion to avoid the value bouncing around (e.g.
        /// from ∞ to 1.5 to 4.0) in the first few seconds of a map
        /// before enough samples have accumulated.
        /// </summary>
        private const int min_judgements_required = 15;

        // Sentinel values flowed through the bindable so the formatter
        // can branch without needing extra state. Choosing
        // double.NaN for "not enough data" means the rolling counter
        // doesn't try to interpolate from a real ratio down to it
        // (NaN comparisons short-circuit the transform), which keeps
        // the early-game "--" stable instead of flickering.
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

            // Scoring is in a sane state already by the time we load
            // (e.g. if a HUD component is recreated mid-play after the
            // skin editor closes). Run an initial recompute so the
            // counter reflects current state without waiting for the
            // next hit.
            recomputeRatio();
        }

        private void onJudgement(JudgementResult _) => recomputeRatio();

        private void recomputeRatio()
        {
            // Read fresh from Statistics every time rather than
            // tracking a local count, so JudgementReverted (used for
            // the scoring rewind during replays / failure-rewind) is
            // handled correctly without us having to track the delta.
            int perfects = scoreProcessor.Statistics.GetValueOrDefault(HitResult.Perfect);
            int greats = scoreProcessor.Statistics.GetValueOrDefault(HitResult.Great);

            int total = perfects + greats;

            if (total < min_judgements_required)
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
