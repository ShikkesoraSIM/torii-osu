// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Users;

namespace osu.Game.Overlays.Profile.Sections.Medals
{
    /// <summary>
    /// Database of all available medals/achievements.
    /// This mirrors the achievement system pattern from m1-lazer-web-main.
    /// </summary>
    public static class MedalDatabase
    {
        private static readonly Dictionary<int, Medal> medals = new Dictionary<int, Medal>
        {
            // Skill-based achievements (Skill & Dedication)
            { 1, new Medal { Name = "Rising Star", Description = "Can't go forward without the first steps.", InternalName = "osu-skill-pass-1" } },
            { 2, new Medal { Name = "Constellation Prize", Description = "Definitely not a consolation prize. Now things start getting hard!", InternalName = "osu-skill-pass-2" } },
            { 3, new Medal { Name = "Building Confidence", Description = "Oh, you've SO got this.", InternalName = "osu-skill-pass-3" } },
            { 4, new Medal { Name = "Insanity Approaches", Description = "You're not twitching, you're just ready.", InternalName = "osu-skill-pass-4" } },
            { 5, new Medal { Name = "These Clarion Skies", Description = "Everything seems so clear now.", InternalName = "osu-skill-pass-5" } },
            { 6, new Medal { Name = "Above and Beyond", Description = "A cut above the rest.", InternalName = "osu-skill-pass-6" } },
            { 7, new Medal { Name = "Supremacy", Description = "All marvel before your prowess.", InternalName = "osu-skill-pass-7" } },
            { 8, new Medal { Name = "Absolution", Description = "My god, you're full of stars!", InternalName = "osu-skill-pass-8" } },
            { 9, new Medal { Name = "Event Horizon", Description = "No force dares to pull you under.", InternalName = "osu-skill-pass-9" } },
            { 10, new Medal { Name = "Phantasm", Description = "Fevered is your passion, extraordinary is your skill.", InternalName = "osu-skill-pass-10" } },
            { 11, new Medal { Name = "Totality", Description = "All the notes. Every single one.", InternalName = "osu-skill-fc-1" } },
            { 12, new Medal { Name = "Business As Usual", Description = "Two to go, please.", InternalName = "osu-skill-fc-2" } },
            { 13, new Medal { Name = "Building Steam", Description = "Hey, this isn't so bad.", InternalName = "osu-skill-fc-3" } },
            { 14, new Medal { Name = "Moving Forward", Description = "How are you even so good?", InternalName = "osu-skill-fc-4" } },
            { 15, new Medal { Name = "Paradigm Shift", Description = "Something about a stream of cores, I guess.", InternalName = "osu-skill-fc-5" } },
            { 16, new Medal { Name = "Accuracy", Description = "Couldn't they just not?", InternalName = "osu-skill-fc-6" } },
            { 17, new Medal { Name = "Ascension", Description = "New speed, new you!", InternalName = "osu-skill-fc-7" } },
            { 18, new Medal { Name = "Tremolo", Description = "You're not just mashing; you're farming.", InternalName = "osu-skill-fc-8" } },
            { 19, new Medal { Name = "Mach", Description = "The speedy hands speak for themselves.", InternalName = "osu-skill-fc-9" } },
            { 20, new Medal { Name = "Ren'ai Syndrome", Description = "All the streaming glory and nothing more.", InternalName = "osu-skill-fc-10" } },

            // Combo achievements
            { 21, new Medal { Name = "500 Combo", Description = "500 big ones! You're moving up in the world!", InternalName = "osu-combo-500" } },
            { 22, new Medal { Name = "750 Combo", Description = "750 notes back to back? Woah.", InternalName = "osu-combo-750" } },
            { 23, new Medal { Name = "1000 Combo", Description = "A thousand reasons why you rock at this game.", InternalName = "osu-combo-1000" } },
            { 24, new Medal { Name = "2000 Combo", Description = "Two thousand times you've clicked your way to glory!", InternalName = "osu-combo-2000" } },

            // Mod introduction achievements
            { 89, new Medal { Name = "Finality", Description = "High stakes, no regrets.", InternalName = "all-intro-suddendeath" } },
            { 90, new Medal { Name = "Perfectionist", Description = "Accept nothing but the best.", InternalName = "all-intro-perfect" } },
            { 91, new Medal { Name = "Rock Around The Clock", Description = "You can't stop the rock.", InternalName = "all-intro-hardrock" } },
            { 92, new Medal { Name = "Time And A Half", Description = "Having a right ol' time. One and a half of them, almost.", InternalName = "all-intro-doubletime" } },
            { 93, new Medal { Name = "Sweet Rave Party", Description = "Founded in the fine tradition of changing things that were just fine as they were.", InternalName = "all-intro-nightcore" } },
            { 94, new Medal { Name = "Blindsight", Description = "I can see just perfectly.", InternalName = "all-intro-hidden" } },
            { 95, new Medal { Name = "Are You Afraid Of The Dark?", Description = "Harder than it looks, probably because it's hard to look.", InternalName = "all-intro-flashlight" } },
            { 96, new Medal { Name = "Dial It Right Back", Description = "Sometimes you just want to take it easy.", InternalName = "all-intro-easy" } },
            { 97, new Medal { Name = "Risk Averse", Description = "Safety nets are fun!", InternalName = "all-intro-nofail" } },
            { 98, new Medal { Name = "Slowboat", Description = "You got there. Eventually.", InternalName = "all-intro-halftime" } },
            { 99, new Medal { Name = "Burned Out", Description = "One cannot always spin to win.", InternalName = "all-intro-spunout" } },
            { 100, new Medal { Name = "Gear Shift", Description = "Tailor your experience to your perfect fit.", InternalName = "all-intro-conversion" } },
            { 101, new Medal { Name = "Game Night", Description = "Mum said it's my turn with the beatmap!", InternalName = "all-intro-fun" } },

            // Daily Challenge (streak medals)
            { 102, new Medal { Name = "Daily Sprout", Description = "Ready for anything.", InternalName = "all-skill-dc-1" } },
            { 103, new Medal { Name = "Weekly Sapling", Description = "Circadian rhythm calibrated.", InternalName = "all-skill-dc-7" } },
            { 104, new Medal { Name = "Monthly Shrub", Description = "In for the grind.", InternalName = "all-skill-dc-30" } },

            // Secret achievements (Hush-Hush)
            { 105, new Medal { Name = "Jackpot", Description = "Lucky sevens is a mild understatement.", InternalName = "all-secret-jackpot" } },
            { 106, new Medal { Name = "Nonstop", Description = "Breaks? What are those?", InternalName = "all-secret-nonstop" } },
            { 107, new Medal { Name = "Time Dilation", Description = "Longer is shorter when all is said and done.", InternalName = "all-secret-tidi" } },
            { 108, new Medal { Name = "To The Core", Description = "In for a penny, in for a pound. Pounding bass, that is.", InternalName = "all-secret-tothecore" } },
            { 109, new Medal { Name = "When You See It", Description = "Three numbers which will haunt you forevermore.", InternalName = "all-secret-when-you-see-it" } },
            { 110, new Medal { Name = "Prepared", Description = "Do it for real next time.", InternalName = "all-secret-prepared" } },
            { 111, new Medal { Name = "Reckless Abandon", Description = "Throw it all to the wind.", InternalName = "all-secret-reckless" } },
            { 112, new Medal { Name = "Lights Out", Description = "The party's just getting started.", InternalName = "all-secret-lightsout" } },
            { 113, new Medal { Name = "Camera Shy", Description = "Stop being cute.", InternalName = "all-secret-uguushy" } },
            { 114, new Medal { Name = "The Sun of All Fears", Description = "Unfortunate.", InternalName = "all-secret-nuked" } },
            { 115, new Medal { Name = "Hour Before The Down", Description = "Eleven skies of everlasting sunrise.", InternalName = "all-secret-hourbeforethedawn" } },
            { 116, new Medal { Name = "Slow And Steady", Description = "Win the race, or start again.", InternalName = "all-secret-slowandsteady" } },
            { 117, new Medal { Name = "No Time To Spare", Description = "Places to be, things to do.", InternalName = "all-secret-ntts" } },
            { 118, new Medal { Name = "Sognare", Description = "A dream in stop-motion, soon forever gone.", InternalName = "all-secret-sognare" } },
            { 119, new Medal { Name = "Realtor Extraordinaire", Description = "An acre-wide stride.", InternalName = "all-secret-realtor" } },
            { 120, new Medal { Name = "Impeccable", Description = "Speed matters not to the exemplary.", InternalName = "all-secret-impeccable" } },
            { 121, new Medal { Name = "Aeon", Description = "In the mire of thawing time, memory shall be your guide.", InternalName = "all-secret-aeon" } },
            { 122, new Medal { Name = "Quick Maths", Description = "Beats per minute over... this isn't quick at all!", InternalName = "all-secret-quickmaffs" } },
            { 123, new Medal { Name = "Kaleidoscope", Description = "So many pretty colours. Most of them red.", InternalName = "all-secret-kaleidoscope" } },
            { 124, new Medal { Name = "Valediction", Description = "One last time.", InternalName = "all-secret-valediction" } },
            { 127, new Medal { Name = "Right On Time", Description = "The first minute is always the hardest.", InternalName = "all-secret-rightontime" } },
            { 128, new Medal { Name = "Not Again", Description = "Regret everything.", InternalName = "all-secret-notagain" } },
            { 129, new Medal { Name = "Deliberation", Description = "The challenge remains.", InternalName = "all-secret-deliberation" } },
            { 130, new Medal { Name = "Clarity", Description = "And yet in our memories, you remain crystal clear.", InternalName = "all-secret-clarity" } },
            { 131, new Medal { Name = "Autocreation", Description = "Absolute rule.", InternalName = "all-secret-autocreation" } },
            { 132, new Medal { Name = "Value Your Identity", Description = "As perfect as you are.", InternalName = "all-secret-identity" } },
            { 133, new Medal { Name = "By The Skin Of The Teeth", Description = "You're that accurate.", InternalName = "all-secret-skinoftheteeth" } },
            { 134, new Medal { Name = "Meticulous Mayhem", Description = "How did we get here?", InternalName = "all-secret-meticulousmayhem" } },
        };

