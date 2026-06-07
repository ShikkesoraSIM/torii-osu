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
using osu.Game.Overlays;
using osu.Game.Overlays.Dashboard.Friends;
using osu.Game.Overlays.Mods.Input;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Edit.Compose.Components;
using osu.Game.Screens.OnlinePlay.Lounge.Components;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osu.Game.Screens.Select.Leaderboards;
using osu.Game.Skinning;
using osu.Game.Users;

namespace osu.Game.Configuration
{
    public class OsuConfigManager : IniConfigManager<OsuSetting>, IGameplaySettings
    {
        /// <summary>
        /// Torii writes its config + OAuth token to <c>torii.ini</c>
        /// instead of the upstream-default <c>game.ini</c>. When a user
        /// runs the official ppy lazer client against the same data
        /// folder, that client owns <c>game.ini</c> exclusively and the
        /// two no longer race to overwrite each other's keys. Shared
        /// content — beatmaps, skins, replays, the realm DB — stays
        /// shared because that's all in sibling files/directories that
        /// neither config file touches.
        /// </summary>
        public const string TORII_CONFIG_FILENAME = "torii.ini";

        /// <summary>
        /// The upstream-default config filename. We migrate from this
        /// once on first run after the cut-over (see
        /// <see cref="prepareMigratedStorage"/>) so users keep their
        /// session and settings without manual intervention.
        /// </summary>
        private const string upstream_config_filename = "game.ini";

        /// <inheritdoc/>
        protected override string Filename => TORII_CONFIG_FILENAME;

        public OsuConfigManager(Storage storage)
            : base(prepareMigratedStorage(storage))
        {
            Migrate();

            // NOTE: the legacy ToriiSettingsPersistence sidecar mechanism
            // is no longer wired up here. With torii.ini as the primary
            // config (Filename above), every Torii-only key already
            // round-trips through it via the standard IniConfigManager
            // path — running the sidecar in parallel would create a
            // dual-writer race on the same file. ToriiSettingsPersistence
            // is left in the tree as dead code marked [Obsolete] for one
            // release cycle so any out-of-tree call sites can migrate;
            // it'll be removed in a follow-up cleanup commit.
        }

        /// <summary>
        /// One-shot migration that ensures <see cref="TORII_CONFIG_FILENAME"/>
        /// exists with the user's pre-existing config + OAuth token
        /// before the base constructor reads from it. Idempotent; safe
        /// to call on every launch.
        /// </summary>
        /// <remarks>
        /// States this handles, in priority order:
        ///
        /// <list type="number">
        ///   <item><description>
        ///   <b>torii.ini already has a Token line</b>: this build (or a
        ///   previous Torii build with the cut-over applied) has
        ///   already written a full config to torii.ini. Nothing to do.
        ///   </description></item>
        ///   <item><description>
        ///   <b>torii.ini exists but has no Token</b>: a previous Torii
        ///   build wrote the curated sidecar subset (see
        ///   <see cref="ToriiSettingsPersistence"/>) here. Treat as
        ///   incomplete and seed from game.ini below; the full set of
        ///   keys including the OAuth token will overwrite the small
        ///   sidecar payload.
        ///   </description></item>
        ///   <item><description>
        ///   <b>game.ini exists</b>: the user has an osu! lazer install
        ///   (this Torii fork, or the upstream ppy client, doesn't
        ///   matter — same INI format). Bytes-for-bytes copy. The
        ///   user keeps their session and every setting.
        ///   </description></item>
        ///   <item><description>
        ///   <b>Fresh install, no files</b>: nothing to do — the base
        ///   IniConfigManager constructor will create torii.ini from
        ///   defaults on its first save.
        ///   </description></item>
        /// </list>
        ///
        /// Errors are swallowed (logged at Important level). A failed
        /// migration falls back to a fresh-defaults torii.ini which is
        /// the least-bad outcome — the user has to log in once but
        /// nothing else is lost; their game.ini is left untouched for
        /// the official client to keep using.
        /// </remarks>
        private static Storage prepareMigratedStorage(Storage storage)
        {
            try
            {
                bool toriiExists = storage.Exists(TORII_CONFIG_FILENAME);

                // State (1): already migrated.
                if (toriiExists && toriiIniContainsToken(storage))
                    return storage;

                // States (2)/(3): copy from game.ini if it exists.
                // CreateFileSafely overwrites — fine for state (2) because
                // the sidecar payload was a strict subset of what we're
                // about to write, and fine for state (3) because we want
                // exactly the same content.
                if (storage.Exists(upstream_config_filename))
                {
                    using (Stream src = storage.GetStream(upstream_config_filename, FileAccess.Read, FileMode.Open))
                    using (Stream dst = storage.CreateFileSafely(TORII_CONFIG_FILENAME))
                        src.CopyTo(dst);
                }
                // State (4): no files. Nothing to do — IniConfigManager
                // will create torii.ini from defaults on first save.
            }
            catch (Exception ex)
            {
                Logger.Log(
                    $"[torii.ini] migration from {upstream_config_filename} failed: {ex.Message}",
                    LoggingTarget.Runtime,
                    LogLevel.Important);
            }
            return storage;
        }

