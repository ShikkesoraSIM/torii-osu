// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

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
    /// cachedFrameBuffer=false: redibuja cada frame (un blur fullscreen compartido) para que SIEMPRE refleje el
    /// mapa actual sin depender de invalidaciones (con cache el buffer arrancaba vacio en algunos mapas).
    /// </summary>
    public partial class PanelBackdrop : CompositeDrawable, IPanelBackdrop
    {
        private readonly BufferedContainer buffered;
        private readonly Container spriteContainer;

        [Resolved]
        private IBindable<WorkingBeatmap> working { get; set; } = null!;

        private Drawable? currentContent;

        public PanelBackdrop(float blurSigma)
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = buffered = new BufferedContainer(cachedFrameBuffer: false)
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
            });
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
