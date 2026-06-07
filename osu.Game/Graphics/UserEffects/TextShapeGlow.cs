// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserEffects
{
    /// <summary>
    /// A pulsing soft glow that hugs the actual letter shapes of a username.
    ///
    /// Internally a <see cref="BufferedContainer"/> renders a duplicate of
    /// the username text into an offscreen buffer, then applies a gaussian
    /// blur. Because the buffered child is the actual glyph shapes, the
    /// blurred result reads as a soft halo following the letter outlines —
    /// equivalent to Photoshop's "Outer Glow" effect at small radius.
    ///
    /// The buffer is auto-sized to the mirror text plus
    /// <see cref="GlowPadding"/> on every side so the gaussian falloff has
    /// room to fade out before clipping at the buffer edge — the missing
    /// padding was why the v1 glow looked stretched / discoloured at the
    /// letter boundaries. The wrapped <see cref="UserAuraContainer"/> is
    /// responsible for offsetting this drawable by <c>-GlowPadding</c> so
    /// the inner mirror text lands exactly on top of the wrapped target's
    /// pixel position; combined with <c>BypassAutoSizeAxes = Both</c> on
    /// the container's side, the glow becomes purely visual padding that
    /// extends outside the wrapper's bounds without growing it.
    /// </summary>
    public partial class TextShapeGlow : BufferedContainer
    {
        /// <summary>
        /// Pixels of padding around the mirror text inside the buffer, on
        /// every side. Drives how far the blur halo can extend past the
        /// letter outlines before clipping. The wrapping
        /// <see cref="UserAuraContainer"/> reads this constant to compute
        /// the negative position offset that re-aligns the glow with the
        /// target text.
        /// </summary>
        public const float GlowPadding = 8f;

        /// <summary>Alpha at the peak of the breath cycle.</summary>
        public float MaxAlpha { get; init; } = 0.95f;

        /// <summary>Alpha at the trough of the breath cycle.</summary>
        public float MinAlpha { get; init; } = 0.45f;

        /// <summary>Duration of one fade direction (full cycle is 2x this).</summary>
        public double DurationMs { get; init; } = 1500;

        /// <summary>
        /// The mirror <see cref="OsuSpriteText"/> rendered into the buffer.
        /// Exposed so the wrapping <see cref="UserAuraContainer"/> can read
        /// its <see cref="Drawable.DrawSize"/> to bind the particle emitter
        /// to the actual text-shape bounds (which the wrapper itself can't
        /// always provide — see RelativeSizeAxes propagation note in
        /// <see cref="UserAuraContainer.Wrap"/>).
        /// </summary>
        public OsuSpriteText Mirror { get; }

        /// <summary>
        /// Optional maximum width (in pixels) for the rendered glow buffer.
        /// When set to a positive value, the buffer's width is clamped to
        /// <c>MaxWidth + 2 * <see cref="GlowPadding"/></c> and the inner
        /// <see cref="Mirror"/> is masked at that boundary — glyphs past
        /// the cap are clipped, eliminating the "ghost glow" that
        /// otherwise extends past a truncated username (e.g. when the
        /// target is a <c>TruncatingSpriteText</c> in a narrow song-select
        /// or gameplay-leaderboard row).
        ///
        /// Default 0 = unbounded (original auto-size behaviour). Setting a
        /// positive value flips the buffer to fixed-width + auto-height
        /// (so the glow still hugs the line height naturally). Resetting
        /// to 0 reverts to fully auto-sized.
        /// </summary>
        public float MaxWidth
        {
            get => maxWidth;
            set
            {
                if (Math.Abs(maxWidth - value) < 0.5f) return;
                maxWidth = value;
                applyMaxWidth();
            }
        }

        private float maxWidth;

        public TextShapeGlow(LocalisableString text, FontUsage font, Color4 colour)
            : base(cachedFrameBuffer: false)
        {
            // Auto-size to the mirror text plus the padding margin we add
            // below. The padding gives the gaussian blur kernel room to
            // fade out smoothly INSIDE the buffer before hitting the edge —
            // without it the blur clips visibly at the buffer boundary and
            // the glow reads as "letters with hard halo edges" rather than
            // a soft outer glow. UserAuraContainer pairs this with
            // BypassAutoSizeAxes so the glow's growth doesn't push the
            // wrapper's auto-size away from the wrapped target.
            AutoSizeAxes = Axes.Both;
            Padding = new MarginPadding(GlowPadding);

            // Sigma 3.5/3 ≈ 10-11px effective blur radius — enough to read
            // as a halo around the letter outlines, small enough that it
            // still hugs glyph shapes rather than smearing into a blob.
            // Stays inside the GlowPadding budget (8px) plus a small slack
            // so the falloff is mostly resident in the buffer.
            BlurSigma = new Vector2(3.5f, 3f);

            BackgroundColour = new Color4(0, 0, 0, 0);
            Alpha = 0;

            // Mirror SpriteText nested inside an explicit Masking
            // Container. Setting Masking on the BufferedContainer
            // itself doesn't clip what gets rasterised into the
            // offscreen buffer — only what gets displayed from it —
            // so a long Mirror would still bake a "ghost" of the
            // full text into the buffer even after we shrank the
            // buffer via MaxWidth. The inner masking container
            // matches the buffer's content area (minus GlowPadding
            // each side) and Masking=true clips the Mirror's draw
            // call at that boundary, so the ghost truly disappears
            // when the visible target text is truncated.
            //
            // OsuSpriteText (not raw SpriteText) per the project's banned-
            // API analyzer.
            Child = mirrorClipContainer = new Container
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                AutoSizeAxes = Axes.Both,
                Child = Mirror = new OsuSpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Text = text,
                    Font = font,
                    Colour = colour,
                    // OsuSpriteText defaults to Shadow=true (a drop
                    // shadow offset slightly down + right under each
                    // glyph). Inside the Mirror, that shadow gets
                    // rasterised into the offscreen buffer ALONG WITH
                    // the glyph, and the BufferedContainer's gaussian
                    // blur smears the two together — the resulting
                    // halo is no longer centred on the glyph outline,
                    // it's centred on (glyph + shadow). Visually the
                    // halo appears shifted down by ~half the shadow
                    // offset, which reads as "the glow doesn't line
                    // up with the username letters" in surfaces where
                    // the eye can compare them side by side (user
                    // panels, chat). Suppressing the Mirror's own
                    // shadow keeps the halo strictly outlining the
                    // glyph; the target text on top still draws its
                    // own drop shadow normally.
                    Shadow = false,
                },
            };
        }

        // Holder we toggle Masking + Width on when MaxWidth changes;
        // the Mirror inside gets clipped via this container's Masking
        // rather than the BufferedContainer's (which only clips the
        // displayed buffer, not what gets rasterised into it).
        private readonly Container mirrorClipContainer;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Snap to the floor first so the breath cycle's fade-IN is the
            // user's first impression rather than a flash of full alpha.
            Alpha = MinAlpha;

            this.Loop(t => t
                .FadeTo(MaxAlpha, DurationMs, Easing.InOutSine)
                .Then()
                .FadeTo(MinAlpha, DurationMs, Easing.InOutSine));
        }

        // Toggle between auto-sized (unbounded) and fixed-width-with-clip
        // (bounded) modes. When bounded, the inner mirrorClipContainer's
        // Masking clips the Mirror's draw call before it hits the
        // offscreen buffer, which is what hides the "ghost glow" past
        // a truncated username.
        private void applyMaxWidth()
        {
            if (maxWidth <= 0)
            {
                // Unbounded: revert to auto-sized + unmasked everywhere.
                // BufferedContainer auto-sizes to the inner clip
                // container (which auto-sizes to the Mirror's natural
                // text extent).
                //
                // Axes hygiene: AutoSizeAxes and RelativeSizeAxes are
                // not allowed to overlap on the same axis. We're coming
                // from the bounded branch which set X to Relative on
                // the inner container; if we set AutoSizeAxes = Both
                // first, X is in BOTH for one statement and the setter
                // throws. Clear RelativeSizeAxes first, then promote
                // AutoSizeAxes. Same dance for the BufferedContainer
                // itself even though it doesn't currently flip Relative
                // — Width is harmless when AutoSize takes over.
                mirrorClipContainer.RelativeSizeAxes = Axes.None;
                mirrorClipContainer.AutoSizeAxes = Axes.Both;
                mirrorClipContainer.Masking = false;
                AutoSizeAxes = Axes.Both;
            }
            else
            {
                // Bounded: fix the BufferedContainer's X to the cap +
                // 2 * GlowPadding (so the gaussian blur still has
                // padding budget on the left/right cap). Inside, the
                // mirrorClipContainer takes the BufferedContainer's
                // full width (minus padding) and masks the Mirror at
                // that boundary — so glyphs past the cap are dropped
                // before rasterising into the buffer, not just hidden
                // after.
                //
                // Same axes-overlap risk in reverse: coming from the
                // unbounded branch the inner container has AutoSizeAxes
                // = Both, so we must shrink AutoSizeAxes off X BEFORE
                // adding RelativeSizeAxes = X.
                AutoSizeAxes = Axes.Y;
                Width = maxWidth + 2 * GlowPadding;
                mirrorClipContainer.AutoSizeAxes = Axes.Y;
                mirrorClipContainer.RelativeSizeAxes = Axes.X;
                mirrorClipContainer.Width = 1f;
                mirrorClipContainer.Masking = true;
            }
        }
    }
}