        /// <summary>
        /// Cheap signal that torii.ini holds a full primary-config
        /// payload rather than the curated sidecar payload an earlier
        /// Torii build may have written. The sidecar never wrote the
        /// OAuth Token (it's a sensitive value with no need to be
        /// mirrored across files), so the presence of any "Token"
        /// key proves this is a primary config and migration has
        /// already happened. False on read errors so the migration
        /// path runs again — at worst a redundant copy.
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
                        // The IniConfigManager format writes "Key = value"
                        // (with spaces around `=`); StartsWith on the
                        // trimmed line catches both that and the no-space
                        // sidecar format.
                        string trimmed = line.TrimStart();
                        if (trimmed.StartsWith("Token", StringComparison.Ordinal))
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
            SetDefault(OsuSetting.BeatmapLeaderboardSortMode, LeaderboardSortMode.Score);

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

            SetDefault(OsuSetting.ProfileCoverExpanded, true);

            SetDefault(OsuSetting.ToolbarClockDisplayMode, ToolbarClockDisplayMode.Full);
            SetDefault(OsuSetting.ToolbarLayoutMode, ToolbarLayoutMode.Expanded);
            SetDefault(OsuSetting.ToolbarDensityMode, ToolbarDensityMode.Auto);
            SetDefault(OsuSetting.AlphaToolbarEnabled, false);
            SetDefault(OsuSetting.AlphaToolbarUse, false);
            SetDefault(OsuSetting.AlphaPpDevModeEnabled, false);
            SetDefault(OsuSetting.AlphaStableSongSelectEnabled, false);

            SetDefault(OsuSetting.SongSelectBackgroundBlur, false);

            // Torii: when enabled, the slanted (sheared) UI elements in
            // Song Select v2 — wedges, the leaderboard rows, the metadata
            // panel, the dropdowns — render strictly vertical instead.
            // Off by default because the slanted style is part of the
            // lazer aesthetic; some users prefer a clean rectangular
            // layout. Live-toggleable: changes take effect on next
            // entry to song select (or any screen reload). See
            // OsuGame.SHEAR for how the value propagates.
            SetDefault(OsuSetting.UnslantedSongSelectUI, false);

            // Force SDL3 backend on every desktop OS, not just Windows.
            // Default false on Linux/macOS to match osu-framework's own
            // default — the framework gates SDL3 behind the OSU_SDL3 env
            // var on those platforms because of remaining cursor /
            // controller issues (see osu-framework FrameworkEnvironment.cs).
            // Windows + mobile are already SDL3 unconditionally upstream;
            // this toggle is a no-op there. Read by osu.Desktop/Program.cs
            // BEFORE GameHost is created (env var is the only viable hook
            // — UseSDL3 is a one-shot static readonly), so changes require
            // a process restart. The settings UI prompts for that.
            SetDefault(OsuSetting.ForceSDL3, false);

