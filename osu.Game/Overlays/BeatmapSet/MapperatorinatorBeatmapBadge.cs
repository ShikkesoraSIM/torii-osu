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
            BadgeText = @"AI generated";
            // rosa: no lo usa ningun otro badge, asi que no se confunde con spotlight
            // (verde) ni featured artist (azul).
            BadgeColour = Colour4.FromHex(@"ff6ec7");
        }

        /// <summary>Whether this set was generated with Mapperatorinator.</summary>
        public static bool ShouldShowFor(APIBeatmapSet? beatmapSet) => HasMarkerTag(beatmapSet?.Tags);

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
