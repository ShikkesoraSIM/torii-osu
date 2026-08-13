// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;

namespace osu.Desktop.Windows
{
    public static class Icons
    {
        /// <summary>
        /// Fully qualified path to the directory that contains icons (in the installation folder).
        /// </summary>
        private static readonly string icon_directory = Path.GetDirectoryName(typeof(Icons).Assembly.Location)!;

        // el archivo se llamaba lazer.ico y era el icono de osu!lazer. Ahora apunta al de
        // Torii; el nombre de la property queda para no romper los call sites.
        public static string Lazer => Path.Join(icon_directory, "torii.ico");

        public static string Beatmap => Path.Join(icon_directory, "torii.ico");
    }
}
