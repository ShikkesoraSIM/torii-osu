// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;

namespace osu.Game.Scoring
{
    /// <summary>
    /// Torii: the server-side pause penalty, mirrored client-side so every surface
    /// (results screen, HUD, tooltips) shows the same number the server will award.
    ///
    /// The server (g0v0 <c>app/calculator.py</c>) applies <c>pp *= 0.93 ** pause_count</c>
    /// on submission — 7% per pause, compounding. The client's local
    /// <c>PerformanceCalculator</c> knows nothing about pauses, which is why the
    /// results screen and the website used to disagree whenever a play had pauses.
    /// If the multiplier ever changes server-side, change it here too.
    /// </summary>
    public static class ToriiPausePenalty
    {
        /// <summary>
        /// Multiplier applied to pp once per pause. Matches g0v0's calculator.
        /// </summary>
        public const double MULTIPLIER_PER_PAUSE = 0.93;

        /// <summary>
        /// Applies the pause penalty to an unpenalised pp value.
        /// </summary>
        public static double Apply(double pp, int pauseCount)
            => pauseCount <= 0 ? pp : pp * Math.Pow(MULTIPLIER_PER_PAUSE, pauseCount);

        /// <summary>
        /// Recovers the unpenalised pp from a server-awarded (already penalised) value,
        /// so the results screen can animate "full pp → slash → awarded pp".
        /// </summary>
        public static double Remove(double pp, int pauseCount)
            => pauseCount <= 0 ? pp : pp / Math.Pow(MULTIPLIER_PER_PAUSE, pauseCount);

        /// <summary>
        /// The total percentage lost to pauses (e.g. 7 for one pause, 13.5 for two).
        /// </summary>
        public static double TotalPercentLost(int pauseCount)
            => (1 - Math.Pow(MULTIPLIER_PER_PAUSE, pauseCount)) * 100;
    }
}
