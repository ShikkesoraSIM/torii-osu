// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace osu.Game.Mapperatorinator
{
    /// <summary>
    /// The generation settings a Mapperatorinator map was made with, stored as a small
    /// json file INSIDE the beatmapset itself. Travelling with the set (it survives
    /// export/import untouched, and doesn't participate in the set hash) is what lets
    /// the context menu offer "regenerate with the same settings" much later.
    /// </summary>
    public class MapperatorinatorSidecar
    {
        public const string FILENAME = @"mapperatorinator.json";

        [JsonPropertyName("model")]
        public string Model { get; set; } = nameof(MapperatorinatorModel.V32);

        [JsonPropertyName("gamemode")]
        public int Gamemode { get; set; }

        [JsonPropertyName("difficulty")]
        public double? Difficulty { get; set; }

        [JsonPropertyName("year")]
        public int? Year { get; set; }

        [JsonPropertyName("mapper_id")]
        public int? MapperId { get; set; }

        [JsonPropertyName("keycount")]
        public int? Keycount { get; set; }

        [JsonPropertyName("circle_size")]
        public double? CircleSize { get; set; }

        [JsonPropertyName("approach_rate")]
        public double? ApproachRate { get; set; }

        [JsonPropertyName("overall_difficulty")]
        public double? OverallDifficulty { get; set; }

        [JsonPropertyName("hp_drain_rate")]
        public double? HpDrainRate { get; set; }

        [JsonPropertyName("hitsounded")]
        public bool Hitsounded { get; set; } = true;

        [JsonPropertyName("super_timing")]
        public bool SuperTiming { get; set; }

        [JsonPropertyName("descriptors")]
        public List<string> Descriptors { get; set; } = new List<string>();

        [JsonPropertyName("negative_descriptors")]
        public List<string> NegativeDescriptors { get; set; } = new List<string>();

        /// <summary>Whether the user gave the map its own title/artist/background.</summary>
        [JsonPropertyName("customized")]
        public bool Customized { get; set; }

        public static MapperatorinatorSidecar FromRequest(MapperatorinatorRequest request, bool customized) => new MapperatorinatorSidecar
        {
            Model = request.Model.ToString(),
            Gamemode = (int)request.Gamemode,
            Difficulty = request.Difficulty,
            Year = request.Year,
            MapperId = request.MapperId,
            Keycount = request.Keycount,
            CircleSize = request.CircleSize,
            ApproachRate = request.ApproachRate,
            OverallDifficulty = request.OverallDifficulty,
            HpDrainRate = request.HpDrainRate,
            Hitsounded = request.Hitsounded,
            SuperTiming = request.SuperTiming,
            Descriptors = request.Descriptors.ToList(),
            NegativeDescriptors = request.NegativeDescriptors.ToList(),
            Customized = customized,
        };

        /// <summary>
        /// A fresh request with these settings. Seed and work directory are deliberately
        /// left for the caller: a regeneration wants a NEW seed, not the recorded one.
        /// </summary>
        public MapperatorinatorRequest ToRequest()
        {
            var request = new MapperatorinatorRequest
            {
                Model = Enum.TryParse<MapperatorinatorModel>(Model, out var parsed) ? parsed : MapperatorinatorModel.V32,
                Gamemode = Enum.IsDefined(typeof(MapperatorinatorGamemode), Gamemode) ? (MapperatorinatorGamemode)Gamemode : MapperatorinatorGamemode.Osu,
                Difficulty = Difficulty,
                Year = Year,
                MapperId = MapperId,
                Keycount = Keycount,
                CircleSize = CircleSize,
                ApproachRate = ApproachRate,
                OverallDifficulty = OverallDifficulty,
                HpDrainRate = HpDrainRate,
                Hitsounded = Hitsounded,
                SuperTiming = SuperTiming,
            };

            request.Descriptors.AddRange(Descriptors);
            request.NegativeDescriptors.AddRange(NegativeDescriptors);
            return request;
        }

        public string Serialize() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

        public static MapperatorinatorSidecar? Deserialize(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<MapperatorinatorSidecar>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