            // Torii: cosmetic chrome palette selection. Read once at
            // startup by OsuColour + OverlayColourProvider via
            // OsuColour.SetThemeFromConfig() before the DI container
            // resolves either type — changing this value requires a
            // process restart because the resolved palette is baked
            // into every drawable at construction. The settings UI
            // prompts for restart on change.
            SetDefault(OsuSetting.UITheme, UIThemeOption.Torii);

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

            SetDefault(OsuSetting.CustomApiUrl, "lazer-api.shikkesora.com");

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
            SetDefault(OsuSetting.NewAudioMigrationApplied, false);

            SetDefault(OsuSetting.AutomaticallyAdjustBeatmapOffset, false);

            // Input
            SetDefault(OsuSetting.MenuCursorSize, 1.0f, 0.5f, 2f, 0.01f);
            SetDefault(OsuSetting.GameplayCursorSize, 1.0f, 0.1f, 2f, 0.01f);
            SetDefault(OsuSetting.GameplayCursorDuringTouch, false);
            SetDefault(OsuSetting.AutoCursorSize, false);
            // Torii-only: deprecated. Kept for backwards compatibility
            // with sidecar files that may still reference it. Effective
            // setting now lives in MenuCursorStyle below.
            SetDefault(OsuSetting.UseGameplayCursorInMenus, false);

            // Torii-only: three-way selector for what cursor visual
            // MenuCursorContainer renders in menus / song-select /
            // overlays. Default is LazerDefault (preserves upstream
            // behaviour). Persists in torii.ini (the Torii primary
            // config; see TORII_CONFIG_FILENAME at the top of this
            // file) so the choice survives a roundtrip through the
            // official lazer client running against the same data
            // folder.
            SetDefault(OsuSetting.MenuCursorStyle, osu.Game.Graphics.Cursor.MenuCursorStyle.LazerDefault);

            SetDefault(OsuSetting.MouseDisableButtons, false);
            SetDefault(OsuSetting.MouseDisableWheel, false);
            SetDefault(OsuSetting.ConfineMouseMode, OsuConfineMouseMode.DuringGameplay);

            SetDefault(OsuSetting.TouchDisableGameplayTaps, false);

            // Graphics
            SetDefault(OsuSetting.ShowFpsDisplay, false);
            SetDefault(OsuSetting.AllowBenchmarkUnlimitedFrameLimiter, false);

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
            SetDefault(OsuSetting.KeepGameplayAfterFailed, false);
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
            // Default = Torii (stable). Users who had the legacy "Lazer" value
            // in torii.ini fall back here automatically — the enum was renamed
            // in May 2026 and `Enum.TryParse("Lazer", ...)` no longer matches,
            // so the bindable loader uses this default. Migration is silent.
            SetDefault(OsuSetting.ReleaseStream, ReleaseStream.Torii);

            SetDefault(OsuSetting.Version, string.Empty);

            SetDefault(OsuSetting.ShowFirstRunSetup, true);
            SetDefault(OsuSetting.ShowMobileDisclaimer, RuntimeInfo.IsMobile);

            SetDefault(OsuSetting.ScreenshotFormat, ScreenshotFormat.Jpg);
            SetDefault(OsuSetting.ScreenshotCaptureMenuCursor, false);

            SetDefault(OsuSetting.Scaling, ScalingMode.Off);
            SetDefault(OsuSetting.SafeAreaConsiderations, true);
            // Torii: lower bound dropped from 0.5f → 0f at a skinner request (Mash).
            // The original 50%-100% range stopped the layout background from going
            // fully opaque (= a flat single-color rectangle where the dimmed image
            // would be), but skins that want to ship a distinct `layout-background.png`
            // (see the SkinBackground / ScalingBackgroundScreen wiring) genuinely
            // want the option to push the original-menu-background image all the
            // way to invisible behind their custom layout image. Existing user
            // values are unchanged because 0.5..1.0 ⊆ 0.0..1.0.
            SetDefault(OsuSetting.ScalingBackgroundDim, 0.9f, 0f, 1f, 0.01f);

