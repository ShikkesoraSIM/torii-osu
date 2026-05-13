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
