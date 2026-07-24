// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Platform;
using osu.Game.Cosmetics.Definitions;

namespace Torii.CosmeticCreator.Editor
{
    /// <summary>
    /// torii: guarda / carga una <see cref="CosmeticDefinition"/> a un archivo ".toriicosmetic" (JSON).
    /// es exactamente el archivo que Torii carga para la store. Export va a la carpeta portable de
    /// exports; Import lee de exports + imports (donde el usuario dropea lo que le mandan para contests)
    /// + SampleAuras. Data pura, sin code-exec, safe para archivos de la comunidad.
    /// </summary>
    public static class CosmeticExporter
    {
        public const string EXTENSION = ".toriicosmetic";
        public const string IMPORTS_DIR = "imports";

        public static string Export(Storage storage, CosmeticDefinition definition)
        {
            var exports = storage.GetStorageForDirectory("exports");
            string fileName = safeFileName(definition.Id ?? definition.Name ?? "cosmetic") + EXTENSION;

            using (var stream = exports.CreateFileSafely(fileName))
            using (var writer = new StreamWriter(stream))
                writer.Write(definition.Serialize());

            return exports.GetFullPath(fileName);
        }

        /// <summary>lee y parsea un .toriicosmetic desde una ruta absoluta.</summary>
        public static CosmeticDefinition Import(string fullPath) => CosmeticDefinition.Parse(File.ReadAllText(fullPath));

        /// <summary>
        /// lista los .toriicosmetic disponibles para reabrir: exports (lo que creaste) + imports (lo que
        /// te mandan) + SampleAuras (los de ejemplo, junto al exe). devuelve (nombre visible -> ruta).
        /// </summary>
        public static IReadOnlyList<(string label, string path)> ListAvailable(Storage storage)
        {
            var result = new List<(string, string)>();

            foreach (var (dirLabel, dir) in candidateDirs(storage))
            {
                if (!Directory.Exists(dir))
                    continue;

                foreach (string file in Directory.GetFiles(dir, "*" + EXTENSION).OrderBy(f => f))
                    result.Add(($"{dirLabel}: {Path.GetFileNameWithoutExtension(file)}", file));
            }

            return result;
        }

        private static IEnumerable<(string label, string dir)> candidateDirs(Storage storage)
        {
            yield return ("imports", storage.GetStorageForDirectory(IMPORTS_DIR).GetFullPath(string.Empty));
            yield return ("exports", storage.GetStorageForDirectory("exports").GetFullPath(string.Empty));
            yield return ("samples", Path.Combine(AppContext.BaseDirectory, "SampleAuras"));
        }

        /// <summary>ruta de la carpeta imports (para abrirla y que el usuario dropee archivos).</summary>
        public static Storage ImportsStorage(Storage storage) => storage.GetStorageForDirectory(IMPORTS_DIR);

        private static string safeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');
            return string.IsNullOrWhiteSpace(name) ? "cosmetic" : name;
        }
    }
}
