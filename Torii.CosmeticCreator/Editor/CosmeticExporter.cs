// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Framework.Platform;
using osu.Game.Cosmetics.Definitions;

namespace Torii.CosmeticCreator.Editor
{
    /// <summary>
    /// torii: guarda una <see cref="CosmeticDefinition"/> a un archivo ".toriicosmetic" (JSON) en la
    /// carpeta de exports de la app. es exactamente el archivo que Torii carga para la store; por ahora
    /// va a un directorio conocido (mas adelante: file picker + subir al server). devuelve el path final.
    /// </summary>
    public static class CosmeticExporter
    {
        public const string EXTENSION = ".toriicosmetic";

        public static string Export(Storage storage, CosmeticDefinition definition)
        {
            var exports = storage.GetStorageForDirectory("exports");
            string fileName = safeFileName(definition.Id ?? definition.Name ?? "cosmetic") + EXTENSION;

            using (var stream = exports.CreateFileSafely(fileName))
            using (var writer = new StreamWriter(stream))
                writer.Write(definition.Serialize());

            return exports.GetFullPath(fileName);
        }

        private static string safeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');
            return string.IsNullOrWhiteSpace(name) ? "cosmetic" : name;
        }
    }
}
