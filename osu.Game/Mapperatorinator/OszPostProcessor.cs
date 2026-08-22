// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace osu.Game.Mapperatorinator
{
    /// <summary>
    /// Applies the user's metadata (title, artist, creator, tags, background) to a
    /// generated .osz BEFORE it gets imported.
    ///
    /// Doing it at the archive level instead of poking realm after import means the
    /// game ingests a perfectly normal beatmap: hashes, metadata rows and background
    /// all come in through the standard import path, and there is nothing to clean up
    /// if the user cancels half way.
    /// </summary>
    public static class OszPostProcessor
    {
        public class MetadataOverrides
        {
            public string? Title { get; set; }
            public string? Artist { get; set; }
            public string? Creator { get; set; }
            public string? Tags { get; set; }

            /// <summary>Absolute path of an image to bundle as the background, or null to keep whatever the tool produced.</summary>
            public string? BackgroundImagePath { get; set; }

            /// <summary>Difficulty name to stamp on the .osu, or null to keep the generated one.</summary>
            public string? Version { get; set; }

            /// <summary>
            /// Filename (inside the archive/set) to point AudioFilename at. Used when the
            /// generated diff is merged into an existing set and must reference ITS audio.
            /// </summary>
            public string? AudioFilename { get; set; }

            /// <summary>
            /// Filename of an EXISTING background to reference in the events section,
            /// without bundling anything. Ignored when <see cref="BackgroundImagePath"/> is set.
            /// </summary>
            public string? BackgroundFilename { get; set; }

            /// <summary>Content for the settings sidecar entry, or null to not write one.</summary>
            public string? SidecarJson { get; set; }

            public bool HasAnything =>
                !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist)
                                                 || !string.IsNullOrWhiteSpace(Creator) || !string.IsNullOrWhiteSpace(Tags)
                                                 || !string.IsNullOrWhiteSpace(BackgroundImagePath);
        }

        /// <summary>
        /// The marker tag every generated map carries, no matter what the user types.
        /// The client and website read it to show the "made with AI" badge, so it is
        /// deliberately not optional.
        /// </summary>
        public const string MARKER_TAG = @"mapperatorinator";

        /// <summary>
        /// Rewrites <paramref name="oszPath"/> in place with the given overrides applied to every .osu inside.
        /// </summary>
        public static void Apply(string oszPath, MetadataOverrides overrides)
        {
            // aunque el usuario no haya tocado nada, el tag marcador va igual:
            // de ese tag sale el badge de "hecho con IA" en el cliente y la web.
            string tags = overrides.Tags ?? string.Empty;
            if (!tags.Contains(MARKER_TAG, StringComparison.OrdinalIgnoreCase))
                overrides.Tags = string.IsNullOrWhiteSpace(tags) ? MARKER_TAG : $"{tags} {MARKER_TAG}";

            string? backgroundEntryName = null;

            using (var zip = ZipFile.Open(oszPath, ZipArchiveMode.Update))
            {
                if (!string.IsNullOrWhiteSpace(overrides.BackgroundImagePath) && File.Exists(overrides.BackgroundImagePath))
                {
                    backgroundEntryName = @"background" + Path.GetExtension(overrides.BackgroundImagePath).ToLowerInvariant();

                    // replace any previous entry of the same name.
                    zip.GetEntry(backgroundEntryName)?.Delete();
                    zip.CreateEntryFromFile(overrides.BackgroundImagePath, backgroundEntryName);
                }

                if (overrides.SidecarJson != null)
                {
                    zip.GetEntry(MapperatorinatorSidecar.FILENAME)?.Delete();
                    var sidecarEntry = zip.CreateEntry(MapperatorinatorSidecar.FILENAME);
                    using (var writer = new StreamWriter(sidecarEntry.Open(), new UTF8Encoding(false)))
                        writer.Write(overrides.SidecarJson);
                }

                string? backgroundEvent = backgroundEntryName ?? overrides.BackgroundFilename;

                foreach (var entry in zip.Entries.Where(e => e.Name.EndsWith(@".osu", StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    string content;
                    using (var reader = new StreamReader(entry.Open(), Encoding.UTF8, leaveOpen: false))
                        content = reader.ReadToEnd();

                    content = rewrite(content, overrides, backgroundEvent);

                    // .osu filenames carry "artist - title (creator) [version]"; leave the
                    // name alone (it only matters cosmetically inside the archive) but the
                    // content must be written back through a fresh entry to truncate.
                    string name = entry.FullName;
                    entry.Delete();
                    var replacement = zip.CreateEntry(name);
                    using (var writer = new StreamWriter(replacement.Open(), new UTF8Encoding(false)))
                        writer.Write(content);
                }
            }
        }

        private static string rewrite(string osuContent, MetadataOverrides overrides, string? backgroundEvent)
        {
            var lines = osuContent.Replace("\r\n", "\n").Split('\n').ToList();
            string section = string.Empty;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                string trimmed = line.Trim();

                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    section = trimmed;
                    continue;
                }

                switch (section)
                {
                    case @"[General]":
                        if (overrides.AudioFilename != null && trimmed.StartsWith(@"AudioFilename", StringComparison.OrdinalIgnoreCase))
                            lines[i] = $"AudioFilename: {overrides.AudioFilename}";
                        break;

                    case @"[Metadata]":
                        lines[i] = rewriteMetadataLine(line, overrides);
                        break;

                    case @"[Events]":
                        // the background is the `0,0,"file"` event.
                        if (backgroundEvent != null && (trimmed.StartsWith(@"0,0,", StringComparison.Ordinal)))
                            lines[i] = $"0,0,\"{backgroundEvent}\",0,0";
                        break;
                }
            }

            // if we set a background but the map had no background event at all, add one.
            if (backgroundEvent != null && !lines.Any(l => l.TrimStart().StartsWith(@"0,0,", StringComparison.Ordinal)))
            {
                int eventsIdx = lines.FindIndex(l => l.Trim() == @"[Events]");
                if (eventsIdx >= 0)
                    lines.Insert(eventsIdx + 1, $"0,0,\"{backgroundEvent}\",0,0");
            }

            return string.Join("\r\n", lines);
        }

        /// <summary>
        /// Reads the difficulty name and audio filename out of the first .osu inside the
        /// archive, without extracting anything. Null if there is no .osu.
        /// </summary>
        public static (string version, string audioFilename)? Peek(string oszPath)
        {
            using (var zip = ZipFile.OpenRead(oszPath))
            {
                var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(@".osu", StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                    return null;

                string version = string.Empty;
                string audio = string.Empty;

                using (var reader = new StreamReader(entry.Open(), Encoding.UTF8))
                {
                    while (reader.ReadLine() is string line)
                    {
                        string trimmed = line.Trim();

                        if (trimmed.StartsWith(@"Version:", StringComparison.Ordinal))
                            version = trimmed.Substring(8).Trim();
                        else if (trimmed.StartsWith(@"AudioFilename:", StringComparison.OrdinalIgnoreCase))
                            audio = trimmed.Substring(14).Trim();

                        if (version.Length > 0 && audio.Length > 0)
                            break;
                    }
                }

                return (version, audio);
            }
        }

        private static string rewriteMetadataLine(string line, MetadataOverrides o)
        {
            var replacements = new List<(string key, string? value)>
            {
                (@"Title:", o.Title),
                (@"TitleUnicode:", o.Title),
                (@"Artist:", o.Artist),
                (@"ArtistUnicode:", o.Artist),
                (@"Creator:", o.Creator),
                (@"Version:", o.Version),
                (@"Tags:", o.Tags),
            };

            foreach ((string key, string? value) in replacements)
            {
                if (!string.IsNullOrWhiteSpace(value) && line.StartsWith(key, StringComparison.Ordinal))
                    return key + value;
            }

            return line;
        }
    }
}
