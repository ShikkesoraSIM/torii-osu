// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Overlays.BeatmapSet
{
    /// <summary>
    /// torii: el set se subio aca y no existe en osu! oficial. Va en rojo, el color con
    /// el que ya se marca todo lo propio de Torii (mods, componentes de skin).
    /// </summary>
    public partial class ToriiExclusiveBeatmapBadge : BeatmapBadge
    {
        [BackgroundDependencyLoader]
        private void load()
        {
            BadgeText = @"Torii";
            BadgeColour = Colour4.FromHex(@"ff5b6b");
        }

        /// <summary>Whether this set lives only on Torii.</summary>
        public static bool ShouldShowFor(APIBeatmapSet? beatmapSet) => beatmapSet?.IsLocal == true;
    }
}
