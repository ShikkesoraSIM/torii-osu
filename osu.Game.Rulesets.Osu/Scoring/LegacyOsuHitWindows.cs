// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.Osu.Scoring
{
    /// <summary>
    /// Hit windows implementation that matches osu!Stable's hit window calculations.
    /// Uses Overall Difficulty (OD) directly instead of the difficulty range system.
    /// </summary>
    public class LegacyOsuHitWindows : HitWindows
    {
        /// <summary>
        /// osu!Stable has a fixed miss window regardless of OD.
        /// </summary>
        public const double MISS_WINDOW = 400;

        private double great;
        private double ok;
        private double meh;

        public override bool IsHitResultAllowed(HitResult result)
        {
            switch (result)
            {
                case HitResult.Great:
                case HitResult.Ok:
                case HitResult.Meh:
                case HitResult.Miss:
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Sets hit windows using osu!Stable's formula based on Overall Difficulty (OD).
        /// In osu!Stable:
        /// - 300 (Great): 80 - (OD * 6) milliseconds
        /// - 100 (Ok): 140 - (OD * 8) milliseconds
        /// - 50 (Meh): 200 - (OD * 10) milliseconds
        /// - Miss: 400 milliseconds (fixed)
        /// </summary>
        /// <param name="difficulty">The Overall Difficulty (OD) value [0, 10].</param>
        public override void SetDifficulty(double difficulty)
        {
            // osu!Stable uses OD directly, not the difficulty range system
            // Clamp OD to valid range [0, 10]
            double od = Math.Max(0, Math.Min(10, difficulty));

            // osu!Stable formula: 80 - (OD * 6)
            great = Math.Floor(80 - (od * 6)) - 0.5;

            // osu!Stable formula: 140 - (OD * 8)
            ok = Math.Floor(140 - (od * 8)) - 0.5;

            // osu!Stable formula: 200 - (OD * 10)
            meh = Math.Floor(200 - (od * 10)) - 0.5;
        }

        public override double WindowFor(HitResult result)
        {
            switch (result)
            {
                case HitResult.Great:
                    return great;

                case HitResult.Ok:
                    return ok;

                case HitResult.Meh:
                    return meh;

                case HitResult.Miss:
                    return MISS_WINDOW;

                default:
                    throw new ArgumentOutOfRangeException(nameof(result), result, null);
            }
        }
    }
}

