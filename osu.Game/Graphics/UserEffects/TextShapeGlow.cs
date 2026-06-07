// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.UserEffects
{
    /// <summary>
    /// A soft (optionally pulsing) glow that hugs the actual letter shapes of a
    /// username.
    ///
    /// Built on <see cref="GlowingDrawable"/> — the SAME additive effect-blend
    /// pipeline the profile/toolbar username glow uses — so it actually blooms
    /// brighter than the text instead of washing out or merging into it. A
    /// duplicate of the username is rendered into an offscreen buffer, blurred,
    /// tinted by <see cref="GlowingDrawable.GlowColour"/> and composited
    /// ADDITIVELY. <c>DrawOriginal</c> is false, so only the halo draws; the real
    /// username text renders separately on top.
    ///
    /// An earlier revision drew a colour-baked mirror with the BufferedContainer's
    /// raw <c>Blending</c>, which barely registered as a glow on most surfaces
    /// (the toolbar looked fine only because it used GlowingDrawable directly).
    /// Routing everything through the same pipeline fixes that.
    /// </summary>
    public partial class TextShapeGlow : GlowingDrawable
    {
        /// <summary>
        /// Kept at 0. The glow now auto-sizes to the mirror with no inner padding
        /// (GlowingDrawable inflates its own draw quad to fit the blur), so the
        /// wrapping <see cref="UserAuraContainer"/>'s historical <c>-GlowPadding</c>
        /// offset is a no-op and the glow sits exactly on the target text.
        /// </summary>
        public const float GlowPadding = 0f;

        /// <summary>Alpha at the peak of the breath cycle.</summary>
        public float MaxAlpha { get; init; } = 0.95f;

        /// <summary>Alpha at the trough of the breath cycle.</summary>
        public float MinAlpha { get; init; } = 0.45f;

        /// <summary>Duration of one fade direction (full cycle is 2x this).</summary>
        public double DurationMs { get; init; } = 1500;

        /// <summary>When false the glow holds a steady <see cref="MaxAlpha"/>
        /// instead of breathing — the hook a future "reduced motion" toggle flips
        /// to calm the effect down.</summary>
        public bool Pulsate { get; init; } = true;

        /// <summary>
        /// The mirror <see cref="OsuSpriteText"/>. Exposed so the wrapping
        /// <see cref="UserAuraContainer"/> can read its
        /// <see cref="Drawable.DrawSize"/> to bind the particle emitter to the
        /// actual text-shape bounds.
        /// </summary>
        public OsuSpriteText Mirror { get; private set; } = null!;

        /// <summary>Retained for API compatibility with the old buffered
        /// implementation. Sizing is now automatic, so this is a no-op.</summary>
        public float MaxWidth { get; set; }

        public TextShapeGlow(LocalisableString text, FontUsage font, Color4 colour)
        {
            // base ctor already built Mirror via CreateDrawable; fill it in.
            Mirror.Text = text;
            Mirror.Font = font;

            // ~10-13px effective halo. GlowingDrawable inflates the draw quad to
            // fit, so this never clips at the drawable's own bounds.
            BlurSigma = new Vector2(4f);

            // The glow is the blurred glyph shapes tinted by GlowColour, added on
            // top of the scene — bright, so it reads as a real glow.
            EffectBlending = BlendingParameters.Additive;
            DrawOriginal = false;
            GlowColour = colour;
            Alpha = 0;
        }

        // White shape: the glow's colour comes from EffectColour (GlowColour),
        // applied to the blurred glyph outlines. Shadow off so the halo stays
        // centred on the glyphs rather than on (glyph + drop shadow).
        protected override Drawable CreateDrawable() => Mirror = new OsuSpriteText
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Shadow = false,
            Colour = Color4.White,
        };

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (!Pulsate)
            {
                // Reduced-motion / static mode: hold a steady glow, no breathing.
                Alpha = MaxAlpha;
                return;
            }

            // Snap to the floor first so the breath cycle's fade-IN is the user's
            // first impression rather than a flash of full alpha.
            Alpha = MinAlpha;

            this.Loop(t => t
                .FadeTo(MaxAlpha, DurationMs, Easing.InOutSine)
                .Then()
                .FadeTo(MinAlpha, DurationMs, Easing.InOutSine));
        }
    }
}