            SetDefault(OsuSetting.ScalingSizeX, 0.8f, 0.2f, 1f, 0.01f);
            SetDefault(OsuSetting.ScalingSizeY, 0.8f, 0.2f, 1f, 0.01f);

            SetDefault(OsuSetting.ScalingPositionX, 0.5f, 0f, 1f, 0.01f);
            SetDefault(OsuSetting.ScalingPositionY, 0.5f, 0f, 1f, 0.01f);

            if (RuntimeInfo.IsMobile)
                SetDefault(OsuSetting.UIScale, 1f, 0.8f, 1.1f, 0.01f);
            else
                SetDefault(OsuSetting.UIScale, 1f, 0.8f, 1.6f, 0.01f);

            SetDefault(OsuSetting.CustomUIHueEnabled, false);
            SetDefault(OsuSetting.CustomUIHue, OverlayColourScheme.Purple.GetHue(), 0, 359, 1);
            SetDefault(OsuSetting.CustomUIHueApplyToMenu, false);
            SetDefault(OsuSetting.CustomUIHueApplyToOverlays, true);
            SetDefault(OsuSetting.CustomUIHueApplyToSettingsPanel, true);

            // Accent hue is the donator-only "second hue" applied only to
            // saturated shades (Highlight/Colour/Light). Backgrounds and
            // text/foreground keep using the base hue. Defaults to the same
            // hue as the base so toggling it on doesn't visually jump.
            SetDefault(OsuSetting.CustomUIAccentEnabled, false);
            SetDefault(OsuSetting.CustomUIAccentHue, OverlayColourScheme.Purple.GetHue(), 0, 359, 1);

            // Defaults to OFF: this changes a long-standing one-click flow,
            // so users have to opt in. The threshold (60s of active gameplay)
            // and confirm window (5s) are intentionally NOT exposed as
            // settings to avoid bloat — they can be promoted to sliders if
            // anyone asks for tuning.
            SetDefault(OsuSetting.ToriiConfirmDangerousButtonsOnLongAttempts, false);

            // Torii skip-breaks — a SKIP button during mid-map break periods.
            // Default ON: it's purely additive (an optional button that seeks
            // past the empty break time, exactly like the intro skip) and was
            // explicitly requested by the community for maps with very long
            // breaks. Players who consider breaks part of the map can turn it
            // off in Settings → Torii → Gameplay.
            SetDefault(OsuSetting.ToriiSkipBreaksEnabled, true);

            // Double-press confirmation is the default for the break skip:
            // breaks are often a wanted breather, and a single misclick (a
            // stray pen tap landing on the button) pulling you out unprepared
            // is exactly the frustration we want to avoid. Players who'd
            // rather skip on one press flip this on (in settings, or right
            // from the one-time briefing popup).
            SetDefault(OsuSetting.ToriiSkipBreaksSingleConfirmation, false);

            // One-shot: becomes true after the first-time skip briefing has
            // been shown + dismissed. Never reset.
            SetDefault(OsuSetting.ToriiSkipBreaksBriefingSeen, false);

            // Torii server pulse — toolbar pill that shows live "currently
            // playing / plays per minute / top map" stats on hover. Default
            // ON because that's the whole point of shipping it; users who
            // find it noisy can toggle off in Settings → Torii → Interface
            // (and the provider stops polling immediately when the bindable
            // flips to false).
            SetDefault(OsuSetting.ToriiServerPulseEnabled, true);

            // Torii hiccup logger — captures frames slower than ~33 ms (sub-30
            // fps) into a JSONL file under <storage>/torii/hiccups/<timestamp>.jsonl
            // along with surrounding context (current screen, visible overlays,
            // API state, GC stats). Default OFF because it should leave runtime
            // identical to a Torii build without it. When OFF the logger
            // component is not even constructed — see OsuGame.cs subscription —
            // so there is zero per-frame cost. Devs and users sending lag
            // reports flip it ON, play, send the JSONL.
            SetDefault(OsuSetting.ToriiHiccupLoggerEnabled, false);

