// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Users.Drawables
{
    /// <summary>
    /// Tiny "Playing on osu! Torii client" badge shown next to online users
    /// who connected through a verified Torii build (server tells us via
    /// <see cref="UserPresence.ClientName"/> == "torii").
    /// </summary>
    /// <remarks>
    /// Replaced the previous full-colour logo sprite with a vector torii
    /// gate drawn from primitives + a "torii" label so the indicator
    /// reads "Playing on osu! Torii client" at a glance instead of
    /// being a generic icon that needed the tooltip to be meaningful.
    /// Done in primitives (no asset) so the badge:
    ///   - scales crisply to any DPI without needing @2x/@4x variants,
    ///   - matches the surrounding user-panel typography colour-wise
    ///     when the panel theme changes,
    ///   - works in test scenes that don't have the texture store wired.
    /// </remarks>
    public partial class ToriiClientBadge : CompositeDrawable, IHasTooltip
    {
        public LocalisableString TooltipText => "Playing on osu! Torii client";

        // Vermillion — the traditional torii-gate colour. High enough
        // contrast against both light and dark user-panel backgrounds
        // that the badge reads clearly without needing a per-theme tint.
        private static readonly Color4 torii_red = new Color4(204, 41, 41, 255);

        public ToriiClientBadge()
        {
            // Auto-sized so the badge takes only the room it actually
            // needs (icon + label + padding). Keeps adjacent badges
            // packed tight in the user panel.
            AutoSizeAxes = Axes.Both;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            const float corner_radius = 5f;

            InternalChild = new Container
            {
                AutoSizeAxes = Axes.Both,
                Masking = true,
                CornerRadius = corner_radius,
                Children = new Drawable[]
                {
                    // Subtle filled background — same vermillion family as
                    // the gate but at low opacity so the badge feels like
                    // a quiet "powered by" pill rather than competing for
                    // attention with the supporter / group badges next to it.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = torii_red,
                        Alpha = 0.18f,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(3, 0),
                        Padding = new MarginPadding { Horizontal = 5, Vertical = 2 },
                        Children = new Drawable[]
                        {
                            new ToriiGateGlyph
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(11, 11),
                                Colour = torii_red,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = "torii",
                                Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                                Colour = torii_red,
                                Margin = new MarginPadding { Bottom = 1 }, // optical centre with the glyph
                            },
                        },
                    },
                },
            };
        }

        /// <summary>
        /// Toggle visibility based on whether the user is connected through
        /// a verified Torii client. Called by <see cref="ExtendedUserPanel"/>
        /// whenever the server's <see cref="UserPresence.ClientName"/>
        /// changes for this user.
        /// </summary>
        public void UpdateClientName(string? clientName)
        {
            bool isTorii = clientName == "torii";
            this.FadeTo(isTorii ? 1f : 0f, 200, Easing.OutQuint);
        }

        /// <summary>
        /// Vector torii-gate glyph. Drawn from four <see cref="Box"/>
        /// primitives (top kasagi beam, smaller nuki beam, two hashira
        /// pillars) so the icon scales crisply at any DPI without needing
        /// a texture asset.
        /// </summary>
        /// <remarks>
        /// Proportions are loosely modelled after a stylised myojin torii
        /// — the upper beam overhangs the pillars on both sides; the lower
        /// beam sits inset between them. Stroke widths are picked so the
        /// gate reads clearly at the 11px size used in the badge but stays
        /// recognisable when scaled up by the parent (e.g. profile cards
        /// at 2x size).
        /// </remarks>
        private partial class ToriiGateGlyph : Container
        {
            public ToriiGateGlyph()
            {
                // All children laid out relative to a canonical 1.0 × 1.0
                // box so the glyph scales with Size.
                Children = new Drawable[]
                {
                    // Kasagi (top beam) — overhangs the pillars by 10% on
                    // each side, sits at the very top of the glyph.
                    new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        RelativePositionAxes = Axes.Y,
                        Anchor = Anchor.TopCentre,
                        Origin = Anchor.TopCentre,
                        Width = 1.0f,
                        Height = 2.5f,
                        // Height is in pixels, RelativeSizeAxes = X means
                        // width relative to parent, height absolute.
                    },
                    // Nuki (secondary beam) — narrower than the pillars'
                    // outer edges, sits about 30% from the top.
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
}
