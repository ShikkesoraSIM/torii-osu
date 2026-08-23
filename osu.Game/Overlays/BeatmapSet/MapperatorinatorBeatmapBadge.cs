// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Overlays.BeatmapSet
{
    /// <summary>
    /// torii: marks a beatmap generated with Mapperatorinator. Every map the client
    /// generates carries the marker tag, so this is the one place that decides how
    /// AI-generated maps announce themselves.
    /// </summary>
    public partial class MapperatorinatorBeatmapBadge : BeatmapBadge
    {
        /// <summary>The tag stamped into every generated map (see OszPostProcessor).</summary>
        public const string MARKER_TAG = @"mapperatorinator";

        [BackgroundDependencyLoader]
        private void load()
        {
            // corto a proposito: en una card conviven el badge de Torii y este, y
            // "AI GENERATED" entero se come el ancho del titulo.
            BadgeText = @"AI gen";
            BadgeColour = Colour4.FromHex(@"4aa8ff");
        }

        /// <summary>
        /// Whether this set was generated with Mapperatorinator. The server's answer wins:
        /// the tag lives in one .osu and anyone can wipe it by editing the metadata, while
        /// the server marks the set from the file the generator leaves inside it. The tag
        /// stays as the fallback for anything the server hasn't marked.
        /// </summary>
        public static bool ShouldShowFor(APIBeatmapSet? beatmapSet) => beatmapSet?.Ai == true || HasMarkerTag(beatmapSet?.Tags);

        public static bool HasMarkerTag(string? tags)
        {
            if (string.IsNullOrEmpty(tags))
                return false;

            foreach (string tag in tags.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (tag.Equals(MARKER_TAG, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
