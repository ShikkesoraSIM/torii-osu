// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Graphics.Backdrops
{
    /// <summary>
    /// Torii DARK GLASS: reemplazo drop-in del <c>Box</c> de fondo de un panel. Cuando el tema glass
    /// esta activo (y hay un <see cref="IBackdropProvider"/> disponible), dibuja la escena que el panel
    /// tiene DETRAS, blureada y clippeada a la forma del panel (por el masking del ancestro) = Aero real.
    /// Encima pone un tinte translucido (<see cref="TintColour"/>). Si no hay glass/backdrop (tema normal,
    /// Potato Mode), se comporta como un simple Box del color del tinte, sin costo extra.
    /// </summary>
    public partial class GlassBackdrop : CompositeDrawable
    {
        /// <summary>
        /// Default gaussian sigma of the per-panel blur, in the low-resolution buffer space
        /// (see <see cref="frame_buffer_scale"/>), so effective on-screen blur is ~2.5x this.
        /// Kept modest: a lighter frost reads cleaner and the smaller kernel is cheaper.
        /// </summary>
        public const float DEFAULT_BLUR_SIGMA = 7f;

        /// <summary>
        /// Resolution scale of the per-panel blur buffer. Lower = cheaper (fewer pixels blurred) and,
        /// since the result is blurred anyway, the low-res source is invisible. The crisp tint/edges
        /// come from the full-res <see cref="tint"/> box on top.
        /// </summary>
        private const float frame_buffer_scale = 0.4f;

        private readonly Box tint;

        /// <summary>
        /// Gaussian sigma of the backdrop blur. Only relevant when the glass backdrop is active.
        /// </summary>
        public float BlurSigma { get; init; } = DEFAULT_BLUR_SIGMA;

        [Resolved(CanBeNull = true)]
        private IBackdropProvider backdrop { get; set; }

        public GlassBackdrop()
        {
            RelativeSizeAxes = Axes.Both;
            // Tint sits in front (Depth -1); the blurred scene view is added behind it in load().
            AddInternal(tint = new Box
            {
                RelativeSizeAxes = Axes.Both,
                Depth = -1,
            });
        }

        /// <summary>
        /// The translucent film drawn over the blurred backdrop (or the flat fill when no backdrop is active).
        /// </summary>
        public Color4 TintColour
        {
            get => tint.Colour;
            set => tint.Colour = value;
        }

        /// <summary>
        /// Like <see cref="TintColour"/> but accepts a gradient <see cref="ColourInfo"/> (e.g. a horizontal
        /// alpha falloff), for surfaces that fade their translucency across the panel (the song-select wedge).
        /// </summary>
        public ColourInfo TintColourInfo
        {
            set => tint.Colour = value;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            if (!OsuColour.IsGlassTheme || backdrop?.Available != true)
                return;

            var sceneView = backdrop.CreateSceneView();
            sceneView.RelativeSizeAxes = Axes.Both;

            // Per-panel blur: render the synchronised scene view into this panel's own buffer and blur it.
            // Only the panel-region portion of the (full-screen) scene lands in the buffer, so we only pay
            // for the visible area of whichever glass panel is open.
            AddInternal(new BufferedContainer(cachedFrameBuffer: false)
            {
                RelativeSizeAxes = Axes.Both,
                BlurSigma = new Vector2(BlurSigma),
                FrameBufferScale = new Vector2(frame_buffer_scale),
                RedrawOnScale = false,
                Depth = 1,
                Child = sceneView,
            });
        }
    }
}
