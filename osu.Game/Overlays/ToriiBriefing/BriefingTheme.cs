// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions.Color4Extensions;
using osuTK.Graphics;

namespace osu.Game.Overlays.ToriiBriefing
{
    /// <summary>
    /// Single source of truth for every visual constant in the Torii Briefing
    /// overlay — colours, spacing, corner radii, type sizes. Pre-redesign
    /// these were scattered across the 1700-line monolith as inline literals
    /// (seven distinct accent colours, four corner radii, four trim lengths,
    /// no spacing scale). Centralising them makes the whole overlay feel
    /// like one system rather than a patchwork.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Naming follows Apple's Human Interface Guidelines vocabulary so the
    /// constants are self-documenting if you've worked with SwiftUI / UIKit:
    /// <c>BodyText</c>, <c>CornerLg</c>, <c>SpacingMd</c>, etc.
    /// </para>
    /// <para>
    /// Spacing uses an 8-point baseline grid. Mixing in odd-pixel offsets
    /// (3px, 14px, 22px) in the original made nothing line up. With the
    /// scale below every gap is a multiple of 4, and the layout snaps to
    /// the grid automatically.
    /// </para>
    /// <para>
    /// Corner radii are concentric: a card sits inside a panel, so the
    /// panel's corner has to be visibly LARGER than the card's by at
    /// least the surrounding padding, otherwise the eye reads the card
    /// as escaping the panel. The Apple rule of thumb is
    /// <c>outer = inner + padding</c>; <c>CornerLg</c> (panel) and
    /// <c>CornerMd</c> (card) below satisfy that with the
    /// <c>SpacingLg</c> padding we ship.
    /// </para>
    /// </remarks>
    internal static class BriefingTheme
    {
        // ─── Colour palette ────────────────────────────────────────────
        // Two brand accents (cyan / pink), four semantic ones. Prefer
        // these over hardcoding hex strings inline; keeps a future
        // re-skin to a single edit.

        /// <summary>Brand cyan. Used as default accent (rank, sync, neutral cards).</summary>
        public static readonly Color4 AccentCyan = Color4Extensions.FromHex(@"69d7ff");

        /// <summary>Brand pink. Used as the panel's signature glow + the primary CTA.</summary>
        public static readonly Color4 AccentPink = Color4Extensions.FromHex(@"ff66b3");

        /// <summary>Mint — score gains, positive movement.</summary>
        public static readonly Color4 AccentGain = Color4Extensions.FromHex(@"8bffcf");

        /// <summary>Coral — score losses, negative movement.</summary>
        public static readonly Color4 AccentLoss = Color4Extensions.FromHex(@"ff8f9c");

        /// <summary>Amber — alerts, unread chat.</summary>
        public static readonly Color4 AccentAmber = Color4Extensions.FromHex(@"ffd36e");

        /// <summary>Cool blue — secondary, sync mode, baseline state.</summary>
        public static readonly Color4 AccentSky = Color4Extensions.FromHex(@"73b7ff");

        /// <summary>Deep navy — the panel's structural base colour. Translucent in glass material.</summary>
        public static readonly Color4 SurfaceBase = Color4Extensions.FromHex(@"0c0e2a");

        /// <summary>Slightly warmer mid-tone for the panel's vertical gradient stop.</summary>
        public static readonly Color4 SurfaceWarm = Color4Extensions.FromHex(@"15112c");

        // Ink colours (text). Three opacities only — no more ad-hoc 0.42 / 0.46 / 0.62.
        /// <summary>Primary ink — headlines, body text the eye reads first.</summary>
        public const float InkPrimary = 1.00f;
        /// <summary>Secondary ink — supporting text, metadata.</summary>
        public const float InkSecondary = 0.62f;
        /// <summary>Tertiary ink — captions, fine print, disabled states.</summary>
        public const float InkTertiary = 0.42f;

        // ─── Spacing (8pt grid) ────────────────────────────────────────

        public const float SpacingXs = 4;   // hairline gaps
        public const float SpacingSm = 8;   // intra-component spacing
        public const float SpacingMd = 16;  // adjacent component gap
        public const float SpacingLg = 24;  // section breathing room
        public const float SpacingXl = 32;  // panel padding

        // ─── Corner radii ──────────────────────────────────────────────
        // Designed to be concentric: a card (CornerMd) sits inside the
        // panel (CornerLg) with SpacingLg of padding around it. Keeps the
        // visual hierarchy obvious without thinking.

        public const float CornerSm = 10;   // pills, chips, small buttons
        public const float CornerMd = 16;   // cards
        public const float CornerLg = 24;   // panel itself

        /// <summary>
        /// CornerExponent &gt; 2 makes corners "squircle-y" (closer to
        /// iOS / macOS curves than a perfect circle arc). 2.4 is what
        /// Apple SF symbols and SwiftUI use under the hood — gives a
        /// gentle continuous curvature rather than a sharp arc.
        /// </summary>
        public const float SquircleExponent = 2.4f;

        // ─── Typography ────────────────────────────────────────────────
        // Tighter scale than before — was 12 / 14.5 / 19 / 22 / 34 (five
        // sizes, none of them on a clean ratio). Now four sizes on a 1.25×
        // ratio with one display.

        public const float TypeCaption = 11;    // KICKER labels, fine print
        public const float TypeBody    = 14;    // body text
        public const float TypeHeadline = 18;   // card headlines
        public const float TypeTitle   = 24;    // section / overlay subtitle
        public const float TypeDisplay = 30;    // overlay title only

        public const float CaptionTracking = 0.18f; // letter-spacing on captions (Apple SF Caps feel)

        // ─── Animation ─────────────────────────────────────────────────
        // Spring-y feel for entrance, snappy for interactive feedback.

        public const double EntranceDuration = 480;
        public const double EntranceStagger  = 70;     // ms between consecutive cards
        public const double HoverDuration    = 180;
        public const double DismissDuration  = 220;
    }
}
