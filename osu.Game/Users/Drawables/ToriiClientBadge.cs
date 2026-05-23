// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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
    /// Small "Playing on Torii" / "Playing on Torii Nova" badge shown next to
    /// online users connected through a verified Torii build. Colour and copy
    /// vary by release stream; a platform icon (Windows / Linux / macOS /
    /// Android) sits inline at the right edge of the pill when the server
    /// knows the user's OS.
    /// </summary>
    /// <remarks>
    /// The badge is fed by <see cref="UserPresence.ClientName"/>, populated by
    /// the spectator server from the Torii server's
    /// <c>/api/private/client-versions/torii-hashes</c> endpoint. The value
    /// flowing in is one of:
    /// <list type="bullet">
    /// <item><description>Empty / null -> hide (user isn't on a verified Torii build).</description></item>
    /// <item><description><c>"torii"</c> / <c>"osu! Torii"</c> -> legacy
    /// brand-only formats; we treat them as the Torii master stream with no
    /// known platform.</description></item>
    /// <item><description><c>"Torii"</c> / <c>"Torii Nova"</c> -> current
    /// brand-only formats from the post-rebrand CI registration; differentiated
    /// by the "nova" substring.</description></item>
    /// <item><description><c>"&lt;brand&gt;|&lt;os&gt;"</c> -> rich format with
    /// platform info appended after a pipe. <c>os</c> is the OS label the CI
    /// registered (<c>Windows</c> / <c>Linux</c> / <c>macOS</c> / <c>Android</c>).</description></item>
    /// </list>
    /// We parse the value here and reshape the pill accordingly — Nova
    /// instances render in saturated amber instead of the Torii vermillion so
    /// you can tell at a glance who's on the experimental preview stream.
    /// </remarks>
    public partial class ToriiClientBadge : CompositeDrawable, IHasTooltip
    {
        // Vermillion (traditional torii-gate red) — used for the stable
        // Torii master stream. High enough contrast against both light and
        // dark user-panel backgrounds that the badge reads cleanly without
        // needing a per-theme tint.
        private static readonly Color4 torii_red = new Color4(204, 41, 41, 255);

        // Bright amber — used for the Torii Nova preview stream. Pulled
        // deliberately toward the warm-yellow side (not orange) so it never
        // gets confused for the supporter pink/red badges that sit next to
        // it in the user panel.
        private static readonly Color4 nova_amber = new Color4(248, 184, 38, 255);

        private LocalisableString tooltip = "Playing on Torii client";
        public LocalisableString TooltipText => tooltip;

        // Mutable visual elements — rebuilt / recoloured by UpdateClientName
        // whenever the spectator hands us a new value for this user.
        //
        // The leading icon used to be our hand-rolled vector ToriiGateGlyph
        // (parallel thin pillars + crossbeam from Box primitives) which
        // rasterised poorly at the 11px size this pill renders in user
        // panels — the pillars came out asymmetric depending on subpixel
        // positioning. Replaced with FontAwesome's torii-gate glyph
        // (fa-torii-gate, U+F6A1): same iconography but a single SDF
        // shape so it rasterises cleanly at any size with perfect
        // symmetry. ToriiGateGlyph stays in use for big-size callouts
        // (cursor-size preview overlay, future hero-sized Torii branding)
        // where the hand-rolled version reads fine.
        private Box? background;
        private SpriteIcon? leadIcon;
        private OsuSpriteText? labelText;
        private SpriteIcon? platformIcon;
        private Container? platformIconWrapper;

        public ToriiClientBadge()
        {
            // Auto-sized so the badge takes only the room it actually needs
            // (icon + label + optional platform icon + padding). Keeps adjacent
            // badges packed tight in the user panel.
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
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = torii_red,
                        Alpha = 0.18f,
                    },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(4, 0),
                        Padding = new MarginPadding { Horizontal = 6, Vertical = 2 },
                        Children = new Drawable[]
                        {
                            leadIcon = new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(11, 11),
                                Colour = torii_red,
                                Icon = FontAwesome.Solid.ToriiGate,
                            },
                            labelText = new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = "torii",
                                Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                                Colour = torii_red,
                            },
                            // Platform icon — hidden until UpdateClientName
                            // parses an OS label out of the rich value. The
                            // wrapper exists so we can collapse the icon
                            // entirely (Alpha=0) when there's no platform
                            // info, instead of leaving an empty gap where
                            // the icon used to be.
                            platformIconWrapper = new Container
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                AutoSizeAxes = Axes.Both,
                                Alpha = 0,
                                Child = platformIcon = new SpriteIcon
                                {
                                    Size = new Vector2(10, 10),
                                    Colour = torii_red,
                                    // Icon swapped in by applyPlatform.
                                    Icon = FontAwesome.Solid.QuestionCircle,
                                },
                            },
                        },
                    },
                },
            };
        }

        /// <summary>
        /// Toggle visibility + reshape the pill based on the spectator-supplied
        /// client name string. See class docs for the expected value formats.
        /// </summary>
        public void UpdateClientName(string? clientName)
        {
            if (string.IsNullOrEmpty(clientName) || !clientName.Contains("torii", StringComparison.OrdinalIgnoreCase))
            {
                this.FadeTo(0f, 200, Easing.OutQuint);
                return;
            }

            // Split off the optional "|<os>" suffix. Brand-only formats
            // (legacy + new) fall through with os == null.
            string brand;
            string? os;
            int pipe = clientName.IndexOf('|');
            if (pipe >= 0)
            {
                brand = clientName.Substring(0, pipe).Trim();
                os = clientName.Substring(pipe + 1).Trim();
                if (string.IsNullOrEmpty(os))
                    os = null;
            }
            else
            {
                brand = clientName.Trim();
                os = null;
            }

            // "Nova" substring is the discriminator for the preview stream.
            // Picked the substring (rather than literal equality) so future
            // brand evolutions like "Torii Nova Public" still resolve.
            bool isNova = brand.Contains("nova", StringComparison.OrdinalIgnoreCase);

            applyStream(isNova);
            applyPlatform(os);

            tooltip = buildTooltip(isNova, os);

            this.FadeTo(1f, 200, Easing.OutQuint);
        }

        private void applyStream(bool isNova)
        {
            Color4 accent = isNova ? nova_amber : torii_red;

            if (background != null) background.Colour = accent;
            if (leadIcon != null) leadIcon.Colour = accent;
            if (labelText != null)
            {
                labelText.Text = isNova ? "nova" : "torii";
                labelText.Colour = accent;
            }
            if (platformIcon != null) platformIcon.Colour = accent;
        }

        private void applyPlatform(string? os)
        {
            if (platformIconWrapper == null || platformIcon == null)
                return;

            IconUsage? icon = resolvePlatformIcon(os);
            if (icon == null)
            {
                platformIconWrapper.Alpha = 0;
                return;
            }

            platformIcon.Icon = icon.Value;
            platformIconWrapper.Alpha = 1;
        }

        private static IconUsage? resolvePlatformIcon(string? os)
        {
            if (string.IsNullOrEmpty(os))
                return null;

            // Matching is case-insensitive + substring-based so the registry
            // can send any of "Windows" / "win-x64" / "Linux" / "linux-x64"
            // etc. without us having to negotiate exact spellings end-to-end.
            string lower = os.ToLowerInvariant();

            if (lower.Contains("android")) return FontAwesome.Brands.Android;
            if (lower.Contains("ios") || lower.Contains("iphone")) return FontAwesome.Brands.Apple;
            if (lower.Contains("mac") || lower.Contains("osx") || lower.Contains("darwin")) return FontAwesome.Brands.Apple;
            if (lower.Contains("linux")) return FontAwesome.Brands.Linux;
            if (lower.Contains("win")) return FontAwesome.Brands.Windows;

            return null;
        }

        private static LocalisableString buildTooltip(bool isNova, string? os)
        {
            string streamName = isNova ? "Torii Nova" : "Torii";
            return os == null
                ? $"Playing on {streamName} client"
                : $"Playing on {streamName} client ({os})";
        }

        // ToriiGateGlyph was previously a private inner class here; promoted
        // to a public class at osu.Game/Graphics/UserInterface/ToriiGateGlyph.cs
        // so other Torii-branded UI (cursor-size preview overlay's "Torii
        // Exclusive" badge, future call-outs) can reuse the same gate
        // geometry without duplicating proportions / stroke widths.
    }
}
