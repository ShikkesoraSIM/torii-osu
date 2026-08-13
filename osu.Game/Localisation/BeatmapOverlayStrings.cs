// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;

namespace osu.Game.Localisation
{
    public static class BeatmapOverlayStrings
    {
        private const string prefix = @"osu.Game.Resources.Localisation.BeatmapOverlayStrings";

        /// <summary>
        /// "User content disclaimer"
        /// </summary>
        public static LocalisableString UserContentDisclaimerHeader => new TranslatableString(getKey(@"user_content_disclaimer"), @"User content disclaimer");

        /// <summary>
        /// "By turning off the &quot;Featured Artist&quot; filter, all user-uploaded content will be displayed.
        ///
        /// This includes content that may not be correctly licensed for use in Torii. Browse at your own risk."
        /// </summary>
        public static LocalisableString UserContentDisclaimerDescription => new TranslatableString(getKey(@"by_turning_off_the_featured"), @"By turning off the ""Featured Artist"" filter, all user-uploaded content will be displayed.

This includes content that may not be correctly licensed for use in Torii. Browse at your own risk.");

        /// <summary>
        /// "I understand"
        /// </summary>
        public static LocalisableString UserContentConfirmButtonText => new TranslatableString(getKey(@"understood"), @"I understand");

        /// <summary>
        /// "Featured Artists are music artists who have collaborated with osu! to make a selection of their tracks available for use in beatmaps. For some Torii releases, we showcase only featured artist beatmaps to better support the surrounding ecosystem."
        /// </summary>
        public static LocalisableString FeaturedArtistsTooltip => new TranslatableString(getKey(@"featured_artists_disabled_tooltip"), @"Featured Artists are music artists who have collaborated with osu! to make a selection of their tracks available for use in beatmaps. For some Torii releases, we showcase only featured artist beatmaps to better support the surrounding ecosystem.");

        /// <summary>
        /// "Filtering by {0} requires an active {1}"
        /// </summary>
        public static LocalisableString SupporterFilterQuote(LocalisableString filters, LocalisableString link) => new TranslatableString(getKey(@"supporter_filter_quote"), @"Filtering by {0} requires an active {1}", filters, link);

        /// <summary>
        /// "Torii supporter tag"
        /// </summary>
        public static LocalisableString SupporterFilterQuoteLinkText => new TranslatableString(getKey(@"supporter_filter_quote_link_text"), @"Torii supporter tag");

        /// <summary>
        /// "You need to be a Torii supporter to access the friend, country, or mod-specific rankings!"
        /// </summary>
        public static LocalisableString ScoreboardSupporterOnly => new TranslatableString(getKey(@"scoreboard_supporter_only"), @"You need to be a Torii supporter to access the friend, country, or mod-specific rankings!");

        private static string getKey(string key) => $@"{prefix}:{key}";
    }
}