            // Sub-toggle, gated by ToriiHiccupLoggerEnabled. When ON, the
            // logger additionally batches captures and POSTs them to
            // POST /api/v2/torii/hiccup-reports every 30 s so devs can see
            // them on the admin dashboard. Default OFF — opt-in even if the
            // local logger is on, so users can capture privately without
            // sharing. Logger.cs reads this bindable on each batch flush.
            SetDefault(OsuSetting.ToriiHiccupShareEnabled, false);

            // Stable per-install identifier for hiccup uploads. Empty by
            // default; generated lazily on first upload as a SHA-256 of a
            // randomly-generated GUID. Once generated, it persists in osu.cfg
            // so the dashboard can correlate reports across game sessions
            // from the same install (without leaking machine identity — the
            // GUID is fresh, not derived from MAC / disk serial / etc.).
            SetDefault(OsuSetting.ToriiHiccupDeviceHash, string.Empty);

            // Android-only: route audio through Google's Oboe library instead
            // of letting BASS drive the AAudio/AudioTrack output directly.
            // When ON, OsuGameBase boots OboeBridgeManager + OboeAudioRedirector
            // so all BASS mixers run in decode-only mode and Oboe pulls PCM
            // from a single global mixer at the AAudio MMAP-exclusive output.
            // Cuts Android audio latency from ~60–200 ms to ~15–30 ms on most
            // devices that support MMAP, with OpenSL ES fallback otherwise.
            // Default ON because the patch ships the native lib in-tree and
            // the bridge silently falls back to vanilla BASS if anything in
            // the load path fails (Samsung security policies, missing AAudio
            // on very old devices, etc.) — the toggle is the user-facing
            // escape hatch when an individual device misbehaves.
            // Read once at OsuGameBase load on Android only; toggle changes
            // require an app restart to take effect (the bridge can't be
            // hot-swapped while audio is playing).
            SetDefault(OsuSetting.EnableOboeAudio, true);

            SetDefault(OsuSetting.UIHoldActivationDelay, 200.0, 0.0, 500.0, 50.0);

            SetDefault(OsuSetting.IntroSequence, IntroSequence.Triangles);

            SetDefault(OsuSetting.MenuBackgroundSource, BackgroundSource.Skin);
            SetDefault(OsuSetting.SeasonalBackgroundMode, SeasonalBackgroundMode.Sometimes);

            SetDefault(OsuSetting.DiscordRichPresence, DiscordRichPresenceMode.Full);

            SetDefault(OsuSetting.EditorDim, 0.25f, 0f, 0.75f, 0.25f);
            SetDefault(OsuSetting.EditorWaveformOpacity, 0.25f, 0f, 1f, 0.25f);
            SetDefault(OsuSetting.EditorShowHitMarkers, true);
            SetDefault(OsuSetting.EditorAutoSeekOnPlacement, true);
            SetDefault(OsuSetting.EditorLimitedDistanceSnap, false);
            SetDefault(OsuSetting.EditorShowSpeedChanges, false);
            SetDefault(OsuSetting.EditorScaleOrigin, EditorOrigin.GridCentre);
            SetDefault(OsuSetting.EditorRotationOrigin, EditorOrigin.GridCentre);
            SetDefault(OsuSetting.EditorAdjustExistingObjectsOnTimingChanges, true);

            SetDefault(OsuSetting.HideCountryFlags, false);

            // Torii: global toggle for the per-user aura particle effect
            // (rendered behind elite-group usernames everywhere their name shows).
            // Defaults on; users on weaker hardware can disable it from Graphics.
            SetDefault(OsuSetting.UserAuraEnabled, true);

