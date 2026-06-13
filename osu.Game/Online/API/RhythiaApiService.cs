// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.IO.Network;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.API.Requests;

namespace osu.Game.Online.API
{
    public class RhythiaApiService
    {
        private readonly IAPIProvider api;

        public RhythiaApiService(IAPIProvider api)
        {
            this.api = api;
        }

        public async Task<List<RhythiaBeatmapSet>> Search(string query, int page)
        {
            var request = new RhythiaSearchRequest(query, page);
            await api.PerformAsync(request);
            return request.Response;
        }

        public APIBeatmapSet ToAPIBeatmapSet(RhythiaBeatmapSet rhythiaSet)
        {
            var beatmapSet = new APIBeatmapSet
            {
                OnlineID = rhythiaSet.OnlineID,
                Title = rhythiaSet.Title,
                TitleUnicode = rhythiaSet.Title,
                Artist = rhythiaSet.Artist,
                ArtistUnicode = rhythiaSet.Artist,
                Author = new APIUser { Username = rhythiaSet.Author },
                AuthorID = 0, // Will be filled when map is imported
                AuthorString = rhythiaSet.Author,
                BPM = rhythiaSet.BPM,
                Preview = rhythiaSet.Preview,
                Covers = new BeatmapSetOnlineCovers
                {
                    Cover = rhythiaSet.Background,
                    Card = rhythiaSet.Background,
                    List = rhythiaSet.Background
                },
                Tags = rhythiaSet.Tags,
                Status = BeatmapOnlineStatus.Ranked, // Set to ranked for SSPM maps
                Source = string.Empty,
                Beatmaps = new APIBeatmap[] { }
            };

            var beatmaps = new List<APIBeatmap>();
            foreach (var rhythiaBeatmap in rhythiaSet.Beatmaps)
            {
                beatmaps.Add(new APIBeatmap
                {
                    OnlineID = rhythiaBeatmap.OnlineID,
                    OnlineBeatmapSetID = rhythiaSet.OnlineID,
                    RulesetID = 727, // osu!space ruleset ID
                    DifficultyName = rhythiaBeatmap.Version,
                    StarRating = rhythiaBeatmap.Difficulty,
                    CircleSize = (float)rhythiaBeatmap.CS,
                    ApproachRate = (float)rhythiaBeatmap.AR,
                    OverallDifficulty = (float)rhythiaBeatmap.OD,
                    DrainRate = (float)rhythiaBeatmap.HP,
                    Length = rhythiaBeatmap.Length,
                    HitLength = rhythiaBeatmap.Length,
                    Status = BeatmapOnlineStatus.Ranked, // Set to ranked for SSPM maps
                    Checksum = string.Empty, // Will be filled when map is imported
                    AuthorID = 0, // Will be filled when map is imported
                    PlayCount = 0,
                    UserPlayCount = 0,
                    PassCount = 0,
                    Convert = false,
                    CircleCount = 0,
                    SliderCount = 0,
                    SpinnerCount = 0
                });
            }

            beatmapSet.Beatmaps = beatmaps.ToArray();
            return beatmapSet;
        }
    }
}
