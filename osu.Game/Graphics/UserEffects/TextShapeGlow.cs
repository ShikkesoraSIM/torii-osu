// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

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

            // Mirror SpriteText pinned to TopLeft so it lands at the same
            // pixel origin as the wrapped target text (which Wrap also
            // resets to TopLeft inside the wrapper). The wrapping
            // UserAuraContainer offsets this whole BufferedContainer by
            // -GlowPadding so that, after the inward Padding pushes the
            // SpriteText right/down by GlowPadding, the SpriteText ends up
            // at the wrapper's (0, 0) — exactly overlapping the target.
            //
            // OsuSpriteText (not raw SpriteText) per the project's banned-
            // API analyzer.
            Child = Mirror = new OsuSpriteText
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Text = text,
                Font = font,
                Colour = colour,
            };
        }

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
    }
}
