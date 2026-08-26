// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;

namespace osu.Game.Graphics.Backdrops
{
    /// <summary>
    /// Torii DARK GLASS: <see cref="IBackdropProvider"/> respaldado por el <see cref="BufferedContainer"/>
    /// que envuelve la escena (fondo + pantallas). El capture-buffer dibuja la escena NITIDA en pantalla
    /// (DrawOriginal=true, sin blur) y ademas guarda su textura, de la que sacamos vistas sincronizadas.
    /// El blur lo aplica cada panel sobre su propia vista (ver GlassBackdrop), asi solo se blurea la
    /// region visible del panel abierto en vez de toda la pantalla.
    /// </summary>
    public class SceneBufferBackdropProvider : IBackdropProvider
    {
        private readonly BufferedContainer sceneBuffer;

        public SceneBufferBackdropProvider(BufferedContainer sceneBuffer)
        {
            this.sceneBuffer = sceneBuffer;
        }

        public bool Available => true;

        public BufferedContainerView<Drawable> CreateSceneView()
        {
            var view = sceneBuffer.CreateView();
            view.SynchronisedDrawQuad = true;
            return view;
        }
    }
}
