// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Screens
{
    /// <summary>
    /// torii: una pantalla que quiere quedarse con los archivos que le tiran encima en
    /// vez de que el juego intente importarlos. Lo usa el generador, donde arrastrar un
    /// mp3 significa "usá esta canción", no "importá esto como mapa".
    /// </summary>
    public interface IHandleDroppedFile
    {
        /// <summary>
        /// Returns true if this screen took the file. When it does, the game skips its
        /// usual import for that path.
        /// </summary>
        bool HandleDroppedFile(string path);
    }
}
