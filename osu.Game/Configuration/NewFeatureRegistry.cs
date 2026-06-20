// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;

namespace osu.Game.Configuration
{
    /// <summary>
    /// Central registry of every "[NEW]" feature ID in the game. Adding
    /// a new badge call site without registering the ID here is a no-op
    /// at runtime — <see cref="NewFeatureTracker.ShouldShowBadge"/>
    /// rejects unknown IDs as a defence against typo'd call sites
    /// creating undismissable phantom badges.
    ///
    /// Convention for IDs
    /// ------------------
    /// <c>vYYYY.MDD.N:kebab-case-name</c>
    ///
    /// The version prefix is the release the feature ships in (or the
    /// next one if it hasn't shipped yet), the suffix is a short
    /// human-readable feature name. The prefix lets you audit "what
    /// got a NEW badge in release X" with a single grep, and lets you
    /// retire stale IDs by version when cleaning up the registry.
    ///
    /// Retiring IDs
    /// ------------
    /// When a feature stops being "new" (e.g. two releases later), drop
    /// its constant from this file. The JSON file on disk will still
    /// contain a stale entry for users who'd already partially viewed
    /// the badge, but <see cref="NewFeatureTracker.ShouldShowBadge"/>
    /// will reject the unknown ID and return false, so the badge
    /// simply stops appearing. No migration needed.
    ///
    /// Adding a new entry
    /// ------------------
    /// 1. Add a <c>public const string</c> here using the version prefix
    ///    of the release the feature ships in.
    /// 2. Reference it from the badge call site by setting the host
    ///    form control's NewFeatureId init property:
    ///    <code>
    ///    new FormEnumDropdown&lt;T&gt;
    ///    {
    ///        Caption = "...",
    ///        NewFeatureId = NewFeatureRegistry.FooThing,
    ///        ...
    ///    }
    ///    </code>
    ///    The pill renders inline inside the control's caption row (to
    ///    the right of the tooltip "?" icon) and dismisses after the
    ///    user interacts with the control the threshold number of
    ///    times. Currently FormDropdown / FormEnumDropdown support
    ///    NewFeatureId; extend other form controls following the same
    ///    plumb-through pattern when a new badge call site needs them.
    /// 3. Done. The tracker will start counting interactions for the
    ///    new ID on the next app launch.
    /// </summary>
    public static class NewFeatureRegistry
    {
        // ---------------------------------------------------------------
        // v2026.514.x — first release shipping the NEW-badge framework.
        // ---------------------------------------------------------------

        /// <summary>
        /// Marks the "UI theme" dropdown surfaced in Settings → Skin
        /// and Settings → Torii → Interface. The dropdown lets the
        /// user switch between the default Torii palette and the
        /// "Grayscale by fsyori" desaturated palette — a significant
        /// new chrome option in this release and the headline novelty
        /// the badge wants to draw attention to. Both call sites mount
        /// the same <see cref="osu.Game.Overlays.Settings.UIThemeDropdownAndRestart"/>
        /// drawable, which sets <c>NewFeatureId</c> on its internal
        /// <see cref="osu.Game.Graphics.UserInterfaceV2.FormEnumDropdown{T}"/>,
        /// so the pill appears uniformly inline-next-to-the-tooltip in
        /// both settings sections.
        /// </summary>
        public const string UITheme = "v2026.514.0:ui-theme";

        // ---------------------------------------------------------------
        // v2026.522.x — first release shipping the in-game NSFW media
        // toggle (mirror of the website's profile_media_show_nsfw
        // preference) inside the Torii Interface settings subsection.
        // ---------------------------------------------------------------

        /// <summary>
        /// Marks the "Show NSFW profile media" toggle at the top of
        /// Settings → Torii → Interface. The flag controls whether the
        /// server serves real avatar / cover URLs for users who have
        /// flagged their profile as NSFW (the in-game counterpart of the
        /// same toggle on the website's User Preferences page). Brand
        /// new in-game surface, hence the badge — once the user has
        /// interacted with it the threshold number of times, the
        /// tracker drops the badge automatically.
        /// </summary>
        public const string NsfwProfileMedia = "v2026.522.0:nsfw-profile-media";

        /// <summary>
        /// Marks the "Input/audio thread rate" dropdown in Settings → Torii →
        /// Interface. The rate used to be hardcoded to 2000 Hz across every
        /// frame-sync mode (introduced with the torii_input_audio_hz constant
        /// in osu-framework); now users pick from 2000 / 4000 / 8000 Hz so
        /// high-polling-rate mice can run faster while weaker machines stay on
        /// the safe 2000 Hz default. Badge calls attention to a setting users
        /// wouldn't otherwise notice exists.
        /// </summary>
        public const string InputAudioHz = "v2026.522.0:input-audio-hz";

