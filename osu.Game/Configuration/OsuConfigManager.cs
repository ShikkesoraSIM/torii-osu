// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Framework;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Configuration.Tracking;
using osu.Framework.Extensions;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps.Drawables.Cards;
using osu.Game.Input;
using osu.Game.Input.Bindings;
using osu.Game.Localisation;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Overlays.Dashboard.Friends;
using osu.Game.Overlays.Mods.Input;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Edit.Compose.Components;
using osu.Game.Screens.OnlinePlay.Lounge.Components;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osu.Game.Skinning;
using osu.Game.Users;

namespace osu.Game.Configuration
{
    public class OsuConfigManager : IniConfigManager<OsuSetting>, IGameplaySettings
    {
        /// <summary>
        /// torii escribe su config + token OAuth en torii.ini en vez del game.ini de upstream. asi cuando
        /// el usuario corre el cliente oficial de ppy sobre la misma carpeta de datos, ese cliente es dueño
        /// de game.ini y los dos no se pisan las keys (ni el login). lo compartido (mapas, skins, replays,
        /// la realm) sigue compartido porque vive en archivos/carpetas aparte que ningun config toca.
        /// </summary>
        public const string TORII_CONFIG_FILENAME = "torii.ini";

        private const string upstream_config_filename = "game.ini";

        protected override string Filename => TORII_CONFIG_FILENAME;

        public OsuConfigManager(Storage storage)
            : base(prepareMigratedStorage(storage))
        {
        }

        /// <summary>
        /// migracion de una sola vez: deja torii.ini con el config + token que el usuario ya tenia (en
        /// game.ini) antes de que el ctor base lea de ahi. idempotente. asi al actualizar nadie pierde
        /// sesion ni settings, y game.ini queda intacto para el cliente oficial. errores se tragan
        /// (log Important): el peor caso es un torii.ini de defaults (login una vez), nada mas se pierde.
        /// </summary>
        private static Storage prepareMigratedStorage(Storage storage)
        {
            try
            {
                bool toriiExists = storage.Exists(TORII_CONFIG_FILENAME);

                // ya migrado (torii.ini tiene un Token): no hacemos nada.
                if (toriiExists && toriiIniContainsToken(storage))
                    return storage;

                // copiamos desde game.ini si existe: el usuario conserva sesion + todas las settings.
                if (storage.Exists(upstream_config_filename))
                {
                    using (Stream src = storage.GetStream(upstream_config_filename, FileAccess.Read, FileMode.Open))
                    using (Stream dst = storage.CreateFileSafely(TORII_CONFIG_FILENAME))
                        src.CopyTo(dst);
                }
                // install nuevo sin archivos: nada que hacer, IniConfigManager crea torii.ini de defaults.
            }
            catch (Exception ex)
            {
                Logger.Log($"[torii.ini] migracion desde {upstream_config_filename} fallo: {ex.Message}", LoggingTarget.Runtime, LogLevel.Important);
            }

            return storage;
        }

