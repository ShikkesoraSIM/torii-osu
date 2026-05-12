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
using osu.Game.Graphics.UserInterface;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Users.Drawables
{
    /// <summary>
    /// Tiny "Playing on Torii Nova client" badge shown next to online users
    /// who connected through a verified Torii build (server tells us via
    /// <see cref="UserPresence.ClientName"/> == "torii").
    /// </summary>
    /// <remarks>
    /// Replaced the previous full-colour logo sprite with a vector torii
    /// gate drawn from primitives + a "torii" label so the indicator
    /// reads "Playing on Torii Nova client" at a glance instead of
    /// being a generic icon that needed the tooltip to be meaningful.
    /// Done in primitives (no asset) so the badge:
    ///   - scales crisply to any DPI without needing @2x/@4x variants,
    ///   - matches the surrounding user-panel typography colour-wise
    ///     when the panel theme changes,
    ///   - works in test scenes that don't have the texture store wired.
    /// </remarks>
    public partial class ToriiClientBadge : CompositeDrawable, IHasTooltip
    {
        public LocalisableString TooltipText => "Playing on Torii Nova client";

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

        // ToriiGateGlyph was previously a private inner class here; promoted
        // to a public class at osu.Game/Graphics/UserInterface/ToriiGateGlyph.cs
        // so other Torii-branded UI (cursor-size preview overlay's "Torii
        // Exclusive" badge, future call-outs) can reuse the same gate
        // geometry without duplicating proportions / stroke widths.
    }
}
