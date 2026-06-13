// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;

namespace osu.Game.Graphics.UserInterface
{
    /// <summary>
    /// Vector torii-gate glyph drawn from primitives — no texture asset.
    /// Originally lived as a private inner class on
    /// <see cref="osu.Game.Users.Drawables.ToriiClientBadge"/>; promoted to a
    /// public class so other Torii-branded UI elements (cursor-size preview
    /// overlay's "Torii Exclusive" badge, future Torii callouts) can share a
    /// single gate-glyph implementation instead of re-deriving the
    /// proportions in each new caller.
    /// </summary>
    /// <remarks>
    /// Proportions are loosely modelled after a stylised myojin torii — the
    /// upper kasagi beam overhangs the pillars on both sides; the lower nuki
    /// beam sits inset between them. Stroke widths are picked so the gate
    /// reads clearly at the small (11px) size used in user-panel badges and
    /// stays recognisable when scaled up by the parent (e.g. profile cards
    /// at 2× size or the cursor-size preview overlay's pill header).
    ///
    /// Drawn from four <see cref="Box"/> primitives so the icon scales
    /// crisply at any DPI without needing @2x / @4x texture variants, and
    /// inherits the parent's <c>Colour</c> for theming (vermillion in
    /// canonical use, but theming as a faded grey for "locked" or
    /// "disabled" treatments would just work).
    /// </remarks>
    public partial class ToriiGateGlyph : Container
    {
        public ToriiGateGlyph()
        {
            // All children laid out relative to a canonical 1.0 × 1.0 box so
            // the glyph scales with Size — caller picks the size, glyph fits.
            Children = new Drawable[]
            {
                // Kasagi (top beam) — overhangs the pillars by 10% on each
                // side, sits at the very top of the glyph.
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    RelativePositionAxes = Axes.Y,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Width = 1.0f,
                    Height = 2.5f,
                    // Height is in pixels; RelativeSizeAxes = X means width
                    // relative to parent, height absolute.
                },
                // Nuki (secondary beam) — narrower than the pillars' outer
                // edges, sits about 30% from the top.
                new Box
                {
                    RelativeSizeAxes = Axes.X,
                    RelativePositionAxes = Axes.Y,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Y = 0.30f,
                    Width = 0.78f,
                    Height = 1.5f,
                },
                // Left hashira (pillar) — anchored to the bottom-left,
                // running from below the top beam down to the bottom.
                new Box
                {
                    RelativePositionAxes = Axes.Both,
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    X = 0.16f,
                    Y = 0.0f,
                    Width = 2f,
                    RelativeSizeAxes = Axes.Y,
                    Height = 1.0f,
                },
                // Right hashira (pillar) — mirror of the left.
                new Box
                {
                    RelativePositionAxes = Axes.Both,
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    X = -0.16f,
                    Y = 0.0f,
                    Width = 2f,
                    RelativeSizeAxes = Axes.Y,
                    Height = 1.0f,
                },
            };
        }
    }
}
