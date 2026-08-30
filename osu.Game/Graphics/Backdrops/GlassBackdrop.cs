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
        /// <remarks>
        /// Sigma y <see cref="frame_buffer_scale"/> se bajaron A LA MITAD LOS DOS, y eso
        /// deja el desenfoque EN PANTALLA exactamente igual: lo que se ve es sigma
        /// dividido la escala del buffer, y 7/0.4 y 3.5/0.2 son los mismos 17,5 px.
        ///
        /// Lo que cambia es el trabajo, que va como el CUBO de la escala: el buffer mide
        /// area por escala al cuadrado, y las muestras del kernel son proporcionales a
        /// sigma. La mitad de escala son cuatro veces menos texeles, y la mitad de sigma
        /// son la mitad de muestras: ocho veces menos trabajo por el mismo resultado.
        ///
        /// En song select hay tres o cuatro de estos rearmandose en CADA cuadro (van con
        /// cachedFrameBuffer: false), asi que esto se paga multiplicado.
        ///
        /// Si en pantallas chicas el frost llega a verse sucio, el punto medio es 0.25 de
        /// escala con sigma 4.4, que mantiene el mismo radio y sigue siendo mucho mas
        /// barato que lo que habia.
        /// </remarks>
        public const float DEFAULT_BLUR_SIGMA = 3.5f;

        /// <summary>
        /// Resolution scale of the per-panel blur buffer. Lower = cheaper (fewer pixels blurred) and,
        /// since the result is blurred anyway, the low-res source is invisible. The crisp tint/edges
        /// come from the full-res <see cref="tint"/> box on top.
        /// </summary>
        private const float frame_buffer_scale = 0.2f;

        /// <summary>
        /// Resolucion del buffer de blur, como fraccion del tamanio del panel.
        /// </summary>
        /// <remarks>
        /// OJO con bajarla en paneles FINITOS. Un gaussiano necesita texeles para desenfocar:
        /// si el buffer termina midiendo menos texeles que el diametro del kernel, el blur
        /// deja de desenfocar y pasa a PROMEDIAR todo a un color plano.
        ///
        /// Paso exactamente eso con la toolbar. Mide 40 de alto: al 0.2 el buffer quedaba en
        /// 8 texeles, y sigma 3.5 cubre mas que la franja entera. El resultado era una lechada
        /// gris lisa de lado a lado, que no se parecia en nada a un frost.
        ///
        /// Regla practica: el panel tiene que quedar con varias veces sigma de alto en espacio
        /// de buffer. Los paneles grandes (ajustes, chat, wedge) son de cientos de pixeles y
        /// les sobra; los finitos necesitan mas escala y menos sigma, que ademas es mas barato
        /// porque el area del buffer sigue siendo chica.
        /// </remarks>
        public float FrameBufferScale { get; init; } = frame_buffer_scale;

        /// <summary>
        /// Escala del buffer por eje, cuando no alcanza con la misma en los dos. Si es null se
        /// usa <see cref="FrameBufferScale"/> pareja.
        /// </summary>
        /// <remarks>
        /// Esto es lo que hace barato un blur de radio grande. El radio que se VE es sigma
        /// dividido la escala, asi que bajar la escala y bajar sigma en la misma proporcion
        /// deja el resultado identico y cuesta muchisimo menos: menos texeles Y menos taps.
        ///
        /// En la toolbar, sigma 26 sobre escala 0.6 son 43px de radio y ~57 taps por texel.
        /// Los mismos 43px con escala 0.2 y sigma 8.6 son ~19 taps sobre un buffer cuatro
        /// veces mas angosto. Es el mismo razonamiento del docstring de arriba, pero por eje:
        /// en horizontal sobra resolucion para regalar, en vertical no hay ninguna.
        ///
        /// El limite es el aliasing: si la escala baja tanto que un texel cubre mas que el
        /// radio del blur, aparece shimmer cuando la escena se mueve.
        /// </remarks>
        public Vector2? FrameBufferScaleVector { get; init; }

        private readonly Box tint;

        /// <summary>
        /// Gaussian sigma of the backdrop blur. Only relevant when the glass backdrop is active.
        /// </summary>
        public float BlurSigma { get; init; } = DEFAULT_BLUR_SIGMA;

        /// <summary>
        /// Sigma por eje, cuando no alcanza con el mismo valor en los dos. Si es null se usa
        /// <see cref="BlurSigma"/> parejo.
        /// </summary>
        /// <remarks>
        /// Sirve para paneles bajos y anchos como la toolbar. Ahi el blur tiene que aguantar
        /// dos cosas distintas: en vertical hay pocos texeles y pasarse aplana todo a un color
        /// liso, pero en horizontal sobra lugar y hace falta MUCHO radio para que los bordes
        /// duros de la UI de atras (el wedge de song select, la barra de busqueda) dejen de
        /// leerse como bloques adentro del frost. Un sigma parejo no puede con las dos.
        /// </remarks>
        public Vector2? BlurSigmaVector { get; init; }

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
                BlurSigma = BlurSigmaVector ?? new Vector2(BlurSigma),
                FrameBufferScale = FrameBufferScaleVector ?? new Vector2(FrameBufferScale),
                RedrawOnScale = false,
                Depth = 1,
                Child = sceneView,
            });
        }
    }
}