        /// <summary>
        /// señal barata de que torii.ini ya tiene un config primario (la presencia de cualquier key
        /// "Token" lo prueba), asi no re-copiamos game.ini encima en cada arranque.
        /// </summary>
        private static bool toriiIniContainsToken(Storage storage)
        {
            try
            {
                using (Stream stream = storage.GetStream(TORII_CONFIG_FILENAME, FileAccess.Read, FileMode.Open))
                using (var reader = new StreamReader(stream))
                {
                    string? line;

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.TrimStart().StartsWith("Token", StringComparison.Ordinal))
                            return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        protected override void InitialiseDefaults()
        {
            // UI/selection defaults
            SetDefault(OsuSetting.Ruleset, string.Empty);
            SetDefault(OsuSetting.Skin, SkinInfo.ARGON_SKIN.ToString());
            SetDefault(OsuSetting.CycleSkinsThroughFavoritesOnly, false);

            SetDefault(OsuSetting.BeatmapDetailTab, BeatmapDetailTab.Local);
            SetDefault(OsuSetting.BeatmapLeaderboardSortMode, LeaderboardSortMode.Score);
            SetDefault(OsuSetting.BeatmapDetailModsFilter, false);

            SetDefault(OsuSetting.ShowConvertedBeatmaps, true);
            SetDefault(OsuSetting.DisplayStarsMinimum, 0.0, 0, 10, 0.1);
            SetDefault(OsuSetting.DisplayStarsMaximum, 10.1, 0, 10.1, 0.1);

            SetDefault(OsuSetting.SongSelectGroupMode, GroupMode.None);
            SetDefault(OsuSetting.SongSelectSortingMode, SortMode.Title);

            SetDefault(OsuSetting.RandomSelectAlgorithm, RandomSelectAlgorithm.RandomPermutation);
            SetDefault(OsuSetting.ModSelectHotkeyStyle, ModSelectHotkeyStyle.Sequential);
            SetDefault(OsuSetting.ModSelectTextSearchStartsActive, true);

            SetDefault(OsuSetting.ChatDisplayHeight, ChatOverlay.DEFAULT_HEIGHT, 0.2f, 1f, 0.01f);

            SetDefault(OsuSetting.BeatmapListingCardSize, BeatmapCardSize.Normal);
            SetDefault(OsuSetting.BeatmapListingFeaturedArtistFilter, true);
            // torii: los mapas de aca y los generados con IA entran al listado como
            // cualquier otro; estos dos son para apagarlos, no para pedirlos.
            SetDefault(OsuSetting.BeatmapListingToriiFilter, true);
            SetDefault(OsuSetting.BeatmapListingAiFilter, true);

            SetDefault(OsuSetting.ProfileCoverExpanded, true);

            SetDefault(OsuSetting.ToolbarClockDisplayMode, ToolbarClockDisplayMode.Full);

            SetDefault(OsuSetting.SongSelectBackgroundBlur, false);
            SetDefault(OsuSetting.UnslantedSongSelectUI, false);

            // Online settings
            SetDefault(OsuSetting.Username, string.Empty);
            SetDefault(OsuSetting.Token, string.Empty);

            SetDefault(OsuSetting.AutomaticallyDownloadMissingBeatmaps, true);

            SetDefault(OsuSetting.SavePassword, true).ValueChanged += enabled =>
            {
                if (enabled.NewValue)
                    SetValue(OsuSetting.SaveUsername, true);
                else
                    GetBindable<string>(OsuSetting.Token).SetDefault();
            };

            SetDefault(OsuSetting.SaveUsername, true).ValueChanged += enabled =>
            {
                if (!enabled.NewValue)
                {
                    GetBindable<string>(OsuSetting.Username).SetDefault();
                    SetValue(OsuSetting.SavePassword, false);
                }
            };

            SetDefault(OsuSetting.ExternalLinkWarning, true);
            SetDefault(OsuSetting.PreferNoVideo, false);

            SetDefault(OsuSetting.ShowOnlineExplicitContent, false);

            SetDefault(OsuSetting.NotifyOnUsernameMentioned, true);
            SetDefault(OsuSetting.NotifyOnPrivateMessage, true);
            SetDefault(OsuSetting.NotifyOnFriendPresenceChange, true);

            // Audio
            SetDefault(OsuSetting.VolumeInactive, 0.25, 0, 1, 0.01);

            SetDefault(OsuSetting.MenuVoice, true);
            SetDefault(OsuSetting.MenuMusic, true);
            SetDefault(OsuSetting.MenuTips, true);

            SetDefault(OsuSetting.AudioOffset, 0, -500.0, 500.0, 1);

            SetDefault(OsuSetting.AutomaticallyAdjustBeatmapOffset, false);

            // Input
            SetDefault(OsuSetting.MenuCursorSize, 1.0f, 0.5f, 2f, 0.01f);
            SetDefault(OsuSetting.GameplayCursorSize, 1.0f, 0.1f, 2f, 0.01f);
            SetDefault(OsuSetting.GameplayCursorDuringTouch, false);
            SetDefault(OsuSetting.AutoCursorSize, false);

            SetDefault(OsuSetting.MouseDisableButtons, false);
            SetDefault(OsuSetting.MouseDisableWheel, false);
            SetDefault(OsuSetting.ConfineMouseMode, OsuConfineMouseMode.DuringGameplay);

            SetDefault(OsuSetting.TouchDisableGameplayTaps, false);

            // Graphics
            SetDefault(OsuSetting.ShowFpsDisplay, false);
            SetDefault(OsuSetting.ToriiPerformanceCounter, true);

            // Torii: force the SDL3 windowing/input backend on Linux + macOS.
            // Read pre-host from game.ini by Program.cs to flip OSU_SDL3 before
            // the framework bakes in its backend choice. No-op on Windows/mobile.
            SetDefault(OsuSetting.ForceSDL3, false);

            SetDefault(OsuSetting.ShowStoryboard, true);
            SetDefault(OsuSetting.BeatmapSkins, true);
            SetDefault(OsuSetting.BeatmapColours, true);
            SetDefault(OsuSetting.BeatmapHitsounds, true);

            SetDefault(OsuSetting.CursorRotation, true);

            SetDefault(OsuSetting.MenuParallax, true);

            // See https://stackoverflow.com/a/63307411 for default sourcing.
            SetDefault(OsuSetting.Prefer24HourTime, !CultureInfoHelper.SystemCulture.DateTimeFormat.ShortTimePattern.Contains(@"tt"));

            // Gameplay
            SetDefault(OsuSetting.PositionalHitsoundsLevel, 0.2f, 0, 1, 0.01f);
            SetDefault(OsuSetting.DimLevel, 0.7, 0, 1, 0.01);
            SetDefault(OsuSetting.BlurLevel, 0, 0, 1, 0.01);
            SetDefault(OsuSetting.LightenDuringBreaks, true);

            SetDefault(OsuSetting.HitLighting, true);
            SetDefault(OsuSetting.StarFountains, true);

            SetDefault(OsuSetting.HUDVisibilityMode, HUDVisibilityMode.Always);
            SetDefault(OsuSetting.ShowHealthDisplayWhenCantFail, true);
            SetDefault(OsuSetting.FadePlayfieldWhenHealthLow, true);
            SetDefault(OsuSetting.KeyOverlay, false);
            SetDefault(OsuSetting.ReplaySettingsOverlay, true);
            SetDefault(OsuSetting.ReplayPlaybackControlsExpanded, true);
            SetDefault(OsuSetting.GameplayLeaderboard, true);
            SetDefault(OsuSetting.AlwaysPlayFirstComboBreak, true);

            SetDefault(OsuSetting.FloatingComments, false);

            SetDefault(OsuSetting.ScoreDisplayMode, ScoringMode.Standardised);

            SetDefault(OsuSetting.IncreaseFirstObjectVisibility, true);
            SetDefault(OsuSetting.GameplayDisableWinKey, true);

            // Update
            SetDefault(OsuSetting.ReleaseStream, ReleaseStream.Torii);
            // torii: ultimo stream del BUILD que vimos, sacado del suffix de la version
            // (-nova/-torii/-vanilla). se usa para alinear ReleaseStream al stream real
            // del build al arrancar, sin pisar un cambio de stream a medio aplicar.
            SetDefault(OsuSetting.LastKnownBuildStream, string.Empty);

            SetDefault(OsuSetting.Version, string.Empty);

            // Torii: server pulse toolbar widget (live "currently playing" stats).
            SetDefault(OsuSetting.ToriiServerPulseEnabled, true);

            // Torii: cosmetic UI theme. glass es el default nuevo; el look clasico quedo como "Torii Legacy".
            SetDefault(OsuSetting.UITheme, UIThemeOption.LiquidGlass);

            // Torii: migracion one-shot del theme default (Torii clasico -> glass) + su popup de aviso.
            // el flag "migrated" corre la migracion una sola vez; el "pending" lo prende la migracion solo
            // si movio a alguien, y lo consume el popup al mostrarse (asi el aviso sale una unica vez).
            SetDefault(OsuSetting.ToriiGlassDefaultMigrated, false);
            SetDefault(OsuSetting.ToriiNewThemePopupPending, false);

            // Torii: Potato Mode (extreme-perf preset, read once at startup, restart-gated).
            SetDefault(OsuSetting.ToriiPotatoMode, false);

            // Torii: input/audio/update thread rate. Drives GameHost.ToriiInputAudioHz live.
            SetDefault(OsuSetting.ToriiInputAudioHz, ToriiInputAudioHzMode.Hz2000);
            // torii: marca de una sola vez. el primer arranque siembra el default de hz segun la
            // maquina (ver OsuGameBase) sin pisar nunca una eleccion posterior del usuario.
            SetDefault(OsuSetting.ToriiInputAudioHzAutoTuned, false);
            // torii: segundo pase del tuner. El primero dejaba a los quad-core viejos con
            // mucha ram en 2000; este los baja UNA vez a lo que el tuner arreglado diria.
            SetDefault(OsuSetting.ToriiInputAudioHzRetunedWeakQuad, false);

            // Torii: cuanta CPU usa el recalculo de star rating de fondo. default gentil (lazer) asi si el
            // popup de arranque se cierra/falla, no se come la maquina. el popup la guarda como preferencia.
            SetDefault(OsuSetting.ToriiDifficultyRecalcMode, ToriiDifficultyRecalcMode.LazerDefault);

            // torii nova: marca de una sola vez para el reset del renderer. la Nova vieja forzaba
            // Renderer=Deferred en el framework.ini; al migrar a toriirefresh (misma data dir) ese
            // valor queda guardado y los traeria al renderer hiccupy. en el primer arranque post-update
            // lo reseteamos a Automatic (ver OsuGame), una sola vez, sin pisar futuras elecciones.
            SetDefault(OsuSetting.ToriiNovaRendererMigrated, false);

            // torii: key debounce anti-chatter. dropea el re-press de una tecla de gameplay que cae
            // dentro del umbral despues de su ultimo release (el doble-tap fantasma de teclados
            // rapid-trigger / switches gastados). umbral en ms reales, bien abajo del gap minimo de
            // un tap real (un stream de un dedo a 200 bpm son ~75ms) o se comeria inputs validos.
            SetDefault(OsuSetting.ToriiKeyDebounceEnabled, false);
            SetDefault(OsuSetting.ToriiKeyDebounceThresholdMs, 15.0, 1.0, 50.0);

            // torii: render de replays a video via o!rdr (panel de la results screen).
            // el panel recuerda lo ultimo elegido; la key de o!rdr vive solo en el server.
            SetDefault(OsuSetting.ToriiRenderResolution, "1280x720");
            SetDefault(OsuSetting.ToriiRenderSkin, "default");
            SetDefault(OsuSetting.ToriiRenderMotionBlur, false);
            SetDefault(OsuSetting.ToriiRenderShare, true);

            // torii: auto-esconder la toolbar. aparece al llevar el mouse bien arriba y se va sola tras
            // un ratito. pensado para el legacy song select (toolbar abajo = experiencia 16:9 completa).
            SetDefault(OsuSetting.ToriiAutoHideToolbar, false);

            // torii: popups de descubrimiento (se muestran una sola vez). uno recomienda esconder la
            // toolbar a quien usa el stable song select; el otro le avisa a quien NO lo usa que existe,
            // tras ver la song select varias veces.
            SetDefault(OsuSetting.ToriiToolbarHintShown, false);
            SetDefault(OsuSetting.ToriiStablePromoShown, false);
            SetDefault(OsuSetting.ToriiSongSelectViews, 0);
            // torii: cuantas veces se togglo la toolbar (Ctrl+T). a las ~30 sugerimos el auto-hide.
            SetDefault(OsuSetting.ToriiToolbarToggleCount, 0);

            // Torii cosmetics / economy / UI
            SetDefault(OsuSetting.UserAuraEnabled, true);
            SetDefault(OsuSetting.EquippedNameColour, string.Empty);
            SetDefault(OsuSetting.EquippedCursorTrail, string.Empty);
            SetDefault(OsuSetting.OwnedCursorTrails, string.Empty);
            SetDefault(OsuSetting.CursorTrailCustomisations, string.Empty);
            SetDefault(OsuSetting.CursorTrailAdjustUnlocked, false);
            SetDefault(OsuSetting.CosmeticStoreDisabled, string.Empty);
            SetDefault(OsuSetting.CosmeticStorePotatoMode, false);
            SetDefault(OsuSetting.CosmeticsReducedMotion, false);
            SetDefault(OsuSetting.CosmeticsHidden, false);
            SetDefault(OsuSetting.ToriiPointsBalance, 0);
            SetDefault(OsuSetting.ToriiPointsFeedCursor, 0);
            SetDefault(OsuSetting.CustomUIHue, (float)osu.Game.Overlays.OverlayColourScheme.Purple.GetHue(), 0f, 359f, 1f);
            SetDefault(OsuSetting.CustomUIHueEnabled, false);
            SetDefault(OsuSetting.CustomUIHueApplyToMenu, false);
            SetDefault(OsuSetting.CustomUIHueApplyToOverlays, true);
            SetDefault(OsuSetting.CustomUIHueApplyToSettingsPanel, true);
            SetDefault(OsuSetting.CustomUIAccentEnabled, false);
            SetDefault(OsuSetting.CustomUIAccentHue, (float)osu.Game.Overlays.OverlayColourScheme.Purple.GetHue(), 0f, 359f, 1f);
            SetDefault(OsuSetting.CustomUIAccentUnlocked, false);
            SetDefault(OsuSetting.MenuCursorStyle, osu.Game.Graphics.Cursor.MenuCursorStyle.LazerDefault);
            SetDefault(OsuSetting.UseGameplayCursorInMenus, false);
            SetDefault(OsuSetting.AlphaPpDevModeEnabled, false);
            SetDefault(OsuSetting.ToriiConfirmDangerousButtonsOnLongAttempts, false);
            SetDefault(OsuSetting.ToriiSkipBreaksEnabled, true);
            SetDefault(OsuSetting.ToriiSkipBreaksSingleConfirmation, false);
            SetDefault(OsuSetting.ToriiSkipBreaksBriefingSeen, false);
            SetDefault(OsuSetting.EnableOboeAudio, true);
            // torii: el stable song select es OPT-IN (se ofrece via el promo "try this!" tras varias
            // visitas). arranca APAGADO. era true por error: caia en modo stable sin que el usuario lo
            // pidiera y ademas tapaba el promo (el guard cortaba + el contador de visitas nunca subia).
            SetDefault(OsuSetting.ToriiLegacyFooterUseSkin, false);
            // migracion one-shot: a quien lo heredo prendido (cuando el default era true) lo apagamos 1 vez.
            SetDefault(OsuSetting.ToriiStableOptInMigrated, false);
            SetDefault(OsuSetting.ToriiLegacySongSelectFooter, false);
            // Torii: stamps PRIVADOS de version de difficulty ("osu:20260621,mania:..."). viven aca (torii.ini)
            // y no en la realm compartida a proposito: el stamp de la realm queda para el cliente oficial, y
            // asi cambiar de cliente no dispara recalcs cruzados de star rating. Applied = ultima version
            // torii recalculada; SeenRealm = ultimo stamp de la realm que vimos (para detectar wipes ajenos).
            SetDefault(OsuSetting.ToriiAppliedDifficultyVersions, string.Empty);
            SetDefault(OsuSetting.ToriiSeenRealmDifficultyVersions, string.Empty);
            // Torii: results screen estilo stable (la vista de detalles pasa a ser el ranking
            // panel de stable, y se abre sola despues de jugar un mapa). OPT-IN igual que el
            // stable song select: arranca APAGADO asi el look default (glass) queda coherente.
            SetDefault(OsuSetting.ToriiStableResults, false);
            // Torii: la UI legacy usa la fuente moderna de lazer por default; este toggle la
            // pasa a "Aller" (la fuente real de osu!stable) para un look 1:1 stable.
            SetDefault(OsuSetting.ToriiLegacyFont, false);

            SetDefault(OsuSetting.ShowFirstRunSetup, true);
            // torii: el wizard propio de torii corre DESPUES del first-run (o despues del reinicio por
            // cambio de carpeta), asi que es una key aparte y no un paso mas del first-run de ppy.
            SetDefault(OsuSetting.ShowToriiWelcome, true);
            SetDefault(OsuSetting.ToriiWelcomeMigrated, false);
            SetDefault(OsuSetting.ShowMobileDisclaimer, RuntimeInfo.IsMobile);

            SetDefault(OsuSetting.ScreenshotFormat, ScreenshotFormat.Jpg);
            SetDefault(OsuSetting.ScreenshotCaptureMenuCursor, false);

            SetDefault(OsuSetting.Scaling, ScalingMode.Off);
            SetDefault(OsuSetting.SafeAreaConsiderations, true);
            // torii: rango ampliado a 0-100% (era 0.5-1) asi el layout-background se puede empujar hasta invisible.
            SetDefault(OsuSetting.ScalingBackgroundDim, 0.9f, 0f, 1f, 0.01f);

            SetDefault(OsuSetting.ScalingSizeX, 0.8f, 0.2f, 1f, 0.01f);
            SetDefault(OsuSetting.ScalingSizeY, 0.8f, 0.2f, 1f, 0.01f);

            SetDefault(OsuSetting.ScalingPositionX, 0.5f, 0f, 1f, 0.01f);
            SetDefault(OsuSetting.ScalingPositionY, 0.5f, 0f, 1f, 0.01f);

            if (RuntimeInfo.IsMobile)
                SetDefault(OsuSetting.UIScale, 1f, 0.8f, 1.1f, 0.01f);
            else
                SetDefault(OsuSetting.UIScale, 1f, 0.8f, 1.6f, 0.01f);

            SetDefault(OsuSetting.UIHoldActivationDelay, 200.0, 0.0, 500.0, 50.0);

            SetDefault(OsuSetting.IntroSequence, IntroSequence.Circles);

            SetDefault(OsuSetting.MenuBackgroundSource, BackgroundSource.Skin);
            SetDefault(OsuSetting.SeasonalBackgroundMode, SeasonalBackgroundMode.Sometimes);

            SetDefault(OsuSetting.DiscordRichPresence, DiscordRichPresenceMode.Full);

            SetDefault(OsuSetting.EditorDim, 0.25f, 0f, 1f, 0.25f);
            SetDefault(OsuSetting.EditorWaveformOpacity, 0.25f, 0f, 1f, 0.25f);
            SetDefault(OsuSetting.EditorShowHitMarkers, true);
            SetDefault(OsuSetting.EditorAutoSeekOnPlacement, true);
            SetDefault(OsuSetting.EditorLimitedDistanceSnap, false);
            SetDefault(OsuSetting.EditorShowSpeedChanges, false);
            SetDefault(OsuSetting.EditorScaleOrigin, EditorOrigin.GridCentre);
            SetDefault(OsuSetting.EditorRotationOrigin, EditorOrigin.GridCentre);
            SetDefault(OsuSetting.EditorAdjustExistingObjectsOnTimingChanges, true);

            SetDefault(OsuSetting.HideCountryFlags, false);

            SetDefault(OsuSetting.MultiplayerRoomFilter, RoomPermissionsFilter.All);
            SetDefault(OsuSetting.MultiplayerShowInProgressFilter, true);
            SetDefault(OsuSetting.MultiplayerShowFullFilter, false);

            SetDefault(OsuSetting.LastProcessedMetadataId, -1);

            SetDefault(OsuSetting.ComboColourNormalisationAmount, 0.2f, 0f, 1f, 0.01f);
            SetDefault(OsuSetting.UserOnlineStatus, UserStatus.Online);

            SetDefault(OsuSetting.EditorTimelineShowTimingChanges, true);
            SetDefault(OsuSetting.EditorTimelineShowBreaks, true);
            SetDefault(OsuSetting.EditorTimelineShowTicks, true);

            SetDefault(OsuSetting.EditorContractSidebars, false);

            SetDefault(OsuSetting.AlwaysShowHoldForMenuButton, false);
            SetDefault(OsuSetting.AlwaysRequireHoldingForPause, false);
            SetDefault(OsuSetting.EditorShowStoryboard, true);

            SetDefault(OsuSetting.EditorSubmissionNotifyOnDiscussionReplies, true);
            SetDefault(OsuSetting.EditorSubmissionLoadInBrowserAfterSubmission, true);

            SetDefault(OsuSetting.WasSupporter, false);

            // intentionally uses `DateTime?` and not `DateTimeOffset?` because the latter fails due to `DateTimeOffset` not implementing `IConvertible`
            SetDefault(OsuSetting.LastOnlineTagsPopulation, (DateTime?)null);

            SetDefault(OsuSetting.DashboardSortMode, UserSortCriteria.LastVisit);
            SetDefault(OsuSetting.DashboardDisplayStyle, OverlayPanelDisplayStyle.Card);
        }

        protected override bool CheckLookupContainsPrivateInformation(OsuSetting lookup)
        {
            switch (lookup)
            {
                case OsuSetting.Token:
                    return true;
            }

            return false;
        }

        public override TrackedSettings CreateTrackedSettings()
        {
            return new TrackedSettings
            {
                new TrackedSetting<bool>(OsuSetting.ShowFpsDisplay, state => new SettingDescription(
                    rawValue: state,
                    name: GlobalActionKeyBindingStrings.ToggleFPSCounter,
                    value: state ? CommonStrings.Enabled.ToLower() : CommonStrings.Disabled.ToLower(),
                    shortcut: LookupKeyBindings(GlobalAction.ToggleFPSDisplay))
                ),
                new TrackedSetting<bool>(OsuSetting.MouseDisableButtons, disabledState => new SettingDescription(
                    rawValue: !disabledState,
                    name: GlobalActionKeyBindingStrings.ToggleGameplayMouseButtons,
                    value: disabledState ? CommonStrings.Disabled.ToLower() : CommonStrings.Enabled.ToLower(),
                    shortcut: LookupKeyBindings(GlobalAction.ToggleGameplayMouseButtons))
                ),
                new TrackedSetting<bool>(OsuSetting.GameplayLeaderboard, state => new SettingDescription(
                    rawValue: state,
                    name: GlobalActionKeyBindingStrings.ToggleInGameLeaderboard,
                    value: state ? CommonStrings.Enabled.ToLower() : CommonStrings.Disabled.ToLower(),
                    shortcut: LookupKeyBindings(GlobalAction.ToggleInGameLeaderboard))
                ),
                new TrackedSetting<HUDVisibilityMode>(OsuSetting.HUDVisibilityMode, visibilityMode => new SettingDescription(
                    rawValue: visibilityMode,
                    name: GameplaySettingsStrings.HUDVisibilityMode,
                    value: visibilityMode.GetLocalisableDescription(),
                    shortcut: new TranslatableString(@"_", @"{0}: {1} {2}: {3}",
                        GlobalActionKeyBindingStrings.ToggleInGameInterface,
                        LookupKeyBindings(GlobalAction.ToggleInGameInterface),
                        GlobalActionKeyBindingStrings.HoldForHUD,
                        LookupKeyBindings(GlobalAction.HoldForHUD)))
                ),
                new TrackedSetting<ScalingMode>(OsuSetting.Scaling, scalingMode => new SettingDescription(
                        rawValue: scalingMode,
                        name: GraphicsSettingsStrings.ScreenScaling,
                        value: scalingMode.GetLocalisableDescription()
                    )
                ),
                new TrackedSetting<string>(OsuSetting.Skin, skin =>
                {
                    string skinName = string.Empty;

                    if (Guid.TryParse(skin, out var id))
                        skinName = LookupSkinName(id);

                    return new SettingDescription(
                        rawValue: skinName,
                        name: SkinSettingsStrings.SkinSectionHeader,
                        value: skinName,
                        shortcut: new TranslatableString(@"_", @"{0}: {1}",
                            GlobalActionKeyBindingStrings.RandomSkin,
                            LookupKeyBindings(GlobalAction.RandomSkin))
                    );
                }),
                new TrackedSetting<float>(OsuSetting.UIScale, scale => new SettingDescription(
                        rawValue: scale,
                        name: GraphicsSettingsStrings.UIScaling,
                        value: $"{scale:N2}x"
                        // TODO: implement lookup for framework platform key bindings
                    )
                ),
            };
        }

        public Func<Guid, string> LookupSkinName { private get; set; } = _ => @"unknown";
        public Func<GlobalAction, LocalisableString> LookupKeyBindings { private get; set; } = _ => @"unknown";

        IBindable<float> IGameplaySettings.ComboColourNormalisationAmount => GetOriginalBindable<float>(OsuSetting.ComboColourNormalisationAmount);
        IBindable<float> IGameplaySettings.PositionalHitsoundsLevel => GetOriginalBindable<float>(OsuSetting.PositionalHitsoundsLevel);
    }

    // IMPORTANT: These are used in user configuration files.
    // The naming of these keys should not be changed once they are deployed in a release, unless migration logic is also added.
    public enum OsuSetting
    {
        Ruleset,
        Token,
        MenuCursorSize,
        GameplayCursorSize,
        AutoCursorSize,
        GameplayCursorDuringTouch,
        DimLevel,
        BlurLevel,
        EditorDim,
        LightenDuringBreaks,
        ShowStoryboard,
        KeyOverlay,
        GameplayLeaderboard,
        PositionalHitsoundsLevel,
        AlwaysPlayFirstComboBreak,
        FloatingComments,
        HUDVisibilityMode,

        ShowHealthDisplayWhenCantFail,
        FadePlayfieldWhenHealthLow,

        /// <summary>
        /// Disables mouse buttons clicks during gameplay.
        /// </summary>
        MouseDisableButtons,
        MouseDisableWheel,
        ConfineMouseMode,

        /// <summary>
        /// Globally applied audio offset.
        /// This is added to the audio track's current time. Higher values will cause gameplay to occur earlier, relative to the audio track.
        /// </summary>
        AudioOffset,

        VolumeInactive,
        MenuMusic,
        MenuVoice,
        MenuTips,
        CursorRotation,
        MenuParallax,
        Prefer24HourTime,
        BeatmapDetailTab,
        BeatmapLeaderboardSortMode,
        BeatmapDetailModsFilter,
        Username,
        ReleaseStream,
        LastKnownBuildStream,
        SavePassword,
        SaveUsername,
        DisplayStarsMinimum,
        DisplayStarsMaximum,
        SongSelectGroupMode,
        SongSelectSortingMode,
        RandomSelectAlgorithm,
        ModSelectHotkeyStyle,
        ShowFpsDisplay,
        ToriiPerformanceCounter,
        ChatDisplayHeight,
        BeatmapListingCardSize,
        ToolbarClockDisplayMode,
        SongSelectBackgroundBlur,
        UnslantedSongSelectUI,
        Version,
        ShowFirstRunSetup,
        ShowToriiWelcome,
        ToriiWelcomeMigrated,
        ShowConvertedBeatmaps,
        Skin,
        CycleSkinsThroughFavoritesOnly,
        ScreenshotFormat,
        ScreenshotCaptureMenuCursor,
        BeatmapSkins,
        BeatmapColours,
        BeatmapHitsounds,
        IncreaseFirstObjectVisibility,
        ScoreDisplayMode,
        ExternalLinkWarning,
        PreferNoVideo,
        Scaling,
        ScalingPositionX,
        ScalingPositionY,
        ScalingSizeX,
        ScalingSizeY,
        ScalingBackgroundDim,
        UIScale,
        IntroSequence,
        NotifyOnUsernameMentioned,
        NotifyOnPrivateMessage,
        NotifyOnFriendPresenceChange,
        UIHoldActivationDelay,
        HitLighting,
        StarFountains,
        MenuBackgroundSource,
        GameplayDisableWinKey,
        SeasonalBackgroundMode,
        EditorWaveformOpacity,
        EditorShowHitMarkers,
        EditorAutoSeekOnPlacement,
        DiscordRichPresence,

        ShowOnlineExplicitContent,
        LastProcessedMetadataId,
        SafeAreaConsiderations,
        ComboColourNormalisationAmount,
        ProfileCoverExpanded,
        EditorLimitedDistanceSnap,
        ReplaySettingsOverlay,
        ReplayPlaybackControlsExpanded,
        AutomaticallyDownloadMissingBeatmaps,
        EditorShowSpeedChanges,
        TouchDisableGameplayTaps,
        ModSelectTextSearchStartsActive,

        /// <summary>
        /// The status for the current user to broadcast to other players.
        /// </summary>
        UserOnlineStatus,

        MultiplayerRoomFilter,
        HideCountryFlags,
        EditorTimelineShowTimingChanges,
        EditorTimelineShowTicks,
        AlwaysShowHoldForMenuButton,
        EditorContractSidebars,
        EditorScaleOrigin,
        EditorRotationOrigin,
        EditorTimelineShowBreaks,
        EditorAdjustExistingObjectsOnTimingChanges,
        AlwaysRequireHoldingForPause,
        MultiplayerShowInProgressFilter,
        MultiplayerShowFullFilter,
        BeatmapListingFeaturedArtistFilter,
        BeatmapListingToriiFilter,
        BeatmapListingAiFilter,
        ShowMobileDisclaimer,
        EditorShowStoryboard,
        EditorSubmissionNotifyOnDiscussionReplies,
        EditorSubmissionLoadInBrowserAfterSubmission,

        /// <summary>
        /// Cached state of whether local user is a supporter.
        /// Used to allow early checks (ie for startup samples) to be in the correct state, even if the API authentication process has not completed.
        /// </summary>
        WasSupporter,

        LastOnlineTagsPopulation,

        AutomaticallyAdjustBeatmapOffset,

        DashboardSortMode,
        DashboardDisplayStyle,

        // Torii
        ForceSDL3,
        ToriiServerPulseEnabled,
        UITheme,
        ToriiGlassDefaultMigrated,
        ToriiNewThemePopupPending,
        ToriiPotatoMode,
        ToriiInputAudioHz,
        ToriiInputAudioHzAutoTuned,
        ToriiInputAudioHzRetunedWeakQuad,
        ToriiNovaRendererMigrated,
        ToriiKeyDebounceEnabled,
        ToriiKeyDebounceThresholdMs,
        ToriiRenderResolution,
        ToriiRenderSkin,
        ToriiRenderMotionBlur,
        ToriiRenderShare,
        ToriiAutoHideToolbar,
        ToriiToolbarHintShown,
        ToriiStablePromoShown,
        ToriiSongSelectViews,
        ToriiToolbarToggleCount,

        // Torii cosmetics / economy / UI
        UserAuraEnabled,
        EquippedNameColour,
        EquippedCursorTrail,
        OwnedCursorTrails,
        CursorTrailCustomisations,
        CursorTrailAdjustUnlocked,
        CosmeticStoreDisabled,
        CosmeticStorePotatoMode,
        CosmeticsReducedMotion,
        CosmeticsHidden,
        ToriiPointsBalance,
        ToriiPointsFeedCursor,
        CustomUIHue,
        CustomUIHueEnabled,
        CustomUIHueApplyToMenu,
        CustomUIHueApplyToOverlays,
        CustomUIHueApplyToSettingsPanel,
        CustomUIAccentEnabled,
        CustomUIAccentHue,
        CustomUIAccentUnlocked,
        MenuCursorStyle,
        UseGameplayCursorInMenus,
        AlphaPpDevModeEnabled,
        ToriiConfirmDangerousButtonsOnLongAttempts,
        ToriiSkipBreaksEnabled,
        ToriiSkipBreaksSingleConfirmation,
        ToriiSkipBreaksBriefingSeen,
        EnableOboeAudio,
        ToriiLegacyFooterUseSkin,
        ToriiStableOptInMigrated,
        ToriiLegacySongSelectFooter,
        ToriiStableResults,
        ToriiLegacyFont,
        ToriiDifficultyRecalcMode,
        ToriiAppliedDifficultyVersions,
        ToriiSeenRealmDifficultyVersions,
    }
}