        // ---------------------------------------------------------------
        // v2026.602.x — first release shipping the mid-map "skip break"
        // button (Settings → Torii → Gameplay).
        // ---------------------------------------------------------------

        /// <summary>
        /// Marks the "Skip breaks mid-map" toggle in Settings → Torii →
        /// Gameplay. The flag enables a SKIP button that appears during a
        /// map's break periods and seeks the gameplay clock to the end of
        /// the break (community-requested QoL for maps with very long
        /// breaks). Brand-new gameplay surface, so the badge draws the
        /// user's eye to a setting that's ON by default but easy to miss.
        /// </summary>
        public const string SkipBreaks = "v2026.602.0:skip-breaks";

        /// <summary>
        /// Marks the "Skip breaks with a single confirmation" toggle in
        /// Settings → Torii → Gameplay (the companion to <see cref="SkipBreaks"/>).
        /// Flips the mid-map skip button from the default double-press
        /// confirmation to a single press.
        /// </summary>
        public const string SkipBreaksSingleConfirmation = "v2026.602.0:skip-breaks-confirm";

        // ---------------------------------------------------------------
        // v2026.618.x — Retry/Quit long-attempt confirm + Android Oboe
        // low-latency audio.
        // ---------------------------------------------------------------

        /// <summary>
        /// Marks the "Confirm Retry/Quit on long attempts" toggle in
        /// Settings → Torii → Gameplay. After ~60 s of active gameplay the
        /// Retry and Quit buttons on the pause / fail overlays require a
        /// second click within a short window, so an accidental tap can't
        /// throw away a long run.
        /// </summary>
        public const string ConfirmDangerousButtons = "v2026.618.0:confirm-dangerous-buttons";

        /// <summary>
        /// Marks the "Low-latency audio (Oboe)" toggle in Settings → Torii →
        /// Android (Android builds only). Routes audio through Google's Oboe
        /// library for AAudio MMAP-exclusive output, cutting Android audio
        /// latency dramatically on supported devices.
        /// </summary>
        public const string OboeAudio = "v2026.618.0:oboe-audio";

        /// <summary>
        /// Marks the "Skin the song-select footer" toggle in Settings → Torii →
        /// Interface. When on, the legacy song-select footer renders the active
        /// skin's own textures (with stable's positioning); when off, a clean
        /// consistent bundled footer is used regardless of skin.
        /// </summary>
        public const string LegacyFooterSkin = "v2026.618.0:legacy-footer-skin";

        /// <summary>
        /// Marks the anti-chatter "Key debounce" toggle in Settings → Input
        /// (mirrored in Settings → Torii). Filters the spurious double-taps that
        /// rapid-trigger / hall-effect keyboards and worn switches produce.
        /// </summary>
        public const string KeyDebounce = "v2026.610.0:key-debounce";

        /// <summary>
        /// Marks the "Auto-hide toolbar" toggle in Settings → Torii → Interface. Hides the toolbar
        /// until you move the cursor to the top of the screen (great for the legacy song select, where
        /// a hidden toolbar gives the full stable-style layout).
        /// </summary>
        public const string AutoHideToolbar = "v2026.619.0:auto-hide-toolbar";

        /// <summary>
        /// Marks the "Legacy footer" toggle in Settings -> Torii -> Song Select. When on, the
        /// stable-style song-select footer (back / mode / mods / random / options + rank panel) is
        /// shown even with the rest of the modern lazer chrome. It is forced on and bundled with the
        /// "Legacy (stable-style) song select" option, so the badge points users to the standalone switch.
        /// </summary>
        public const string LegacySongSelectFooter = "v2026.620.0:legacy-song-select-footer";

        /// <summary>
        /// Set of every registered ID. Built once at startup from the
        /// constants above via reflection-free explicit enumeration —
        /// kept as a hand-written list rather than reflecting over the
        /// class so adding a new const is the only thing required to
        /// register, and so refactoring tools can't accidentally orphan
        /// an entry by renaming the constant without updating the list.
        /// </summary>
        private static readonly HashSet<string> known_ids = new HashSet<string>
        {
            UITheme,
            NsfwProfileMedia,
            InputAudioHz,
            SkipBreaks,
            SkipBreaksSingleConfirmation,
            ConfirmDangerousButtons,
            OboeAudio,
            LegacyFooterSkin,
            KeyDebounce,
            AutoHideToolbar,
            LegacySongSelectFooter,
        };

        /// <summary>
        /// True if the given feature ID has been registered. Defensive
        /// check used by <see cref="NewFeatureTracker"/> to reject typo'd
        /// or removed IDs at runtime — see the class summary for why
        /// unknown IDs must NOT trigger a badge.
        /// </summary>
        public static bool IsKnown(string featureId) => known_ids.Contains(featureId);
    }
}
