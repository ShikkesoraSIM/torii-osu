// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using Newtonsoft.Json;
using osu.Game.Rulesets.Difficulty;

namespace osu.Game.Rulesets.Mania.Difficulty
{
    public class ManiaPerformanceAttributes : PerformanceAttributes
    {
        [JsonProperty("difficulty")]
        public double Difficulty { get; set; }

        [JsonProperty("variety_multiplier")]
        public double VarietyMultiplier { get; set; }

        [JsonProperty("acc_multiplier")]
        public double AccMultiplier { get; set; }

        [JsonProperty("length_multiplier")]
        public double LengthMultiplier { get; set; }

        public override IEnumerable<PerformanceDisplayAttribute> GetAttributesForDisplay()
        {
            foreach (var attribute in base.GetAttributesForDisplay())
                yield return attribute;

            yield return new PerformanceDisplayAttribute(nameof(Difficulty), "Difficulty", Difficulty);
            yield return new PerformanceDisplayAttribute(nameof(VarietyMultiplier), "Variety Multiplier", VarietyMultiplier);
            yield return new PerformanceDisplayAttribute(nameof(AccMultiplier), "Acc Multiplier", AccMultiplier);
            yield return new PerformanceDisplayAttribute(nameof(LengthMultiplier), "Total Notes", LengthMultiplier);
        }
    }
}