            // Torii cosmetics (cursor-trail store). Owned/equipped/customisation
            // cached client-side; ToriiPointsBalance here is a LOCAL cache (the
            // authoritative balance lives server-side in g0v0).
            SetDefault(OsuSetting.EquippedCursorTrail, string.Empty);
            SetDefault(OsuSetting.OwnedCursorTrails, string.Empty);
            SetDefault(OsuSetting.CursorTrailAdjustUnlocked, false);
            SetDefault(OsuSetting.CursorTrailCustomisations, string.Empty);
            SetDefault(OsuSetting.ToriiPointsBalance, 90000);
            SetDefault(OsuSetting.ToriiPointsSeeded, false);
            SetDefault(OsuSetting.CosmeticStorePotatoMode, false);
            SetDefault(OsuSetting.EquippedNameColour, string.Empty);

            SetDefault(OsuSetting.MultiplayerRoomFilter, RoomPermissionsFilter.All);
            SetDefault(OsuSetting.MultiplayerShowInProgressFilter, true);

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

            // GU specific settings
            SetDefault(OsuSetting.DisableAutomaticUpdates, false);

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

        public void Migrate()
        {
            string? configuredApiUrl = Get<string>(OsuSetting.CustomApiUrl)?.Trim().TrimEnd('/');

            if (!string.IsNullOrEmpty(configuredApiUrl))
            {
                if (configuredApiUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                    configuredApiUrl = configuredApiUrl.Substring("http://".Length).TrimEnd('/');
                else if (configuredApiUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    configuredApiUrl = configuredApiUrl.Substring("https://".Length).TrimEnd('/');

                if (string.Equals(configuredApiUrl, "gamerherz.ddns.net", StringComparison.OrdinalIgnoreCase))
                    SetValue(OsuSetting.CustomApiUrl, "lazer-api.shikkesora.com");
            }

            // arrives as 2020.123.0-lazer
            string rawVersion = Get<string>(OsuSetting.Version);

            if (rawVersion.Length < 6)
                return;

            string[] pieces = rawVersion.Split('.');

            // on a fresh install or when coming from a non-release build, execution will end here.
            // we don't want to run migrations in such cases.
            if (!int.TryParse(pieces[0], out int year)) return;
            if (!int.TryParse(pieces[1], out int monthDay)) return;

            int combined = year * 10000 + monthDay;

            if (combined < 20250214)
            {
                // UI scaling on mobile platforms has been internally adjusted such that 1x UI scale looks correctly zoomed in than before.
                if (RuntimeInfo.IsMobile)
                    GetBindable<float>(OsuSetting.UIScale).SetDefault();
            }
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
        KeepGameplayAfterFailed,

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

        /// <summary>
        /// One-shot flag: whether the migration to the experimental (WASAPI) audio default has run.
        /// </summary>
        NewAudioMigrationApplied,

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
        SavePassword,
        SaveUsername,
        DisplayStarsMinimum,
        DisplayStarsMaximum,
        SongSelectGroupMode,
        SongSelectSortingMode,
        RandomSelectAlgorithm,
        ModSelectHotkeyStyle,
        ShowFpsDisplay,
        ChatDisplayHeight,
        BeatmapListingCardSize,
        ToolbarClockDisplayMode,
        ToolbarLayoutMode,
        ToolbarDensityMode,
        AlphaToolbarEnabled,
        AlphaToolbarUse,
        AlphaPpDevModeEnabled,
        AlphaStableSongSelectEnabled,
        SongSelectBackgroundBlur,

        /// <summary>
        /// When true, slanted (sheared) UI elements in Song Select v2
        /// render strictly vertical. The shear factor read by every
        /// component is sourced from <see cref="OsuGame.SHEAR"/>, which
        /// honours this setting; toggling at runtime takes effect on
        /// the next screen entry.
        /// </summary>
        UnslantedSongSelectUI,

        /// <summary>
        /// When true on a desktop OS that defaults to SDL2 (Linux, macOS),
        /// the desktop entry point sets the OSU_SDL3 environment variable
        /// before constructing the GameHost so osu-framework picks the
        /// SDL3 windowing backend. Windows + mobile platforms ignore this
        /// (already SDL3 unconditionally). Requires a process restart to
        /// take effect — the settings UI prompts for that and uses the
        /// same Velopack-mediated restart path the renderer setting uses.
        /// </summary>
        ForceSDL3,

        /// <summary>
        /// Torii: which cosmetic UI palette the chrome should use.
        /// See <see cref="UIThemeOption"/> for the catalogue + intent.
        /// Read once at startup by <see cref="OsuColour"/> +
        /// <see cref="OverlayColourProvider"/> — changing the value
        /// requires a process restart, prompted by the dropdown UI.
        /// </summary>
        UITheme,

        Version,
        ShowFirstRunSetup,
        ShowConvertedBeatmaps,
        Skin,
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
        CustomUIHueEnabled,
        CustomUIHue,
        CustomUIHueApplyToMenu,
        CustomUIHueApplyToOverlays,
        CustomUIHueApplyToSettingsPanel,
        CustomUIAccentEnabled,
        CustomUIAccentHue,
        // Torii: when enabled, Retry/Quit on the pause + fail overlays require
        // a second click to confirm if the current attempt has been running
        // long enough for the user to plausibly care about losing it. See
        // GameplayMenuOverlay for the threshold + arming behaviour.
        ToriiConfirmDangerousButtonsOnLongAttempts,

        /// <summary>
        /// Torii: when enabled, a SKIP button appears during break periods
        /// mid-map, letting the player fast-forward to the end of the break
        /// (seeks the gameplay clock, same mechanism as the intro skip).
        /// Default ON — it's a community-requested QoL feature and only adds
        /// an opt-in button; you still have to press skip for anything to
        /// happen. See <see cref="osu.Game.Screens.Play.SkipBreakOverlay"/>.
        /// </summary>
        ToriiSkipBreaksEnabled,

        /// <summary>
        /// Torii: when enabled, the mid-map break skip activates on a SINGLE
        /// press. When disabled (default), it requires a quick double-press
        /// confirmation so a stray pen/tap on the skip button can't yank you
        /// out of a break you wanted. See
        /// <see cref="osu.Game.Screens.Play.SkipBreakOverlay"/>.
        /// </summary>
        ToriiSkipBreaksSingleConfirmation,

        /// <summary>
        /// Torii: internal one-shot flag — set true the first time the player
        /// presses the mid-map skip button, after the explanatory briefing has
        /// been shown. Not a user-facing setting; gates the one-time popup so
        /// it never appears again. See
        /// <see cref="osu.Game.Screens.Play.SkipBreakOverlay"/>.
        /// </summary>
        ToriiSkipBreaksBriefingSeen,
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
        BeatmapListingFeaturedArtistFilter,
        ShowMobileDisclaimer,
        EditorShowStoryboard,
        EditorSubmissionNotifyOnDiscussionReplies,
        EditorSubmissionLoadInBrowserAfterSubmission,

        /// <summary>
        /// Custom API endpoint URL.
        /// </summary>
        CustomApiUrl,

        /// <summary>
        /// Cached state of whether local user is a supporter.
        /// Used to allow early checks (ie for startup samples) to be in the correct state, even if the API authentication process has not completed.
        /// </summary>
        WasSupporter,

        LastOnlineTagsPopulation,

        AutomaticallyAdjustBeatmapOffset,

        /// <summary>
        /// Disables automatic updates for the Torii version.
        /// </summary>
        DisableAutomaticUpdates,
        AllowBenchmarkUnlimitedFrameLimiter,
        DashboardSortMode,
        DashboardDisplayStyle,

        /// <summary>
        /// Toggle for the per-user aura particle effect rendered behind
        /// elite-group usernames (admin embers, goof leaves, etc.) anywhere
        /// the username appears (chat, profile, leaderboards, multi).
        /// </summary>
        UserAuraEnabled,

        // Torii cosmetics (cursor-trail store).
        EquippedCursorTrail,
        OwnedCursorTrails,
        CursorTrailAdjustUnlocked,
        CursorTrailCustomisations,
        ToriiPointsBalance,
        ToriiPointsSeeded,

        /// <summary>Store "Potato PC" mode: previews render a frozen snapshot
        /// instead of animating live, for weak hardware.</summary>
        CosmeticStorePotatoMode,

        /// <summary>Equipped username-colour cosmetic id ("" = default white).</summary>
        EquippedNameColour,

        /// <summary>
        /// Torii: when on, MenuCursorContainer renders the user's
        /// skin gameplay cursor (cursor.png + cursormiddle.png) as
        /// the menu cursor instead of lazer's default Cursor/menu-cursor
        /// texture. Sized by GameplayCursorSize, exact same composition
        /// as the actual gameplay cursor — what you see in the
        /// playfield is what you see in menus.
        /// </summary>
        /// <remarks>
        /// DEPRECATED. Superseded by <see cref="MenuCursorStyle"/>
        /// (an enum) which allows distinguishing between the user's
        /// skin gameplay cursor and the Torii stylised cursor — the
        /// older bool conflated those two cases. Kept in the enum at
        /// its original position to avoid renumbering subsequent
        /// values (which would break user configs that store
        /// settings by integer enum value).
        /// </remarks>
        UseGameplayCursorInMenus,

        /// <summary>
        /// Three-way menu cursor style selector. See
        /// <see cref="osu.Game.Graphics.Cursor.MenuCursorStyle"/>.
        /// </summary>
        MenuCursorStyle,

        /// <summary>
        /// Torii: toolbar server-pulse widget. When on, the toolbar shows
        /// a small heartbeat pill with currently-playing count and a
        /// hover-popover with sparkline + top map. See
        /// <see cref="osu.Game.Online.Server.ToriiServerPulseProvider"/>
        /// for the polling provider and
        /// <c>ToriiServerPulseButton</c> / <c>ToriiServerPulsePopover</c>
        /// for the UI surfaces.
        /// </summary>
        ToriiServerPulseEnabled,

        /// <summary>
        /// Torii: hiccup logger toggle. When OFF (default), absolutely nothing
        /// from the logger runs — it isn't even constructed. When ON, a single
        /// component is added to the game host that records frames longer than
        /// the configured threshold to JSONL with surrounding context.
        /// See <see cref="osu.Game.Performance.ToriiHiccupLogger"/>.
        /// </summary>
        ToriiHiccupLoggerEnabled,

        /// <summary>
        /// Torii: opt-in to upload captured hiccup records to the Torii
        /// admin dashboard at <c>/api/v2/torii/hiccup-reports</c>. Sub-toggle
        /// gated by <see cref="ToriiHiccupLoggerEnabled"/>. When OFF (default)
        /// captures stay local-only.
        /// </summary>
        ToriiHiccupShareEnabled,

        /// <summary>
        /// Torii: stable per-install device identifier for hiccup uploads
        /// (SHA-256 of a randomly-generated GUID, lazy-populated on first
        /// upload). Lets the admin dashboard group reports from the same
        /// install across user logouts without leaking machine identity.
        /// </summary>
        ToriiHiccupDeviceHash,

        CycleSkinsThroughFavoritesOnly,

        /// <summary>
        /// Torii: Android-only low-latency audio path via Google's Oboe library.
        /// When ON (default), <c>OsuGameBase</c> boots <c>OboeBridgeManager</c> +
        /// <c>OboeAudioRedirector</c> so BASS runs decode-only and Oboe pulls PCM
        /// straight to the AAudio MMAP-exclusive output. Cuts Android audio
        /// latency from ~60–200 ms down to ~15–30 ms on devices with MMAP
        /// support; transparent OpenSL ES fallback otherwise.
        ///
        /// The bridge silently no-ops on Desktop / iOS — the setting itself
        /// only fires meaningful work when <c>RuntimeInfo.OS ==
        /// RuntimeInfo.Platform.Android</c>. The toggle exists primarily as
        /// a user-facing escape hatch when an individual Android device
        /// misbehaves (Samsung security policies blocking <c>dlopen</c> of
        /// the bundled <c>libosu_native.so</c>, very old devices without
        /// AAudio, etc.). Toggle changes require an app restart.
        /// </summary>
        EnableOboeAudio,
    }
}
