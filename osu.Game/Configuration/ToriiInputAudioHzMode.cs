// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.ComponentModel;

namespace osu.Game.Configuration
{
    /// <summary>
    /// Five-step preset for the input + audio thread rate. The numeric
    /// values double as the actual Hz the game pushes into
    /// <c>GameHost.ToriiInputAudioHz</c> at startup, so picking
    /// <see cref="Hz1000"/> in the UI is literally "set input+audio
    /// thread to 1000 Hz" — no separate lookup table required. The update
    /// thread follows the same value (see GameHost.updateFrameSyncMode), so
    /// the three loops move together.
    ///
    /// Defaults to <see cref="Hz2000"/> (Torii historical competitive
    /// default), but the first launch auto-lowers it on weak / old hardware
    /// via <see cref="ToriiInputAudioHzDefaults.ForThisMachine"/>. The lower
    /// options exist for users on weaker hardware who don't want their machine
    /// running the loop hot; the higher options are for capable CPUs chasing
    /// the tightest latency.
    ///
    /// Note: this setting only affects the *capped* rate. "I am stupid"
    /// mode (UnlimitedNoCap + AllowDangerousUnlimitedNoCap) still
    /// bypasses this entirely and runs the input/audio thread fully
    /// uncapped.
    /// </summary>
    public enum ToriiInputAudioHzMode
    {
        [Description("500 Hz (compatibility)")]
        Hz500 = 500,

        [Description("1000 Hz (lazer default)")]
        Hz1000 = 1000,

        [Description("2000 Hz (default)")]
        Hz2000 = 2000,

        [Description("4000 Hz")]
        Hz4000 = 4000,

        [Description("8000 Hz (high CPU)")]
        Hz8000 = 8000,
    }

    /// <summary>
    /// First-launch auto-tuning for <see cref="ToriiInputAudioHzMode"/>: a weak
    /// or old PC shouldn't open at the 2000 Hz competitive default and start
    /// hiccuping. Core count is the primary signal (running input/audio/update
    /// all at a high rate competes for cores); a RAM floor catches genuinely old
    /// machines. Deliberately conservative — only the clearly weak get lowered,
    /// a normal modern desktop stays at 2000. Used once to seed the default; the
    /// user's dropdown choice wins on every launch after that. Thresholds are
    /// easy to tune here.
    /// </summary>
    public static class ToriiInputAudioHzDefaults
    {
        public static ToriiInputAudioHzMode ForThisMachine()
        {
            int cores = Environment.ProcessorCount;
            double ramGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024d * 1024 * 1024);

            // Dual-core or barely any RAM: genuinely old / very weak.
            if (cores <= 2 || ramGb < 4)
                return ToriiInputAudioHzMode.Hz500;

            // Older budget quad-core (few cores AND low RAM): play it safe at lazer's rate.
            if (cores <= 4 && ramGb < 8)
                return ToriiInputAudioHzMode.Hz1000;

            return ToriiInputAudioHzMode.Hz2000;
        }
    }
}
