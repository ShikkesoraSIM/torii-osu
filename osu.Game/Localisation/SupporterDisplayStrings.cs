// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;

namespace osu.Game.Localisation
{
    public static class SupporterDisplayStrings
    {
        private const string prefix = @"osu.Game.Resources.Localisation.SupporterDisplay";

        /// <summary>
        /// "Eternal thanks to you for supporting Torii"
        /// </summary>
        public static LocalisableString ThankYouForSupporting => new TranslatableString(getKey(@"thank_you_for_supporting"), @"Eternal thanks to you for supporting Torii");

        /// <summary>
        /// "Consider becoming a [Torii supporter]({0}) to help support Torii's development"
        /// </summary>
        public static LocalisableString ConsiderBecomingASupporter(string url) => new TranslatableString(getKey(@"consider_becoming_a_supporter"), @"Consider becoming a [Torii supporter]({0}) to help support Torii's development", url);

        private static string getKey(string key) => $@"{prefix}:{key}";
    }
}
