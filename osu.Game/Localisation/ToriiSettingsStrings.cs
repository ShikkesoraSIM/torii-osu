// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;

namespace osu.Game.Localisation
{
    public static class ToriiSettingsStrings
    {
        private const string prefix = @"osu.Game.Resources.Localisation.ToriiSettings";

        public static LocalisableString DataSourceHeader => new TranslatableString(getKey(@"data_source_header"), @"Torii data source");

        public static LocalisableString FirstRunHeader => new TranslatableString(getKey(@"first_run_header"), @"Welcome to Torii");

        public static LocalisableString FirstRunDescription => new TranslatableString(getKey(@"first_run_description"), @"We found your osu!lazer folder on this computer. Import it and Torii opens with all your beatmaps, skins, scores and collections already in place. Nothing is copied or moved, so it is instant and your osu!lazer install is left exactly as it is.");

        public static LocalisableString FirstRunSharedFolderNote => new TranslatableString(getKey(@"first_run_shared_folder_note"), @"From then on Torii and osu!lazer share that folder, so a beatmap you download in one is already waiting for you in the other.");

        public static LocalisableString DetectedLazerFolder(string path) => new TranslatableString(getKey(@"detected_lazer_folder"), "Detected osu!lazer folder:\n{0}", path);

        public static LocalisableString NoDetectedLazerFolder => new TranslatableString(getKey(@"no_detected_lazer_folder"), @"We could not find an osu!lazer folder automatically. You can point Torii at one yourself, or just start fresh and sort this out later.");

        public static LocalisableString FolderSelectorLabel => new TranslatableString(getKey(@"folder_selector_label"), @"Existing osu! lazer data folder");

        public static LocalisableString FolderSelectorPlaceholder => new TranslatableString(getKey(@"folder_selector_placeholder"), @"Choose the folder that contains client.realm");

        public static LocalisableString UseDetectedLazerFolder => new TranslatableString(getKey(@"use_detected_lazer_folder"), @"Import from detected osu! folder");

        public static LocalisableString UseSelectedLazerFolder => new TranslatableString(getKey(@"use_selected_lazer_folder"), @"Import from this folder");

        public static LocalisableString KeepPortable => new TranslatableString(getKey(@"keep_portable"), @"Stay fresh for now");

        public static LocalisableString ImportAnotherLazerFolder => new TranslatableString(getKey(@"import_another_lazer_folder"), @"I want to import another osu! Lazer folder, not the detected one");

        public static LocalisableString ChooseLazerFolderManually => new TranslatableString(getKey(@"choose_lazer_folder_manually"), @"Choose my osu! lazer folder");

        public static LocalisableString StayFreshInstead => new TranslatableString(getKey(@"stay_fresh_instead"), @"Never mind, stay fresh for now");

        public static LocalisableString SkipFirstRunSetup => new TranslatableString(getKey(@"skip_first_run_setup"), @"Skip for now");

        public static LocalisableString ChangeLaterInSettings => new TranslatableString(getKey(@"change_later_in_settings"), @"You can change this later from Settings > Torii.");

        public static LocalisableString InvalidLazerFolder => new TranslatableString(getKey(@"invalid_lazer_folder"), @"That folder does not look like an osu! lazer data folder. Choose the folder that contains client.realm.");

        public static LocalisableString CurrentModePortable => new TranslatableString(getKey(@"current_mode_portable"), @"Current mode: portable Torii data");

        public static LocalisableString CurrentModeLinked(string path) => new TranslatableString(getKey(@"current_mode_linked"), @"Current mode: linked to {0}", path);

        public static LocalisableString OpenActiveDataFolder => new TranslatableString(getKey(@"open_active_data_folder"), @"Open active data folder");

        public static LocalisableString ConfigureLazerDataFolder => new TranslatableString(getKey(@"configure_lazer_data_folder"), @"Link to osu! lazer data folder");

        public static LocalisableString ManageDataSource => new TranslatableString(getKey(@"manage_data_source"), @"Manage Torii data source");

        public static LocalisableString SwitchToPortableMode => new TranslatableString(getKey(@"switch_to_portable_mode"), @"Switch back to portable Torii data");

        public static LocalisableString SwitchToPortableModeDescription => new TranslatableString(getKey(@"switch_to_portable_mode_description"), @"Torii will close and use its own portable data folder again on next launch.");

        public static LocalisableString LinkLazerDataFolderDescription => new TranslatableString(getKey(@"link_lazer_data_folder_description"), @"Pick an existing osu! lazer data folder. Torii will close and use it on next launch.");

        public static LocalisableString ContinueSetup => new TranslatableString(getKey(@"continue_setup"), @"Continue setup");

        public static LocalisableString PickLazerFolderHeader => new TranslatableString(getKey(@"pick_lazer_folder_header"), @"Select your osu! lazer data folder");

        public static LocalisableString PickLazerFolderDescription => new TranslatableString(getKey(@"pick_lazer_folder_description"), @"Browse to the osu!lazer folder you want Torii to use. It is the folder that contains client.realm. Nothing is copied or moved: Torii reads your data where it already lives, and both clients share the folder from then on.");

        public static LocalisableString SkipBreaks => new TranslatableString(getKey(@"skip_breaks"), @"Skip breaks mid-map");

        public static LocalisableString SkipBreaksHint => new TranslatableString(getKey(@"skip_breaks_hint"), @"Show a SKIP button during a map's break periods so you can fast-forward past long breaks instead of waiting. The score is unaffected (breaks contain no notes).");

        public static LocalisableString SkipBreaksSingleConfirmation => new TranslatableString(getKey(@"skip_breaks_single_confirmation"), @"Skip breaks with a single confirmation");

        public static LocalisableString SkipBreaksSingleConfirmationHint => new TranslatableString(getKey(@"skip_breaks_single_confirmation_hint"), @"When on, the mid-map skip button activates on a single press. When off, it asks for a quick second press first so an accidental tap can't pull you out of a break.");

        // ── First-time mid-map skip briefing popup ──
        public static LocalisableString SkipBreakBriefingTitle => new TranslatableString(getKey(@"skip_break_briefing_title"), @"You can skip breaks now");

        public static LocalisableString SkipBreakBriefingBody => new TranslatableString(getKey(@"skip_break_briefing_body"), "We added a button that skips long mid-map breaks. To make sure you don't accidentally skip one and end up in a rough spot, skipping takes a quick double press for now, and this is a one-time warning to let you know.\n\nPrefer a single press? Turn it on below (or later in Settings > Torii > Gameplay). You'll only see this once.");

        public static LocalisableString SkipBreakBriefingInlineTip => new TranslatableString(getKey(@"skip_break_briefing_inline_tip"), @"No need to open settings, flip it right here:");

        public static LocalisableString SkipBreakBriefingDismiss => new TranslatableString(getKey(@"skip_break_briefing_dismiss"), @"Got it, back to the map");

        private static string getKey(string key) => $@"{prefix}:{key}";
    }
}
