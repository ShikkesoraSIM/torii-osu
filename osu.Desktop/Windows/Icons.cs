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

        /// <summary>
        /// App-brand icon (gradient torii gate). Used for Windows file
        /// associations (osu:// / osump://) and any other "show the
        /// program's icon" path the desktop project hits.
        /// </summary>
        public static string Torii => Path.Join(icon_directory, "torii.ico");

        /// <summary>
        /// Legacy lazer.ico — kept ONLY because any pre-rebrand registry
        /// associations / shortcuts written by older Torii builds may
        /// still point at this filename. The file on disk still contains
        /// the LEGACY osu! lazer icon (not the new torii brand) — code
        /// inside Torii Nova should use <see cref="Torii"/> instead.
        /// This property exists strictly for the "give me the path of
        /// the legacy file" case, which is currently no callers (kept
        /// as a getter for symmetry with <see cref="Beatmap"/> and for
        /// any future migration code that needs to find the old file).
        /// </summary>
        public static string Lazer => Path.Join(icon_directory, "lazer.ico");

        /// <summary>
        /// File-association icon for .osz/.olz/.osr/.osk. Separate from
        /// the app brand icon — this one is the stylised beatmap icon
        /// the framework ships, not the torii gate logo.
        /// </summary>
        public static string Beatmap => Path.Join(icon_directory, "beatmap.ico");
    }
}
