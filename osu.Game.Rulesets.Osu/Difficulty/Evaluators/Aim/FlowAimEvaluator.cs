// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Utils;
using osu.Game.Rulesets.Osu.Difficulty.Preprocessing;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.Osu.Difficulty.Evaluators.Aim
{
    public static class FlowAimEvaluator
    {
        private const double velocity_change_multiplier = 0.52;

        // Multipliers for flow aim on relax
        private const double flow_angular_velocity_scale = 270.0;
        private const double relax_washing_machine_velocity_threshold = 1.2; // Speed needed to define the difference between a normal stream and a washing machine
        private const double relax_circularity_angular_threshold = 200.0; // How sharp a player cursor should move in order to be awarded for it
        private const double relax_acceleration_buff_multiplier = 0.2; //How much of the acceleration difficulty bonus the player keeps when relax is on

        /// <summary>
        /// Evaluates difficulty of "flow aim" - aiming pattern where player doesn't stop their cursor on every object and instead "flows" through them.
        /// </summary>
        public static double EvaluateDifficultyOf(DifficultyHitObject current, bool withSliderTravelDistance, bool isRelax = false)
        {
            if (current.BaseObject is Spinner || current.Index <= 1 || current.Previous(0).BaseObject is Spinner)
                return 0;

            var osuCurrObj = (OsuDifficultyHitObject)current;
            var osuLastObj = (OsuDifficultyHitObject)current.Previous(0);
            var osuLastLastObj = (OsuDifficultyHitObject)current.Previous(1);

            double currDistance = withSliderTravelDistance ? osuCurrObj.LazyJumpDistance : osuCurrObj.JumpDistance;
            double prevDistance = withSliderTravelDistance ? osuLastObj.LazyJumpDistance : osuLastObj.JumpDistance;

            double currVelocity = currDistance / osuCurrObj.AdjustedDeltaTime;

            if (osuLastObj.BaseObject is Slider && withSliderTravelDistance)
            {
                // If the last object is a slider, then we extend the travel velocity through the slider into the current object.
                double sliderDistance = osuLastObj.LazyTravelDistance + osuCurrObj.LazyJumpDistance;
                currVelocity = Math.Max(currVelocity, sliderDistance / osuCurrObj.AdjustedDeltaTime);
            }

            double prevVelocity = prevDistance / osuLastObj.AdjustedDeltaTime;

            double flowDifficulty = currVelocity;

            // If Relax is active, I chose to completely strip the SmallCircleBonus for flowing objects. This can be heightened if it feels too harsh.
            if (!isRelax)
            {
                flowDifficulty *= Math.Sqrt(osuCurrObj.SmallCircleBonus);
            }

            // Rhythm changes are harder to flow
            flowDifficulty *= 1 + Math.Min(0.25,
                Math.Pow((Math.Max(osuCurrObj.AdjustedDeltaTime, osuLastObj.AdjustedDeltaTime) - Math.Min(osuCurrObj.AdjustedDeltaTime, osuLastObj.AdjustedDeltaTime)) / 50, 4));

            if (osuCurrObj.Angle != null && osuLastObj.Angle != null)
            {
                double angleDifference = Math.Abs(osuCurrObj.Angle.Value - osuLastObj.Angle.Value);
                double angleDifferenceAdjusted = Math.Sin(angleDifference / 2) * 180.0;
                double angularVelocity = angleDifferenceAdjusted / (osuCurrObj.AdjustedDeltaTime * 0.1);

                // Low angular velocity flow (angles are consistent) is easier to follow than erratic flow
                double angularMultiplier = 0.8 + Math.Sqrt(angularVelocity / flow_angular_velocity_scale);

                // this is for washing machines type streams 
                if (isRelax)
                {
                    // Trigger threshold heightened to 1.2 used to be 1.0 (catches even medium-sized circular abuse)
                    if (currVelocity > relax_washing_machine_velocity_threshold)
                    {
                        // The wider the curve is the harsher will be the flow aim nerf, killing abuse maps 
                        double flowConsistencyKeep = DifficultyCalculationUtils.Smoothstep(angularVelocity, 0, relax_circularity_angular_threshold);

                        // Exponential velocity penalty: the larger the circle, the harder the multiplier drops, it should be able to kill pp completely at cs0 
                        double velocityPenalty = Math.Pow(relax_washing_machine_velocity_threshold / currVelocity, 2);

                        angularMultiplier *= flowConsistencyKeep + (1.0 - flowConsistencyKeep) * velocityPenalty;
                    }
                }

                flowDifficulty *= angularMultiplier;
            }

            // If all three notes are overlapping - don't reward bonuses as you don't have to do additional movement
            double overlappedNotesWeight = 1;

            if (current.Index > 2)
            {
                double o1 = calculateOverlapFactor(osuCurrObj, osuLastObj);
                double o2 = calculateOverlapFactor(osuCurrObj, osuLastLastObj);
                double o3 = calculateOverlapFactor(osuLastObj, osuLastLastObj);

                overlappedNotesWeight = 1 - o1 * o2 * o3;
            }

            if (osuCurrObj.Angle != null)
            {
                // Acute angles are also hard to flow
                flowDifficulty += currVelocity *
                                  SnapAimEvaluator.CalcAngleAcuteness(osuCurrObj.Angle.Value) *
                                  overlappedNotesWeight;
            }

            if (Math.Max(prevVelocity, currVelocity) != 0)
            {
                if (withSliderTravelDistance)
                {
                    currVelocity = currDistance / osuCurrObj.AdjustedDeltaTime;
                }

                // Scale with ratio of difference compared to 0.5 * max dist.
                double distRatio = DifficultyCalculationUtils.Smoothstep(Math.Abs(prevVelocity - currVelocity) / Math.Max(prevVelocity, currVelocity), 0, 1);

                // Reward for % distance up to 125 / strainTime for overlaps where velocity is still changing.
                double overlapVelocityBuff = Math.Min(OsuDifficultyHitObject.NORMALISED_DIAMETER * 1.25 / Math.Min(osuCurrObj.AdjustedDeltaTime, osuLastObj.AdjustedDeltaTime),
                    Math.Abs(prevVelocity - currVelocity));

                // acceleration buff nerf
                if (isRelax)
                {
                    // Trivial to track acceleration/deceleration streams in Relax, reduce the buff by 80%
                    overlapVelocityBuff *= relax_acceleration_buff_multiplier;
                }

                flowDifficulty += overlapVelocityBuff *
                                  distRatio *
                                  overlappedNotesWeight *
                                  velocity_change_multiplier;
            }

            if (osuCurrObj.BaseObject is Slider && withSliderTravelDistance)
            {
                // Include slider velocity to make velocity more consistent with snap
                flowDifficulty += osuCurrObj.TravelDistance / osuCurrObj.TravelTime;
            }

            // Final velocity is being raised to a power because flow difficulty scales harder with both high distance and time, and we want to account for that
            flowDifficulty = Math.Pow(flowDifficulty, 1.45);

            // Reduce difficulty for low spacing since spacing below radius is always to be flowed
            return flowDifficulty * DifficultyCalculationUtils.Smootherstep(currDistance, 0, OsuDifficultyHitObject.NORMALISED_RADIUS);
        }

        private static double calculateOverlapFactor(OsuDifficultyHitObject first, OsuDifficultyHitObject second)
        {
            var firstBase = (OsuHitObject)first.BaseObject;
            var secondBase = (OsuHitObject)second.BaseObject;
            double objectRadius = firstBase.Radius;

            double distance = Vector2.Distance(firstBase.StackedPosition, secondBase.StackedPosition);
            return Math.Clamp(1 - Math.Pow(Math.Max(distance - objectRadius, 0) / objectRadius, 2), 0, 1);
        }
    }
}
