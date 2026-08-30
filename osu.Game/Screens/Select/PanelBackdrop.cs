// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics.Primitives;
using osu.Framework.Utils;
using osu.Game.Screens.Backgrounds;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// torii DARK GLASS: fuente compartida para el vidrio (frost) de los paneles del carousel.
    /// UNA sola imagen fullscreen del beatmap actual, blureada, dibujada INVISIBLE (AlwaysPresent + Alpha 0:
    /// renderiza su framebuffer pero no se ve en pantalla, asi el wallpaper real queda intacto). Cada panel
    /// saca un <see cref="BufferedContainerView{T}"/> con SynchronisedDrawQuad que muestra la porcion del blur
    /// alineada a su posicion en pantalla. Un solo blur compartido, continuo al scrollear, sin feedback.
    /// El buffer va CACHEADO y lo redibujamos a mano (ver <see cref="Update"/>). Su contenido solo cambia
    /// cuando cambia el mapa, pero sin cache se rehacia entero 60 veces por segundo: era el item mas caro de
    /// song select, un blur fullscreen completo por cuadro para una imagen que estaba quieta.
    ///
    /// El intento anterior de cachearlo fallo y quedo documentado aca ("el buffer arrancaba vacio en algunos
    /// mapas"). El motivo: un BufferedContainer cacheado NO se entera de nada que pase adentro suyo. El sprite
    /// entra por LoadComponentAsync, eso invalida con source Child, y el LayoutValue del buffer solo escucha
    /// Self y Parent. Nunca se redibuja y el buffer queda con lo que hubiera (nada) para siempre.
    ///
    /// Por eso el cache va acompaniado de ForceRedraw explicito mientras el contenido cambia.
    /// </summary>
    public partial class PanelBackdrop : CompositeDrawable, IPanelBackdrop
    {
        private readonly BufferedContainer buffered;
        private readonly Container spriteContainer;

        /// <summary>
        /// Hasta cuando hay que seguir redibujando el buffer. Cubre el crossfade de 300ms entre
        /// mapas: mientras el sprite hace su fade el contenido cambia en cada cuadro.
        /// </summary>
        private double redrawUntil = double.PositiveInfinity;

        private Vector2 lastDrawSize;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        [Resolved(CanBeNull = true)]
        private BackgroundScreenStack? backgroundStack { get; set; }

        /// <summary>
        /// Ultima geometria a la que alineamos, para saber cuando hay que rehacer el buffer.
        /// </summary>
        private Quad lastAlignedQuad;

        private Drawable? currentContent;

        public PanelBackdrop(float blurSigma)
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = buffered = new BufferedContainer(cachedFrameBuffer: true)
            {
                RelativeSizeAxes = Axes.Both,
                BlurSigma = new Vector2(blurSigma),
                FrameBufferScale = new Vector2(0.5f),
                RedrawOnScale = false,
                // renderiza el buffer pero no pinta nada en pantalla (wallpaper real intacto)
                AlwaysPresent = true,
                Alpha = 0f,
                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        // fondo base oscuro: si el mapa no tiene imagen, el frost queda oscuro en vez del
                        // gradiente default brillante de lazer.
                        new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(20, 20, 25, 255) },
                        spriteContainer = new Container { RelativeSizeAxes = Axes.Both },
                        // dim general: mantiene el frost siempre subdued, nunca revienta de brillo.
                        new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black, Alpha = 0.35f },
                    },
                },
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            working.BindValueChanged(updateBackground, true);
        }

        private void updateBackground(ValueChangedEvent<WorkingBeatmap> e)
        {
            var beatmap = e.NewValue;

            var previous = currentContent;
            currentContent = null;

            // si el mapa no tiene imagen de fondo, no cargamos nada: queda el Box oscuro base (evita el
            // gradiente default brillante de lazer que reventaba las dificultades).
            // Cualquier camino que cambie lo que hay adentro tiene que abrir la ventana de
            // redibujado, este o no el sprite: el fade del anterior tambien es contenido que cambia.
            redrawUntil = Time.Current + 320;

            if (beatmap == null || string.IsNullOrEmpty(beatmap.Metadata?.BackgroundFile))
            {
                previous?.FadeOut(300, Easing.OutQuint).Expire();
                return;
            }

            var newSprite = new BeatmapBackgroundSprite(beatmap)
            {
                RelativeSizeAxes = Axes.Both,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                FillMode = FillMode.Fill,
                Alpha = 0f,
            };
            currentContent = newSprite;

            // GetBackground() (disco + upload de textura) corre en el hilo async, no traba el update thread.
            LoadComponentAsync(newSprite, loaded =>
            {
                spriteContainer.Add(loaded);
                loaded.FadeIn(300, Easing.OutQuint);
                previous?.FadeOut(300, Easing.OutQuint).Expire();

                // La carga es async: cuando termina, la ventana que abrimos al pedir el mapa ya
                // pudo haberse cerrado. La reabrimos desde ahora, que es cuando arranca el fade.
                redrawUntil = Time.Current + 320;
            });
        }

        protected override void Update()
        {
            base.Update();

            // Con el buffer cacheado nada de adentro lo invalida solo: ni el sprite entrando por
            // LoadComponentAsync, ni su fade. Lo pedimos a mano mientras el contenido se mueve.
            //
            // Ojo con el orden: Update() del padre corre antes del UpdateSubTree de los hijos, asi
            // que el ForceRedraw de este cuadro habilita el update de los hijos a tiempo. Sin eso
            // el fade quedaria congelado, porque con el buffer cacheado los hijos ni se actualizan.
            bool resized = DrawSize != lastDrawSize;
            lastDrawSize = DrawSize;

            // Si la geometria del wallpaper cambio (parallax, transicion de pantalla, resize),
            // el frost cacheado quedo sampleado en la posicion vieja: hay que rehacerlo.
            if (alignToRealBackground())
                resized = true;

            // El resize deberia invalidar solo, pero con RedrawOnScale = false el framework
            // des-escala el tamanio antes de compararlo, asi que no me confio y lo pido igual.
            if (resized || Time.Current < redrawUntil)
                buffered.ForceRedraw();
        }

        /// <summary>
        /// Le copia al sprite de esta copia la geometria EXACTA del wallpaper de verdad.
        /// </summary>
        /// <remarks>
        /// Sin esto los dos hacen su propio FillMode.Fill contra cajas distintas y nunca
        /// coinciden. El wallpaper vive adentro del ParallaxContainer, que le suma una escala
        /// de 1.02 y lo mueve con el mouse, arriba de la escala de BackgroundScreen.Update, y
        /// encima su caja va estirada hacia arriba por BackgroundTopExtension. Esta copia no
        /// tiene nada de eso: su caja es la pantalla pelada.
        ///
        /// El resultado era que cada mapa se desalineaba distinto segun su aspect ratio. Los
        /// mas anchos que la caja cortan por alto y los mas altos por ancho, asi que segun de
        /// que lado del limite caiga la imagen, la diferencia cambia hasta de naturaleza: por
        /// eso algunos parecian encajar y la mayoria no.
        ///
        /// En vez de replicar esa cadena de transformaciones a mano (y tener que actualizarla
        /// cada vez que upstream toque una), le copiamos el quad ya calculado. Cualquier cosa
        /// que le pase al wallpaper la seguimos gratis.
        /// </remarks>
        /// <returns>true si la geometria cambio respecto del cuadro anterior.</returns>
        private bool alignToRealBackground()
        {
            var real = (backgroundStack?.CurrentScreen as BackgroundScreenBeatmap)?.BackgroundSprite;

            // Sin fondo real (transiciones de pantalla, test scenes, o todavia cargando) el
            // sprite se queda con su Fill de siempre: desalineado, pero cubriendo. Nunca lo
            // dejamos a medio configurar.
            if (real == null || !real.IsLoaded || currentContent is not Drawable sprite || !sprite.IsLoaded)
                return false;

            var quad = real.ScreenSpaceDrawQuad;

            bool cambio = !Precision.AlmostEquals(quad.TopLeft, lastAlignedQuad.TopLeft)
                          || !Precision.AlmostEquals(quad.BottomRight, lastAlignedQuad.BottomRight);

            if (!cambio)
                return false;

            lastAlignedQuad = quad;

            var topLeft = spriteContainer.ToLocalSpace(quad.TopLeft);
            var bottomRight = spriteContainer.ToLocalSpace(quad.BottomRight);

            sprite.RelativeSizeAxes = Axes.None;
            sprite.FillMode = FillMode.Stretch;
            sprite.Anchor = Anchor.TopLeft;
            sprite.Origin = Anchor.TopLeft;
            sprite.Position = topLeft;
            sprite.Size = bottomRight - topLeft;

            return true;
        }

        public BufferedContainerView<Drawable> CreateView()
        {
            var view = buffered.CreateView();
            view.SynchronisedDrawQuad = true;
            view.DisplayOriginalEffects = true; // dibujar el buffer YA blureado
            return view;
        }
    }

    /// <summary>
    /// Provee vistas del <see cref="PanelBackdrop"/> compartido a los paneles del carousel.
    /// Cacheado por <see cref="SongSelect"/> solo con el tema glass activo (si no, se resuelve null).
    /// </summary>
    public interface IPanelBackdrop
    {
        BufferedContainerView<Drawable> CreateView();
    }
}
