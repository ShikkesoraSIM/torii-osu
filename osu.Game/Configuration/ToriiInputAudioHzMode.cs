// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.ComponentModel;

namespace osu.Game.Configuration
{
    /// <summary>
    /// Torii: selectable rate (Hz) for the input / audio / update thread pipeline.
    /// The enum values ARE the Hz, so the chosen value can be fed straight into
    /// <c>GameHost.ToriiInputAudioHz</c> via a plain cast.
    /// </summary>
    public enum ToriiInputAudioHzMode
    {
        [Description("500 Hz")]
        Hz500 = 500,

        [Description("1000 Hz")]
        Hz1000 = 1000,

        [Description("2000 Hz (recommended)")]
        Hz2000 = 2000,

        [Description("4000 Hz")]
        Hz4000 = 4000,

        [Description("8000 Hz")]
        Hz8000 = 8000,
    }
}
