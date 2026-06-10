// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.ComponentModel;

namespace osu.Game.Configuration
{
    /// <summary>
    /// Five-step preset for the input + audio thread rate. The numeric
    /// values double as the actual Hz the game pushes into
    /// <c>GameHost.ToriiInputAudioHz</c> at startup, so picking
    /// <see cref="Hz1000"/> in the UI is literally "set input+audio
    /// thread to 1000 Hz" — no separate lookup table required.
    ///
    /// Defaults to <see cref="Hz2000"/> (Torii historical competitive
    /// default). The lower options exist for users on weaker hardware
    /// who don't want their machine running the input/audio loop hot
    /// just because they're playing osu!. The higher options are for
    /// users with capable CPUs who want the tightest possible latency
    /// and don't mind the thermal cost.
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

        [Description("1000 Hz")]
        Hz1000 = 1000,

        [Description("2000 Hz (default)")]
        Hz2000 = 2000,

        [Description("4000 Hz")]
        Hz4000 = 4000,

        [Description("8000 Hz (high CPU)")]
        Hz8000 = 8000,
    }
}
