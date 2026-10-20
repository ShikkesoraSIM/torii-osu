// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Utils;

namespace osu.Game.Beatmaps
{
    /// <summary>
    /// Provides a fast heuristic approximation of mod-adjusted star rating,
    /// for use when an exact difficulty calculation is not yet available.
    /// </summary>
    public static class StarRatingApproximator
    {
        /// <summary>
        /// Determines whether a given mod alters beatmap difficulty / star rating.
        /// </summary>
        public static bool ModAffectsDifficulty(Mod mod) =>
            mod is IApplicableToDifficulty || mod is IApplicableToRate || mod is ModFlashlight || mod is ModHidden
            || mod is ModRelax || mod is ModTouchDevice || mod is IAffectsDifficulty;

        /// <summary>
        /// Computes a fast O(1) approximation of star rating when difficulty-altering mods are applied
        /// and the exact calculation is not yet available in the cache.
        /// </summary>
        public static double Approximate(IBeatmapInfo beatmapInfo, IRulesetInfo? rulesetInfo, IEnumerable<Mod> mods)
        {
            double rate = ModUtils.CalculateRateWithMods(mods);
            var flattenedMods = ModUtils.FlattenMods(mods).ToArray();
            return Approximate(beatmapInfo, rulesetInfo, rate, flattenedMods);
        }

        // overload used by carousel lookups to avoid re-flattening mods per beatmap
        public static double Approximate(IBeatmapInfo beatmapInfo, IRulesetInfo? rulesetInfo, double rate, IReadOnlyList<Mod> flattenedMods)
        {
            double baseStars = beatmapInfo.StarRating;
            if (baseStars <= 0)
                return baseStars;

            string rulesetName = rulesetInfo?.ShortName ?? beatmapInfo.Ruleset?.ShortName ?? @"osu";
            double multiplier = 1.0;

            switch (rulesetName)
            {
                case "taiko":
                    if (Math.Abs(rate - 1.0) > 0.001)
                    {
                        double exp = rate > 1.0 ? 0.726 : 0.85;
                        multiplier *= Math.Pow(rate, exp);
                    }

                    foreach (var mod in flattenedMods)
                    {
                        switch (mod)
                        {
                            case ModHardRock:
                                multiplier *= 1.05;
                                break;

                            case ModEasy:
                                multiplier *= 0.90;
                                break;
                        }
                    }
                    break;

                case "fruits":
                    if (Math.Abs(rate - 1.0) > 0.001)
                    {
                        double exp = rate > 1.0 ? 0.602 : 0.75;
                        multiplier *= Math.Pow(rate, exp);
                    }

                    foreach (var mod in flattenedMods)
                    {
                        switch (mod)
                        {
                            case ModHardRock:
                                multiplier *= 1.10;
                                break;

                            case ModEasy:
                                multiplier *= 0.85;
                                break;
                        }
                    }
                    break;

                case "mania":
                    if (Math.Abs(rate - 1.0) > 0.001)
                    {
                        double exp = rate > 1.0 ? 0.430 : 0.55;
                        multiplier *= Math.Pow(rate, exp);
                    }
                    break;

                case "osu":
                default:
                    var diff = beatmapInfo.Difficulty;
                    double cs = diff?.CircleSize ?? 4f;

                    // Clock rate scaling
                    if (Math.Abs(rate - 1.0) > 0.001)
                    {
                        if (rate > 1.0)
                        {
                            // In Torii pp-dev, star rating is driven by the norm of Aim, Speed, and Reading skills.
                            // The effective clock rate scaling exponent falls linearly from 0.57 at 0★ to 0.45 at 1.5★,
                            // then rises with base star rating towards 0.97 (~0.71 at 4★, ~0.85 at 6.6★, ~0.89 at 8★, ~0.94 at 12★).
                            double rateExp = baseStars <= 1.5
                                ? 0.45 + 0.12 * Math.Max(0, 1.5 - baseStars) / 1.5
                                : 0.45 + 0.52 * (1.0 - Math.Exp(-0.28 * (baseStars - 1.5)));

                            multiplier *= Math.Pow(rate, rateExp);
                        }
                        else
                        {
                            // HT scaling (rate < 1.0): drops faster for high star maps due to collapsing speed/reading strains
                            double rateExp = 0.58 + 0.35 * Math.Clamp((baseStars - 1.5) / 7.0, 0.0, 1.0);
                            multiplier *= Math.Pow(rate, rateExp);
                        }
                    }

                    // Additional mod multipliers
                    foreach (var mod in flattenedMods)
                    {
                        switch (mod)
                        {
                            case ModHardRock:
                            {
                                double hrFactor = 1.05 + 0.008 * Math.Min(8.0, baseStars) + 0.01 * Math.Max(0, cs - 5.0);
                                multiplier *= hrFactor;
                                break;
                            }

                            case ModEasy:
                            {
                                if (diff != null && diff.ApproachRate >= 7.0f)
                                {
                                    // In Torii pp-dev, EZ halves AR. On higher-AR and higher-star maps, dense overlapping objects
                                    // produce heavy reading strain (raising star rating), whereas low-AR/starter maps see a difficulty reduction.
                                    double ar = diff.ApproachRate;
                                    double ezFactor = 0.85 + 0.03 * Math.Clamp(baseStars - 2.0, 0.0, 5.0) + 0.025 * (ar - 7.0);
                                    multiplier *= ezFactor;
                                }
                                else
                                {
                                    multiplier *= 0.85;
                                }
                                break;
                            }

                            case ModDifficultyAdjust da:
                            {
                                if (diff != null)
                                {
                                    if (da.SettingsMap.TryGetValue("circle_size", out var csSetting) && csSetting is IBindable<float?> csBindable && csBindable.Value is float csVal)
                                        multiplier *= Math.Pow(1.05, csVal - diff.CircleSize);
                                    if (da.SettingsMap.TryGetValue("approach_rate", out var arSetting) && arSetting is IBindable<float?> arBindable && arBindable.Value is float arVal)
                                        multiplier *= Math.Pow(1.02, arVal - diff.ApproachRate);
                                }
                                break;
                            }

                            case ModFlashlight:
                            {
                                multiplier *= 1.15;
                                break;
                            }

                            case ModHidden:
                                multiplier *= 1.035;
                                break;
                        }
                    }
                    break;
            }

            return baseStars * multiplier;
        }
    }
}