        private static readonly Dictionary<int, string> medalCategories = new Dictionary<int, string>
        {
            // Skill & Dedication (1-20)
            { 1, "Skill & Dedication" }, { 2, "Skill & Dedication" }, { 3, "Skill & Dedication" }, { 4, "Skill & Dedication" }, { 5, "Skill & Dedication" },
            { 6, "Skill & Dedication" }, { 7, "Skill & Dedication" }, { 8, "Skill & Dedication" }, { 9, "Skill & Dedication" }, { 10, "Skill & Dedication" },
            { 11, "Skill & Dedication" }, { 12, "Skill & Dedication" }, { 13, "Skill & Dedication" }, { 14, "Skill & Dedication" }, { 15, "Skill & Dedication" },
            { 16, "Skill & Dedication" }, { 17, "Skill & Dedication" }, { 18, "Skill & Dedication" }, { 19, "Skill & Dedication" }, { 20, "Skill & Dedication" },

            // Combo Milestones (21-24)
            { 21, "Combo Milestones" }, { 22, "Combo Milestones" }, { 23, "Combo Milestones" }, { 24, "Combo Milestones" },

            // Mod Introduction (89-101)
            { 89, "Mod Introduction" }, { 90, "Mod Introduction" }, { 91, "Mod Introduction" }, { 92, "Mod Introduction" }, { 93, "Mod Introduction" },
            { 94, "Mod Introduction" }, { 95, "Mod Introduction" }, { 96, "Mod Introduction" }, { 97, "Mod Introduction" }, { 98, "Mod Introduction" },
            { 99, "Mod Introduction" }, { 100, "Mod Introduction" }, { 101, "Mod Introduction" },

            // Daily Challenge (102-104)
            { 102, "Daily Challenge" }, { 103, "Daily Challenge" }, { 104, "Daily Challenge" },

            // Hush-Hush (105-134)
            { 105, "Hush-Hush" }, { 106, "Hush-Hush" }, { 107, "Hush-Hush" }, { 108, "Hush-Hush" }, { 109, "Hush-Hush" },
            { 110, "Hush-Hush" }, { 111, "Hush-Hush" }, { 112, "Hush-Hush" }, { 113, "Hush-Hush" }, { 114, "Hush-Hush" },
            { 115, "Hush-Hush" }, { 116, "Hush-Hush" }, { 117, "Hush-Hush" }, { 118, "Hush-Hush" }, { 119, "Hush-Hush" },
            { 120, "Hush-Hush" }, { 121, "Hush-Hush" }, { 122, "Hush-Hush" }, { 123, "Hush-Hush" }, { 124, "Hush-Hush" },
            { 127, "Hush-Hush" }, { 128, "Hush-Hush" }, { 129, "Hush-Hush" }, { 130, "Hush-Hush" }, { 131, "Hush-Hush" },
            { 132, "Hush-Hush" }, { 133, "Hush-Hush" }, { 134, "Hush-Hush" },
        };

        /// <summary>
        /// Get all available medals in the database.
        /// </summary>
        public static IReadOnlyDictionary<int, Medal> GetMedals() => medals;

        /// <summary>
        /// Get a specific medal by achievement ID.
        /// </summary>
        public static Medal? GetMedal(int achievementId)
        {
            medals.TryGetValue(achievementId, out var medal);
            return medal;
        }

        /// <summary>
        /// Get the category name for a specific achievement ID.
        /// </summary>
        public static string GetCategory(int achievementId)
        {
            if (medalCategories.TryGetValue(achievementId, out var category))
                return category;
            return "Other";
        }
    }
}
