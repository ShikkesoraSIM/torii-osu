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

        public static LocalisableString FirstRunDescription => new TranslatableString(getKey(@"first_run_description"), @"Torii is currently using its own portable data folder.\n\nIf you already use normal osu! lazer, you can link Torii to that existing data folder and immediately reuse your maps, skins, scores, and collections without importing everything again.");

        public static LocalisableString DetectedLazerFolder(string path) => new TranslatableString(getKey(@"detected_lazer_folder"), @"Detected osu! lazer data folder:\n{0}", path);

        public static LocalisableString NoDetectedLazerFolder => new TranslatableString(getKey(@"no_detected_lazer_folder"), @"No standard osu! lazer data folder was detected automatically. You can still choose it manually below.");

        public static LocalisableString FolderSelectorLabel => new TranslatableString(getKey(@"folder_selector_label"), @"Existing osu! lazer data folder");

        public static LocalisableString FolderSelectorPlaceholder => new TranslatableString(getKey(@"folder_selector_placeholder"), @"Choose the folder that contains client.realm");

        public static LocalisableString UseDetectedLazerFolder => new TranslatableString(getKey(@"use_detected_lazer_folder"), @"Use detected osu! lazer data");

        public static LocalisableString UseSelectedLazerFolder => new TranslatableString(getKey(@"use_selected_lazer_folder"), @"Use selected lazer data");

        public static LocalisableString KeepPortable => new TranslatableString(getKey(@"keep_portable"), @"Keep Torii portable for now");

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

        private static string getKey(string key) => $@"{prefix}:{key}";
    }
}
