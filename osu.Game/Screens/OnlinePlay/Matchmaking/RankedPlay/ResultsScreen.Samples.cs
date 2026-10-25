// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Audio.Sample;
using osu.Framework.Graphics.Audio;
using osu.Framework.Logging;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay
{
    public partial class ResultsScreen
    {
        internal const string DAMAGE_HIT_SAMPLE = @"Multiplayer/Matchmaking/Ranked/Results/dmg-hit";
        internal const string DAMAGE_HIT_FALLBACK_SAMPLE = @"Multiplayer/Matchmaking/Ranked/Results/dmg-hit-medium";

        internal static DrawableSample LoadResultSample(ISampleStore samples, string name, string? fallbackName = null)
        {
            Sample? sample = samples.Get(name);

            if (sample == null && fallbackName != null)
                sample = samples.Get(fallbackName);

            if (sample == null)
                Logger.Log($"Ranked play results sample missing: {name}", level: LogLevel.Important);

            return new DrawableSample(sample ?? new SampleVirtual(name));
        }
    }
}
