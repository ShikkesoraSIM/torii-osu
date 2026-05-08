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
    /// The unified surface primitive used by the Torii Briefing — both the
    /// outer panel and the individual cards inside it. Defaults are tuned
    /// for cards (clean elevated dark surface with a black drop shadow);
    /// the panel opts into the "signature" treatment (pink-tinted shadow +
    /// top-edge specular ribbon) via the public properties below.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The earlier iteration of this primitive layered four accent
    /// treatments on top of every consumer (horizontal accent wash,
    /// always-on specular ribbon, accent-tinted shadow, surface-lift
    /// gradient). Stacked vertically, the per-card accent shadows
    /// blended into a multi-coloured halo that fought the cards above
    /// rather than supporting them. This rewrite keeps only what reads
    /// well in isolation:
    /// </para>
    /// <list type="number">
    ///     <item>
    ///         <description>
    ///         <b>Base.</b> Vertical gradient (warmer mid-tone top →
    ///         deep navy bottom). Brightness is controlled by
    ///         <see cref="SurfaceLift"/>: 1.0 matches the panel base
    ///         (use for the panel itself), &gt;1 lifts the surface so
    ///         cards visibly float above the panel underneath.
    ///         </description>
    ///     </item>
    ///     <item>
    ///         <description>
    ///         <b>Optional specular ribbon.</b> A faint white band at
    ///         the top edge that fades down. Default off; the panel
    ///         opts in to sell the "ambient light from above" feeling
    ///         on its larger surface.
    ///         </description>
    ///     </item>
    ///     <item>
    ///         <description>
    ///         <b>Hairline stroke.</b> 1 px white at low opacity for
    ///         edge definition.
    ///         </description>
    ///     </item>
    ///     <item>
    ///         <description>
    ///         <b>Drop shadow.</b> Configurable colour and opacity. The
    ///         default is <see cref="Color4.Black"/> at 30% — neutral
    ///         elevation that doesn't fight whatever's underneath. Only
    ///         the panel uses a coloured shadow (its pink "signature"
    ///         glow). Cards stay neutral so a stack of them doesn't
    ///         turn into a rainbow.
    ///         </description>
    ///     </item>
    /// </list>
    /// <para>
    /// Corners use <see cref="BriefingTheme.SquircleExponent"/> (2.4)
    /// for the iOS / SwiftUI continuous-curvature look rather than the
    /// perfect circular arc of the default exponent 2.
    /// </para>
    /// <para>
    /// Children added via <c>Child</c> / <c>Children</c> route to a
    /// content-slot <see cref="Container"/> sitting on top of the
    /// material layers. The slot defaults to X-relative / Y-auto (card
    /// mode); the panel switches to Both-relative via
    /// <see cref="RelativeContentSize"/>.
    /// </para>
    /// </remarks>
    internal partial class BriefingGlass : Container
    {
        protected override Container<Drawable> Content => content;
        private readonly Container content;

        private float cornerSize = BriefingTheme.CornerMd;
        private float shadowOpacity = 0.22f;
        private float shadowRadius = 18f;
        private Vector2 shadowOffset = new Vector2(0, 4);
        private Color4 shadowColor = Color4.Black;
        private float shadowRoundness = 6f;
        private float specularStrength = 0f;
        private float specularHeight = 38f;
        private float surfaceLift = 1.0f;

        /// <summary>Corner radius. <see cref="BriefingTheme.CornerMd"/> for cards, <see cref="BriefingTheme.CornerLg"/> for the panel.</summary>
        public float CornerSize
        {
            get => cornerSize;
            set
            {
                cornerSize = value;
                CornerRadius = value;
            }
        }

        /// <summary>Drop-shadow colour. Defaults to black (neutral elevation).</summary>
        public Color4 ShadowColor
        {
            get => shadowColor;
            set
            {
                shadowColor = value;
                applyShadow();
            }
        }

        /// <summary>Drop-shadow alpha. Default 0.30. Set to 0 to disable.</summary>
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
        /// Extra rounding added to the shadow shape on top of <see cref="CornerSize"/>.
        /// Higher values make the shadow blob outward more circularly — softer falloff,
        /// less of the "rectangular halo" feeling on stacked cards. Default 6.
        /// </summary>
        public float ShadowRoundness
        {
            get => shadowRoundness;
            set
            {
                shadowRoundness = value;
                applyShadow();
            }
        }

        /// <summary>
        /// Configures the inner content slot's sizing. Defaults to <c>Axes.X</c>
        /// (X-relative + Y-auto, for cards). Set to <c>Axes.Both</c> for fixed-size
        /// users like the panel.
        /// </summary>
        public Axes RelativeContentSize
        {
            set
            {
                content.AutoSizeAxes = Axes.None;
                content.RelativeSizeAxes = value;

                var auto = Axes.Both & ~value;
                if (auto != Axes.None)
                    content.AutoSizeAxes = auto;
            }
        }

        /// <summary>Top-edge specular highlight strength. Default 0 (off). The panel uses ~0.18.</summary>
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

        /// <summary>Specular ribbon height. Default 38 (cards if enabled); the panel uses ~70.</summary>
        public float SpecularHeight
        {
            get => specularHeight;
            set
            {
                specularHeight = value;
                if (specularContainer != null)
                    specularContainer.Height = value;
            }
        }

        /// <summary>
        /// Surface brightness relative to the panel base. 1.0 matches the panel
        /// (use for the panel itself); 1.3–1.5 lifts cards above the panel.
        /// </summary>
        public float SurfaceLift
        {
            get => surfaceLift;
            set
            {
                surfaceLift = value;
                if (baseBox != null)
                    applyBaseTone();
            }
        }

        private Box baseBox;
        private readonly Box specularRibbon;
        private Container specularContainer;

        public BriefingGlass()
        {
            Masking = true;
            CornerRadius = cornerSize;
            CornerExponent = BriefingTheme.SquircleExponent;
            MaskingSmoothness = 1.4f;
            BorderThickness = 1f;
            // Slightly stronger hairline than the previous 0.07 — at low
            // opacity the edge gets lost on dark surfaces, especially
            // where the card meets the panel and the card needs visible
            // boundaries to read as a separate object.
            BorderColour = Color4.White.Opacity(0.12f);

            applyShadow();

            baseBox = new Box
            {
                RelativeSizeAxes = Axes.Both,
                BypassAutoSizeAxes = Axes.Both,
            };
            applyBaseTone();

            specularContainer = new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = specularHeight,
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

            content = new Container
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
            };

            AddRangeInternal(new Drawable[]
            {
                baseBox,
                specularContainer,
                content,
            });
        }

        private void applyShadow()
        {
            EdgeEffect = new EdgeEffectParameters
            {
                Type = EdgeEffectType.Shadow,
                Colour = shadowColor.Opacity(shadowOpacity),
                Radius = shadowRadius,
                Offset = shadowOffset,
                // Roundness adds to the shadow's corner radius, making the falloff
                // softer and more circular at the corners. Without this, the
                // Gaussian blur edges parallel the card's straight sides and
                // can read as "rectangular haloed" — exactly the brittleness
                // you want to avoid on stacked cards.
                Roundness = shadowRoundness,
            };
        }

        /// <summary>
        /// Applies the surface gradient. The top stop scales with
        /// <see cref="SurfaceLift"/> so cards (lift &gt; 1) read as floating above
        /// the panel (lift = 1) rather than blending into it.
        /// </summary>
        private void applyBaseTone()
        {
            float liftFactor = System.Math.Clamp(surfaceLift, 0.6f, 1.6f);
            // Slight brightening at the top stop only; bottom always anchors to the
            // deep panel base so cards feel grounded.
            float topOpacity = 0.45f * liftFactor;

            baseBox.Colour = ColourInfo.GradientVertical(
                BriefingTheme.SurfaceWarm.Opacity(topOpacity),
                BriefingTheme.SurfaceBase.Opacity(0.92f));
        }
    }
}
