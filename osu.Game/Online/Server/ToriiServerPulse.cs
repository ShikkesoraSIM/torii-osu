// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Online.Server
{
    /// <summary>
    /// Process-wide read-once flag for the Torii server-pulse widget. Set at
    /// startup from <see cref="osu.Game.Configuration.OsuSetting.ToriiServerPulseEnabled"/>.
    ///
    /// When disabled the pulse is FULLY off, not just hidden: the polling
    /// provider is never loaded and the toolbar button removes itself, so there
    /// is zero background work (no network, no per-frame, no subscriptions).
    /// Because the decision is taken once at startup, the settings toggle prompts
    /// for a restart so a change takes full effect.
    /// </summary>
    public static class ToriiServerPulse
    {
        private static bool enabled = true;

        /// <summary>
        /// True when the server pulse was enabled at app startup. Immutable for
        /// the process lifetime; toggling the setting only takes effect after a restart.
        /// </summary>
        public static bool Enabled => enabled;

        /// <summary>Pin the flag from config before the toolbar + provider are built.</summary>
        public static void SetFromConfig(bool value) => enabled = value;
    }
}
