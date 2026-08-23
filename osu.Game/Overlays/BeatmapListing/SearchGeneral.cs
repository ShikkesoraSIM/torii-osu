// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.ComponentModel;
using osu.Framework.Localisation;
using osu.Game.Resources.Localisation.Web;

namespace osu.Game.Overlays.BeatmapListing
{
    public enum SearchGeneral
    {
        [LocalisableDescription(typeof(BeatmapsStrings), nameof(BeatmapsStrings.GeneralRecommended))]
        [Description("Recommended difficulty")]
        Recommended,

        [LocalisableDescription(typeof(BeatmapsStrings), nameof(BeatmapsStrings.GeneralConverts))]
        [Description("Include converted beatmaps")]
        Converts,

        [LocalisableDescription(typeof(BeatmapsStrings), nameof(BeatmapsStrings.GeneralFollows))]
        [Description("Subscribed mappers")]
        Follows,

        [LocalisableDescription(typeof(BeatmapsStrings), nameof(BeatmapsStrings.GeneralSpotlights))]
        Spotlights,

        [LocalisableDescription(typeof(BeatmapsStrings), nameof(BeatmapsStrings.GeneralFeaturedArtists))]
        [Description("Featured artists")]
        FeaturedArtists,

        // torii: estos dos vienen prendidos y son para APAGAR. Los mapas subidos aca y
        // los generados con IA son parte del listado como cualquier otro; si a alguien
        // no le interesan, los saca.
        [Description("Torii maps")]
        ToriiExclusive,

        [Description("AI maps")]
        AiGenerated
    }
}
