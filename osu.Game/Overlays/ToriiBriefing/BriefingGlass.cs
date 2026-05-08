// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ToriiBriefing
{
    /// <summary>
    /// The Liquid Glass material used everywhere in the Torii Briefing
    /// overlay. A reusable wrapper that turns whatever you put inside it
    /// into a translucent, lifted glass surface — the panel itself, the
    /// individual cards, the floating header pill all use this primitive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// osu-framework does not have a true backdrop-blur primitive (the
    /// <c>BufferedContainer</c> only blurs its own children, not what's
    /// behind it), so the "glass" effect here is faked the same way Apple
    /// fakes it on opaque dark surfaces: a stack of subtle layers that
    /// together read as a single material.
    /// </para>
    /// <list type="number">
    ///     <item>
    ///         <description>
    ///         <b>Base.</b> A vertical gradient from a slightly warmer mid-tone
    ///         at the top to the deep panel base at the bottom. This is the
    ///         "ambient occlusion" cue — the bottom of any glass surface is
    ///         always slightly darker than the top in real life.
    ///         </description>
    ///     </item>
    ///     <item>
    ///         <description>
    ///         <b>Accent wash.</b> A very faint horizontal gradient from the
    ///         accent colour fading to transparent. Hints at the card's
    ///         category without dominating the surface.
    ///         </description>
    ///     </item>
    ///     <item>
    ///         <description>
    ///         <b>Specular ribbon.</b> A 32px-tall band of white-at-low-opacity
    ///         at the very top edge that fades downward. Simulates light
    ///         coming from above hitting the rounded top edge — this is
    ///         the single most important detail for selling the "glass"
    ///         feel against a flat-coloured background.
    ///         </description>
    ///     </item>
    ///     <item>
    ///         <description>
    ///         <b>Hairline stroke.</b> A 1px white border at low opacity
    ///         catches stray light along the edge.
    ///         </description>
    ///     </item>
    ///     <item>
    ///         <description>
    ///         <b>Soft drop shadow.</b> An accent-tinted shadow lifts the
    ///         surface off whatever is behind it. Tinting the shadow
    ///         (instead of using neutral black, which disappears against
    ///         the dark overlay) is what makes it feel like a glowing
    ///         card rather than a flat sticker.
    ///         </description>
    ///     </item>
    /// </list>
    /// <para>
    /// Corners use <see cref="BriefingTheme.SquircleExponent"/> (2.4) for
    /// the iOS / SwiftUI continuous-curvature look rather than the perfect
    /// circular arc you get with the default exponent of 2.
    /// </para>
    /// <para>
    /// Children added to <see cref="BriefingGlass"/> via the standard
    /// <c>Child</c> / <c>Children</c> properties go INSIDE the glass — the
    /// <see cref="Content"/> override routes them to a content-slot
    /// <see cref="Container"/> that sits on top of the material layers.
    /// All layer boxes set <see cref="Drawable.BypassAutoSizeAxes"/> so the
    /// glass works equally well at fixed sizes and with auto-sizing axes.
    /// </para>
    /// </remarks>
    internal partial class BriefingGlass : Container
    {
        protected override Container<Drawable> Content => content;
        private readonly Container content;

        private readonly Box accentWash;

        private float cornerSize = BriefingTheme.CornerMd;
        private float shadowOpacity = 0.18f;
        private float shadowRadius = 22f;
        private Vector2 shadowOffset = new Vector2(0, 8);
        private Color4 accent = BriefingTheme.AccentCyan;
        private float accentMix = 0.05f;
        private float specularStrength = 0.10f;

        /// <summary>Corner radius. Defaults to <see cref="BriefingTheme.CornerMd"/> (cards). Use <see cref="BriefingTheme.CornerLg"/> for the panel.</summary>
        public float CornerSize
        {
            get => cornerSize;
            set
            {
                cornerSize = value;
                CornerRadius = value;
            }
        }

        /// <summary>Tint colour bled into the surface (very faint) and the drop shadow. Defaults to brand cyan.</summary>
        public Color4 Accent
        {
            get => accent;
            set
            {
                accent = value;
                if (accentWash != null)
                    accentWash.Colour = ColourInfo.GradientHorizontal(accent.Opacity(accentMix), Color4.Transparent);
                applyShadow();
            }
        }

        /// <summary>How strongly the accent tints the surface. Default 0.05 (very subtle).</summary>
        public float AccentMix
        {
            get => accentMix;
            set
            {
                accentMix = value;
                if (accentWash != null)
                    accentWash.Colour = ColourInfo.GradientHorizontal(accent.Opacity(accentMix), Color4.Transparent);
            }
        }

        /// <summary>Drop-shadow opacity. 0.18 for cards, 0.30 for the panel.</summary>
        public float ShadowOpacity
        {
            get => shadowOpacity;
            set
            {
                shadowOpacity = value;
                applyShadow();
            }
        }

        /// <summary>Drop-shadow blur radius.</summary>
        public float ShadowRadius
        {
            get => shadowRadius;
            set
            {
                shadowRadius = value;
                applyShadow();
            }
        }

        /// <summary>Drop-shadow offset.</summary>
        public Vector2 ShadowOffset
        {
            get => shadowOffset;
            set
            {
                shadowOffset = value;
                applyShadow();
            }
        }

        /// <summary>
        /// Configures the inner content slot's sizing. Defaults to <c>Axes.X</c> (i.e. X-relative,
        /// Y-auto) which is what cards want. Set this to <c>Axes.Both</c> for fixed-size users
        /// like the panel itself.
        /// </summary>
        /// <remarks>
        /// Internally this swaps which axes use <see cref="Drawable.RelativeSizeAxes"/> vs
        /// <see cref="CompositeDrawable.AutoSizeAxes"/> on the content slot — the two cannot
        /// overlap, so we clear auto-size first to avoid the framework's overlap exception.
        /// </remarks>
        public Axes RelativeContentSize
        {
            set
            {
                content.AutoSizeAxes = Axes.None;
                content.RelativeSizeAxes = value;

                // Anything not relative-sized falls back to auto-size, mirroring the parent's
                // most likely intent (Both = pure fixed-relative; X = relative-X + auto-Y).
                var auto = Axes.Both & ~value;
                if (auto != Axes.None)
                    content.AutoSizeAxes = auto;
            }
        }

        /// <summary>Top-edge specular highlight strength (0 disables it). Default 0.10.</summary>
        public float SpecularStrength
        {
            get => specularStrength;
            set
            {
                specularStrength = value;
                if (specularRibbon != null)
                {
                    specularRibbon.Colour = ColourInfo.GradientVertical(
                        Color4.White.Opacity(specularStrength),
                        Color4.White.Opacity(0));
                }
            }
        }

        private readonly Box specularRibbon;

        public BriefingGlass()
        {
            Masking = true;
            CornerRadius = cornerSize;
            CornerExponent = BriefingTheme.SquircleExponent;
            MaskingSmoothness = 1.4f;
            BorderThickness = 1f;
            BorderColour = Color4.White.Opacity(0.10f);

            applyShadow();

            // 1. Base — vertical gradient (slightly warmer top → deep navy bottom)
            //    The bypass on auto-size axes lets BriefingGlass be used both
            //    in fixed-size mode (panel) and auto-size mode (cards).
            var baseBox = new Box
            {
                RelativeSizeAxes = Axes.Both,
                BypassAutoSizeAxes = Axes.Both,
                Colour = ColourInfo.GradientVertical(
                    BriefingTheme.SurfaceWarm.Opacity(0.78f),
                    BriefingTheme.SurfaceBase.Opacity(0.94f)),
            };

            // 2. Accent wash — fades from accent (very low opacity) → transparent
            accentWash = new Box
            {
                RelativeSizeAxes = Axes.Both,
                BypassAutoSizeAxes = Axes.Both,
                Colour = ColourInfo.GradientHorizontal(accent.Opacity(accentMix), Color4.Transparent),
            };

            // 3. Specular highlight ribbon at the top edge.
            //    Sized as a fixed-height container at the top so it bypasses
            //    auto-size on Y but always covers the top 32px regardless of
            //    panel height.
            var specularContainer = new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 32,
                BypassAutoSizeAxes = Axes.Both,
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                Child = specularRibbon = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(
                        Color4.White.Opacity(specularStrength),
                        Color4.White.Opacity(0)),
                },
            };

            // 4. Content slot — children added via Add/Children land here on top of the material.
            //    Defaults to "card mode" (X-relative + Y-auto) since the cards are by far
            //    the most common consumer; panel/fixed-size users override via
            //    <see cref="RelativeContentSize"/> to switch to Both-relative.
            content = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
            };

            AddRangeInternal(new Drawable[]
            {
                baseBox,
                accentWash,
                specularContainer,
                content,
            });
        }

        private void applyShadow()
        {
            // EdgeEffectParameters is a struct; assigning the whole property in one shot
            // avoids "cannot modify members of readonly field" issues that would arise
            // from caching it in a field.
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Colour = accent.Opacity(shadowOpacity),
                Radius = shadowRadius,
                Offset = shadowOffset,
            };
        }
    }
}
