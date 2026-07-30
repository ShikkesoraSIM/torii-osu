// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Localisation;

namespace osu.Game.Localisation
{
    public static class ToriiWelcomeStrings
    {
        private const string prefix = @"osu.Game.Resources.Localisation.ToriiWelcome";

        public static LocalisableString WelcomeTitle => new TranslatableString(getKey(@"welcome_title"), @"Welcome to Torii!");

        public static LocalisableString WelcomeDescription => new TranslatableString(getKey(@"welcome_description"), @"The parts that aren't in stock osu!");

        public static LocalisableString ClickToResumeWelcome => new TranslatableString(getKey(@"click_to_resume_welcome"), @"Click to resume the Torii welcome guide at any point");

        // ── paso 1: que es torii ──

        public static LocalisableString IntroTitle => new TranslatableString(getKey(@"intro_title"), @"Torii");

        public static LocalisableString IntroDescription => new TranslatableString(getKey(@"intro_description"), @"Torii is a private osu! server with its own client: the same game you know, running against our servers, with a pile of extras built on top.

Everything you play here counts here. Your scores, your pp and your leaderboard spot are yours and live on Torii, completely separate from the official server, so you can chase ranks without touching your osu! account.");

        public static LocalisableString IntroPointsTitle => new TranslatableString(getKey(@"intro_points_title"), @"Points and cosmetics");

        public static LocalisableString IntroPointsDescription => new TranslatableString(getKey(@"intro_points_description"), @"Playing earns Torii points. Spend them in the store on cursor trails, name colours and auras that show up next to your name everywhere.");

        public static LocalisableString IntroRenderTitle => new TranslatableString(getKey(@"intro_render_title"), @"Replay rendering");

        public static LocalisableString IntroRenderDescription => new TranslatableString(getKey(@"intro_render_description"), @"Turn any replay into a video straight from the results screen. No external tools, no setup.");

        public static LocalisableString IntroNotesTitle => new TranslatableString(getKey(@"intro_notes_title"), @"Score notes");

        public static LocalisableString IntroNotesDescription => new TranslatableString(getKey(@"intro_notes_description"), @"Leave a note on your own scores, text or an image, so you remember how a play actually went.");

        // ── paso 2: atajos ──

        public static LocalisableString ShortcutsTitle => new TranslatableString(getKey(@"shortcuts_title"), @"Shortcuts");

        public static LocalisableString ShortcutsDescription => new TranslatableString(getKey(@"shortcuts_description"), @"Torii adds a shortcut osu! doesn't have: resize your cursor without opening settings.");

        public static LocalisableString ShortcutsIncreaseCursorSize => new TranslatableString(getKey(@"shortcuts_increase_cursor_size"), @"Bigger cursor");

        public static LocalisableString ShortcutsDecreaseCursorSize => new TranslatableString(getKey(@"shortcuts_decrease_cursor_size"), @"Smaller cursor");

        public static LocalisableString ShortcutsAnywhere => new TranslatableString(getKey(@"shortcuts_anywhere"), @"This works anywhere, not just in gameplay: the main menu, song select, the results screen, mid-map. A preview pops up while you adjust so you can see the size you're landing on before you commit to it.");

        public static LocalisableString ShortcutsWheelHint => new TranslatableString(getKey(@"shortcuts_wheel_hint"), @"Prefer the mouse? Ctrl + Shift + scroll wheel does exactly the same thing.");

        public static LocalisableString ShortcutsMenuCursorHint => new TranslatableString(getKey(@"shortcuts_menu_cursor_hint"), @"The size below is the one the shortcut moves. It also drives the menu cursor when you pick a menu style that scales with it.");

        // ── paso 3: themes ──

        public static LocalisableString ThemesTitle => new TranslatableString(getKey(@"themes_title"), @"Themes");

        public static LocalisableString ThemesDescription => new TranslatableString(getKey(@"themes_description"), @"Torii ships its own interface themes. ""Torii"" is the default: translucent glass panels you can see the game through, with soft rounded corners. ""Torii Legacy"" is the classic lazer look if you'd rather keep that. There's also a desaturated monochrome theme and the Midnight family in three hues.

You can change this whenever you want from Settings, this is just the fastest place to try one.");

        public static LocalisableString ThemesRestartNote => new TranslatableString(getKey(@"themes_restart_note"), @"Picking a different theme closes the game, because the palette is baked in when the interface is built. Open it again and you'll come straight back to this guide.");

        public static LocalisableString ThemesHueDescription => new TranslatableString(getKey(@"themes_hue_description"), @"Want your own colour instead? Tint the entire interface to whatever hue you like. This one applies instantly, no restart.");

        // ── paso 4: opciones ──

        public static LocalisableString OptionsTitle => new TranslatableString(getKey(@"options_title"), @"Options");

        public static LocalisableString OptionsDescription => new TranslatableString(getKey(@"options_description"), @"A handful of Torii-only options worth deciding on now. Nothing here is permanent, they all live in Settings > Torii afterwards.");

        public static LocalisableString OptionsConfirmDangerousButtons => new TranslatableString(getKey(@"options_confirm_dangerous_buttons"), @"Confirm Retry/Quit on long attempts");

        public static LocalisableString OptionsConfirmDangerousButtonsHint => new TranslatableString(getKey(@"options_confirm_dangerous_buttons_hint"), @"After about a minute of playing, Retry and Quit on the pause and fail screens ask for a second click. Saves the run you were about to throw away by misclicking.");

        public static LocalisableString OptionsAutoHideToolbar => new TranslatableString(getKey(@"options_auto_hide_toolbar"), @"Auto-hide the toolbar");

        public static LocalisableString OptionsAutoHideToolbarHint => new TranslatableString(getKey(@"options_auto_hide_toolbar_hint"), @"Keeps the top bar hidden and only reveals it when you push the cursor to the very top of the screen, then it tucks itself away again.");

        /// <summary>
        /// "Stable-style results screen (might cause lag)"
        /// </summary>
        public static LocalisableString OptionsStableResults => new TranslatableString(getKey(@"options_stable_results"), @"Stable-style results screen (might cause lag)");

        public static LocalisableString OptionsLegacySongSelect => new TranslatableString(getKey(@"options_legacy_song_select"), @"Stable-style song select (might cause lag)");

        public static LocalisableString OptionsLegacySongSelectHint => new TranslatableString(getKey(@"options_legacy_song_select_hint"), @"Makes song select look like osu!stable: the skinnable legacy footer and your rank panel, with the modern filter bar and info wedges hidden. It's experimental, so expect the odd rough edge.");

        public static LocalisableString OptionsUserAuras => new TranslatableString(getKey(@"options_user_auras"), @"Show user auras");

        public static LocalisableString OptionsUserAurasHint => new TranslatableString(getKey(@"options_user_auras_hint"), @"Renders the particle effect behind usernames that have one, everywhere they appear. Turn it off if you'd rather save the GPU work.");

        private static string getKey(string key) => $@"{prefix}:{key}";
    }
}
