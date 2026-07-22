// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;

namespace osu.Game.Graphics.Backdrops
{
    /// <summary>
    /// Torii DARK GLASS: provee vistas sincronizadas de la escena (fondo + pantallas) ya capturada
    /// a un framebuffer, para que los paneles de vidrio puedan blurear lo que tienen DETRAS
    /// (Aero real), no solo el wallpaper. Cacheado por <see cref="OsuGame"/> solo cuando el tema
    /// glass esta activo y Potato Mode apagado; si no, se resuelve null y los paneles caen a fill plano.
    /// </summary>
    public interface IBackdropProvider
    {
        /// <summary>
        /// Whether a captured scene buffer is available to sample from.
        /// </summary>
        bool Available { get; }

        /// <summary>
        /// Creates a view of the captured scene, aligned to its original on-screen position
        /// (<see cref="BufferedContainerView{T}.SynchronisedDrawQuad"/> is set), so that placing it
        /// behind a masked panel shows exactly the scene content sitting under that panel.
        /// The caller is responsible for blurring/tinting the returned view.
        /// </summary>
        BufferedContainerView<Drawable> CreateSceneView();
    }
}
