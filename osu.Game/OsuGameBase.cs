// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Development;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input;
using osu.Framework.Input.Handlers;
using osu.Framework.Input.Handlers.Joystick;
using osu.Framework.Input.Handlers.Midi;
using osu.Framework.Input.Handlers.Mouse;
using osu.Framework.Input.Handlers.Pen;
using osu.Framework.Input.Handlers.Tablet;
using osu.Framework.Input.Handlers.Touch;
using osu.Framework.IO.Stores;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Timing;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Formats;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Graphics;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.UserInterface;
using osu.Game.Input;
using osu.Game.Input.Bindings;
using osu.Game.IO;
using osu.Game.Localisation;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.Chat;
using osu.Game.Online.Leaderboards;
using osu.Game.Online.Metadata;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Spectator;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osu.Game.Overlays.Settings.Sections;
using osu.Game.Overlays.Settings.Sections.Input;
using osu.Game.Resources;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;
using osu.Game.Skinning;
using osu.Game.Utils;
using RuntimeInfo = osu.Framework.RuntimeInfo;

namespace osu.Game
{
    /// <summary>
    /// The most basic <see cref="Game"/> that can be used to host osu! components and systems.
    /// Unlike <see cref="OsuGame"/>, this class will not load any kind of UI, allowing it to be used
    /// for provide dependencies to test cases without interfering with them.
    /// </summary>
    [Cached(typeof(OsuGameBase))]
    public partial class OsuGameBase : Framework.Game, ICanAcceptFiles, IBeatSyncProvider
    {
#if DEBUG
        public const string GAME_NAME = "Torii Nova (development)";
#else
        public const string GAME_NAME = "Torii Nova";
#endif

        public const string OSU_PROTOCOL = "osu://";

        /// <summary>
        /// The filename of the main client database.
        /// </summary>
        public const string CLIENT_DATABASE_FILENAME = @"client.realm";

        public const int SAMPLE_CONCURRENCY = 6;

        public const double SFX_STEREO_STRENGTH = 0.6;

        /// <summary>
        /// Length of debounce (in milliseconds) for commonly occuring sample playbacks that could stack.
        /// </summary>
        public const int SAMPLE_DEBOUNCE_TIME = 20;

        /// <summary>
        /// The maximum volume at which audio tracks should play back at. This can be set lower than 1 to create some head-room for sound effects.
        /// </summary>
        private const double global_track_volume_adjust = 0.8;

        public virtual bool UseDevelopmentServer => DebugUtils.IsDebugBuild;

        // toriirefresh: hard-locked to the Torii server. The official osu.ppy.sh
        // production / development endpoints are intentionally unreachable so the
        // client can only ever connect to Torii's API + hubs.
        public virtual EndpointConfiguration CreateEndpoints() => new ToriiEndpointConfiguration();

        protected override OnlineStore CreateOnlineStore() => new TrustedDomainOnlineStore();

        public virtual Version AssemblyVersion => Assembly.GetEntryAssembly()?.GetName().Version ?? new Version();

        /// <summary>
        /// MD5 representation of the game executable.
        /// </summary>
        public string VersionHash { get; private set; }

        public bool IsDeployedBuild => AssemblyVersion.Major > 0;

        public virtual string Version
        {
            get
            {
                if (!IsDeployedBuild)
                    return @"local " + (DebugUtils.IsDebugBuild ? @"debug" : @"release");

                string informationalVersion = Assembly.GetEntryAssembly()?
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion;

                // Example: [assembly: AssemblyInformationalVersion("2025.613.0-tachyon+d934e574b2539e8787956c3c9ecce9dadebb10ee")]
                if (!string.IsNullOrEmpty(informationalVersion))
                    return informationalVersion.Split('+').First();

                Version version = AssemblyVersion;
                return $@"{version.Major}.{version.Minor}.{version.Build}-lazer";
            }
        }

        /// <summary>
        /// The <see cref="Edges"/> that the game should be drawn over at a top level.
        /// Defaults to <see cref="Edges.None"/>.
        /// </summary>
        protected virtual Edges SafeAreaOverrideEdges => Edges.None;

        protected OsuConfigManager LocalConfig { get; private set; }

        // Torii: kept alive so the input/audio thread-rate binding isn't collected.
        private Bindable<ToriiInputAudioHzMode> toriiInputAudioHz;

        protected SessionStatics SessionStatics { get; private set; }

        protected OsuColour Colours { get; private set; }

        protected BeatmapManager BeatmapManager { get; private set; }

        protected BeatmapModelDownloader BeatmapDownloader { get; private set; }

        protected ScoreManager ScoreManager { get; private set; }

        protected ScoreModelDownloader ScoreDownloader { get; private set; }

        protected SkinManager SkinManager { get; private set; }

        protected RealmRulesetStore RulesetStore { get; private set; }

        protected RealmKeyBindingStore KeyBindingStore { get; private set; }

        protected GlobalCursorDisplay GlobalCursorDisplay { get; private set; }

        protected MusicController MusicController { get; private set; }

        protected IAPIProvider API { get; set; }

        protected Storage Storage { get; set; }

        /// <summary>
        /// The language in which the game is currently displayed in.
        /// </summary>
        public Bindable<Language> CurrentLanguage { get; } = new Bindable<Language>();

        protected Bindable<WorkingBeatmap> Beatmap { get; private set; } // cached via load() method

        /// <summary>
        /// The current ruleset selection for the local user.
        /// </summary>
        [Cached]
        [Cached(typeof(IBindable<RulesetInfo>))]
        protected internal readonly Bindable<RulesetInfo> Ruleset = new Bindable<RulesetInfo>();

        /// <summary>
        /// The current mod selection for the local user.
        /// </summary>
        /// <remarks>
        /// If a mod select overlay is present, mod instances set to this value are not guaranteed to remain as the provided instance and will be overwritten by a copy.
        /// In such a case, changes to settings of a mod will *not* propagate after a mod is added to this collection.
        /// As such, all settings should be finalised before adding a mod to this collection.
        /// </remarks>
        [Cached]
        [Cached(typeof(IBindable<IReadOnlyList<Mod>>))]
        protected readonly Bindable<IReadOnlyList<Mod>> SelectedMods = new Bindable<IReadOnlyList<Mod>>(Array.Empty<Mod>());

        /// <summary>
        /// Mods available for the current <see cref="Ruleset"/>.
        /// </summary>
        public readonly Bindable<Dictionary<ModType, IReadOnlyList<Mod>>> AvailableMods = new Bindable<Dictionary<ModType, IReadOnlyList<Mod>>>(new Dictionary<ModType, IReadOnlyList<Mod>>());

        private BeatmapDifficultyCache difficultyCache;
        private IBeatmapUpdater beatmapUpdater;

        private UserLookupCache userCache;
        private BeatmapLookupCache beatmapCache;
        protected LeaderboardManager LeaderboardManager { get; private set; }

        private RulesetConfigCache rulesetConfigCache;

        private SessionAverageHitErrorTracker hitErrorTracker;

        protected SpectatorClient SpectatorClient { get; private set; }

        protected MultiplayerClient MultiplayerClient { get; private set; }

        private MetadataClient metadataClient;

        private RealmAccess realm;

        protected SafeAreaContainer SafeAreaContainer { get; private set; }

        /// <summary>
        /// For now, this is used as a source specifically for beat synced components.
        /// Going forward, it could potentially be used as the single source-of-truth for beatmap timing.
        /// </summary>
        private readonly FramedBeatmapClock beatmapClock = new FramedBeatmapClock(applyOffsets: true, requireDecoupling: false);

        protected override Container<Drawable> Content => content;

        private Container content;

        private DependencyContainer dependencies;

        private readonly BindableNumber<double> globalTrackVolumeAdjust = new BindableNumber<double>(global_track_volume_adjust);

        // Torii: fuente de la UI legacy (Aller vs lazer). Campo persistente asi el weak-ref del bindable
        // no lo colecta el GC; empuja el valor al flag estatico LegacyFonts.UseAllerFont en cada cambio.
        private readonly Bindable<bool> legacyFontSetting = new BindableBool();

        private Bindable<string> frameworkLocale = null!;

        private IBindable<LocalisationParameters> localisationParameters = null!;

        /// <summary>
        /// Number of unhandled exceptions to allow before aborting execution.
        /// </summary>
        /// <remarks>
        /// When an unhandled exception is encountered, an internal count will be decremented.
        /// If the count hits zero, the game will crash.
        /// Each second, the count is incremented until reaching the value specified.
        /// </remarks>
        protected virtual int UnhandledExceptionsBeforeCrash => DebugUtils.IsDebugBuild ? 0 : 1;

        public OsuGameBase()
        {
            Name = GAME_NAME;

            allowableExceptions = UnhandledExceptionsBeforeCrash;
        }

        [BackgroundDependencyLoader]
        private void load(ReadableKeyCombinationProvider keyCombinationProvider, FrameworkConfigManager frameworkConfig)
        {
            try
            {
                using (var str = File.OpenRead(typeof(OsuGameBase).Assembly.Location))
                    VersionHash = str.ComputeMD5Hash();
            }
            catch
            {
                // special case for android builds, which can't read DLLs from a packed apk.
                // should eventually be handled in a better way.
                VersionHash = $"{Version}-{RuntimeInfo.OS}".ComputeMD5Hash();
            }

            Resources.AddStore(new DllResourceStore(OsuResources.ResourceAssembly));

            dependencies.Cache(realm = new RealmAccess(Storage, CLIENT_DATABASE_FILENAME, Host.UpdateThread));

            dependencies.CacheAs<RulesetStore>(RulesetStore = new RealmRulesetStore(realm, Storage));
            dependencies.CacheAs<IRulesetStore>(RulesetStore);

            Decoder.RegisterDependencies(RulesetStore);

            dependencies.CacheAs(Storage);

            // Torii: tracks which in-game "NEW" feature badges have been seen/dismissed
            // (sidecar JSON under the torii/ storage dir). Cached after Storage so its
            // constructor can resolve it. Consumed by NewFeatureBadge on form controls.
            dependencies.Cache(new osu.Game.Configuration.NewFeatureTracker(Storage));

            var largeStore = new LargeTextureStore(Host.Renderer, Host.CreateTextureLoaderStore(new NamespacedResourceStore<byte[]>(Resources, @"Textures")));
            largeStore.AddTextureSource(Host.CreateTextureLoaderStore(CreateOnlineStore()));
            dependencies.Cache(largeStore);

            dependencies.CacheAs(LocalConfig);
            dependencies.CacheAs<IGameplaySettings>(LocalConfig);

            InitialiseFonts();

            addFilesWarning();

            Audio.Samples.PlaybackConcurrency = SAMPLE_CONCURRENCY;

            dependencies.Cache(SkinManager = new SkinManager(Storage, realm, Host, Resources, Audio, Scheduler));
            dependencies.CacheAs<ISkinSource>(SkinManager);

            EndpointConfiguration endpoints = CreateEndpoints();

            MessageFormatter.WebsiteRootUrl = endpoints.WebsiteUrl;

            // Initialise localisation
            frameworkLocale = frameworkConfig.GetBindable<string>(FrameworkSetting.Locale);
            frameworkLocale.BindValueChanged(_ => updateLanguage());

            localisationParameters = Localisation.CurrentParameters.GetBoundCopy();
            localisationParameters.BindValueChanged(_ => updateLanguage(), true);

            CurrentLanguage.BindValueChanged(val => frameworkLocale.Value = val.NewValue.ToCultureCode());

            dependencies.CacheAs(API ??= new APIAccess(this, LocalConfig, endpoints, VersionHash));

            var defaultBeatmap = new DummyWorkingBeatmap(Audio, Textures);

            dependencies.Cache(difficultyCache = new BeatmapDifficultyCache());

            // ordering is important here to ensure foreign keys rules are not broken in ModelStore.Cleanup()
            dependencies.Cache(ScoreManager = new ScoreManager(RulesetStore, () => BeatmapManager, Storage, realm, API, LocalConfig));

            dependencies.Cache(BeatmapManager = new BeatmapManager(Storage, realm, API, Audio, Resources, Host, defaultBeatmap, difficultyCache, performOnlineLookups: true));
            dependencies.CacheAs<IWorkingBeatmapCache>(BeatmapManager);

            // Torii cosmetics / economy: app-wide owned/equipped/points state, shared by the
            // cursor containers, the username decorator (auras + name colours) and the store.
            var toriiCosmetics = new osu.Game.Cosmetics.ToriiCosmeticsManager(LocalConfig);
            dependencies.Cache(toriiCosmetics);

            // Static hooks the UserAuraContainer reads to decorate the local user's name everywhere.
            osu.Game.Graphics.UserEffects.UserAuraContainer.LocalUserProvider = () => API?.LocalUser.Value;
            osu.Game.Graphics.UserEffects.UserAuraContainer.LocalUserHasNameColour =
                () => !string.IsNullOrEmpty(toriiCosmetics.EquippedNameColourId.Value)
                      // Also true when the local user has a derivable role colour (admin,
                      // dev, ...) but nothing equipped, so their name still gets wrapped +
                      // painted on surfaces whose per-row user object is stripped of groups.
                      || osu.Game.Cosmetics.CosmeticNameColourCatalog.GetTopEarned(API?.LocalUser.Value) != null;
            osu.Game.Graphics.UserEffects.UserAuraContainer.ReducedMotion =
                () => LocalConfig.Get<bool>(OsuSetting.CosmeticsReducedMotion);
            osu.Game.Graphics.UserEffects.UserAuraContainer.CosmeticsSuppressed =
                () => LocalConfig.Get<bool>(OsuSetting.CosmeticsHidden) || Performance.PotatoMode.Active;

            // The flat colour of the local user's EQUIPPED name colour, so the
            // leaderboard base-colour helper (ToriiColourHelper.GetTopColour) can
            // paint their choice even on surfaces that only hold a stripped per-row
            // user object. Resolves buyable AND role colours from the FULL local
            // user; null when nothing is equipped (helper then falls back to role).
            osu.Game.Online.ToriiColourHelper.LocalEquippedNameColourProvider = () =>
            {
                string equippedId = toriiCosmetics.EquippedNameColourId.Value;
                var localUser = API?.LocalUser.Value;
                if (string.IsNullOrEmpty(equippedId) || localUser == null)
                    return null;

                var resolved = osu.Game.Cosmetics.CosmeticNameColourCatalog.GetById(equippedId, localUser);
                if (resolved == null)
                    return null;

                var c = resolved.Primary;
                return new Colour4(c.R, c.G, c.B, c.A);
            };

            // Persist the local user's equipped name colour server-side the moment
            // they change it, so every other client repaints their username to match
            // (mirrors the aura equip request). We send whatever they equipped —
            // a bought colour OR a role colour (name-group-*) — so their EXPLICIT
            // pick wins everywhere (leaderboards, others' clients) instead of every
            // client re-deriving their highest-priority group. "none"/empty sends
            // null to clear. Not run immediately: the server already holds whatever
            // they last equipped, so a bare relaunch never re-pushes.
            toriiCosmetics.EquippedNameColourId.BindValueChanged(e =>
            {
                var localUser = API?.LocalUser.Value;
                if (localUser == null || localUser.Id <= 1)
                    return;

                // Skip the server echo when the change is SyncOwned authoritatively
                // reconciling the equipped colour against the owned set (not a user
                // action — SuppressEquipSound is set around it). Otherwise a
                // transiently-incomplete owned list would clear the user's real
                // choice server-side, cross-client and non-self-healing.
                if (toriiCosmetics.SuppressEquipSound)
                    return;

                string payload = string.IsNullOrEmpty(e.NewValue) ? null : e.NewValue;
                API.Queue(new osu.Game.Online.API.Requests.UpdateEquippedNameColourRequest(payload));
            });

            dependencies.Cache(BeatmapDownloader = new BeatmapModelDownloader(BeatmapManager, API));
            dependencies.Cache(ScoreDownloader = new ScoreModelDownloader(ScoreManager, API));

            // Add after all the above cache operations as it depends on them.
            base.Content.Add(difficultyCache);

            // TODO: OsuGame or OsuGameBase?
            dependencies.CacheAs(beatmapUpdater = CreateBeatmapUpdater());
            dependencies.CacheAs(SpectatorClient = new OnlineSpectatorClient(endpoints));
            dependencies.CacheAs(MultiplayerClient = new OnlineMultiplayerClient(endpoints));
            dependencies.CacheAs(metadataClient = new OnlineMetadataClient(endpoints));

            // Torii: when the server broadcasts that the locally-signed-in user's
            // public payload changed (equipped cosmetics, group membership, …),
            // refresh LocalUser so badges / name colour / aura / accent gate
            // repaint live without a relogin. UserAuraContainer handles OTHER users.
            metadataClient.UserUpdated += updatedUserId =>
            {
                if (API.LocalUser.Value != null && API.LocalUser.Value.Id == updatedUserId)
                    API.RefreshLocalUser();
            };

            base.Content.Add(new BeatmapOnlineChangeIngest(beatmapUpdater, realm, metadataClient));

            BeatmapManager.ProcessBeatmap = (beatmapSet, scope) => beatmapUpdater.Process(beatmapSet, scope);

            dependencies.Cache(userCache = new UserLookupCache());
            base.Content.Add(userCache);

            dependencies.Cache(beatmapCache = new BeatmapLookupCache());
            base.Content.Add(beatmapCache);

            dependencies.CacheAs<IRulesetConfigCache>(rulesetConfigCache = new RulesetConfigCache(realm, RulesetStore));

            var powerStatus = CreateBatteryInfo();
            if (powerStatus != null)
                dependencies.CacheAs(powerStatus);

            dependencies.Cache(SessionStatics = new SessionStatics());
            dependencies.Cache(hitErrorTracker = new SessionAverageHitErrorTracker());

            // Torii: glass es el theme default nuevo. una sola vez, a quien venia en el Torii clasico (el
            // default viejo, ahora "Torii Legacy") lo pasamos a glass y marcamos el popup de aviso. el que
            // eligio otra cosa (midnight, grayscale) o ya estaba en glass queda intacto. corre ACA, antes
            // del bake de OsuColour, asi el theme nuevo aplica ya en este arranque sin pedir restart.
            if (!LocalConfig.Get<bool>(OsuSetting.ToriiGlassDefaultMigrated))
            {
                if (LocalConfig.Get<UIThemeOption>(OsuSetting.UITheme) == UIThemeOption.Torii)
                {
                    LocalConfig.SetValue(OsuSetting.UITheme, UIThemeOption.LiquidGlass);
                    LocalConfig.SetValue(OsuSetting.ToriiNewThemePopupPending, true);
                }

                LocalConfig.SetValue(OsuSetting.ToriiGlassDefaultMigrated, true);
                // flush ya: si el juego se cierra sin salir prolijo (crash/kill), el flag migrated tiene
                // que quedar igual asi la migracion + el popup no se repiten al proximo arranque.
                LocalConfig.Save();
            }

            // Torii: el stable-style song select es OPT-IN (se ofrece con el promo "try this!" tras varias
            // visitas). arrancaba prendido por un default erroneo, asi que a quien lo tiene heredado en true
            // lo apagamos UNA sola vez; el que lo quiera lo re-activa desde el promo o Settings > Torii. el
            // que ya lo tenia apagado no se toca. mismo patron one-shot que la migracion del theme de arriba.
            if (!LocalConfig.Get<bool>(OsuSetting.ToriiStableOptInMigrated))
            {
                if (LocalConfig.Get<bool>(OsuSetting.ToriiLegacyFooterUseSkin))
                    LocalConfig.SetValue(OsuSetting.ToriiLegacyFooterUseSkin, false);

                LocalConfig.SetValue(OsuSetting.ToriiStableOptInMigrated, true);
                LocalConfig.Save();
            }

            // Torii: pin the cosmetic UI theme BEFORE OsuColour is constructed (its palette is
            // captured into the instance at build time). The dropdown prompts a restart on change.
            OsuColour.SetThemeFromConfig(LocalConfig.Get<UIThemeOption>(OsuSetting.UITheme));

            // Torii: la UI legacy (song select estilo stable) usa la fuente moderna de lazer por default;
            // este setting la pasa a Aller (fuente de osu!stable). Bindeado (no read-once) asi re-entrar al
            // song select ya toma la fuente nueva sin reiniciar; LegacyFonts.Get lo lee en construccion.
            LocalConfig.BindWith(OsuSetting.ToriiLegacyFont, legacyFontSetting);
            legacyFontSetting.BindValueChanged(e => Screens.Select.LegacyFonts.UseAllerFont = e.NewValue, true);

            // Torii: Potato Mode read-once flag (heavy visuals check it at construction). Restart-gated.
            Performance.PotatoMode.SetFromConfig(LocalConfig.Get<bool>(OsuSetting.ToriiPotatoMode));
            // Potato forces the legacy (BASS) audio path; its larger buffer rides over GC hitches
            // that starve the tiny WASAPI buffer on weak machines (the map-switch stutter).
            if (Performance.PotatoMode.Active)
                frameworkConfig.SetValue(FrameworkSetting.AudioUseExperimentalWasapi, false);

            // Torii: input/audio/update thread rate is the single source of truth for the
            // host's framesync pipeline. Applied live (the framework re-evaluates on change).
            // torii: en el primerisimo arranque sembramos el default de hz segun la capacidad
            // aproximada de la maquina, asi una pc floja/vieja no abre a los 2000 competitivos y
            // tironea. una sola vez (guardado por ToriiInputAudioHzAutoTuned); despues gana siempre
            // la eleccion del usuario en el dropdown. va antes del GetBindable asi el bindable lee
            // el valor ya sembrado.
            if (!LocalConfig.Get<bool>(OsuSetting.ToriiInputAudioHzAutoTuned))
            {
                var tunedHz = ToriiInputAudioHzDefaults.ForThisMachine();
                Logger.Log($"Torii: first-launch input/audio Hz auto-tuned to {(int)tunedHz} ({Environment.ProcessorCount} cores).");
                LocalConfig.SetValue(OsuSetting.ToriiInputAudioHz, tunedHz);
                LocalConfig.SetValue(OsuSetting.ToriiInputAudioHzAutoTuned, true);
            }

            toriiInputAudioHz = LocalConfig.GetBindable<ToriiInputAudioHzMode>(OsuSetting.ToriiInputAudioHz);
            toriiInputAudioHz.BindValueChanged(e => Host.ToriiInputAudioHz.Value = (int)e.NewValue, true);

            dependencies.Cache(Colours = new OsuColour());

            RegisterImportHandler(BeatmapManager);
            RegisterImportHandler(ScoreManager);
            RegisterImportHandler(SkinManager);

            // drop track volume game-wide to leave some head-room for UI effects / samples.
            // this means that for the time being, gameplay sample playback is louder relative to the audio track, compared to stable.
            // we may want to revisit this if users notice or complain about the difference (consider this a bit of a trial).
            Audio.Tracks.AddAdjustment(AdjustableProperty.Volume, globalTrackVolumeAdjust);

            Beatmap = new NonNullableBindable<WorkingBeatmap>(defaultBeatmap);

            dependencies.CacheAs<IBindable<WorkingBeatmap>>(Beatmap);
            dependencies.CacheAs(Beatmap);

            dependencies.Cache(LeaderboardManager = new LeaderboardManager());
            base.Content.Add(LeaderboardManager);

            // add api components to hierarchy.
            if (API is APIAccess apiAccess)
                base.Content.Add(apiAccess);

            base.Content.Add(SpectatorClient);
            base.Content.Add(MultiplayerClient);
            base.Content.Add(metadataClient);

            base.Content.Add(rulesetConfigCache);

            PreviewTrackManager previewTrackManager;
            dependencies.Cache(previewTrackManager = new PreviewTrackManager(BeatmapManager.BeatmapTrackStore));
            base.Content.Add(previewTrackManager);

            base.Content.Add(MusicController = new MusicController());
            dependencies.CacheAs(MusicController);

            MusicController.TrackChanged += onTrackChanged;
            base.Content.Add(beatmapClock);

            GlobalActionContainer globalBindings;

            OsuMenuSamples menuSamples;
            dependencies.Cache(menuSamples = new OsuMenuSamples());
            base.Content.Add(menuSamples);

            base.Content.Add(SafeAreaContainer = new SafeAreaContainer
            {
                SafeAreaOverrideEdges = SafeAreaOverrideEdges,
                RelativeSizeAxes = Axes.Both,
                Child = CreateScalingContainer().WithChild(globalBindings = new GlobalActionContainer(this)
                {
                    Children = new Drawable[]
                    {
                        (GlobalCursorDisplay = new GlobalCursorDisplay
                        {
                            RelativeSizeAxes = Axes.Both
                        }).WithChild(content = new OsuTooltipContainer(GlobalCursorDisplay.MenuCursor)
                        {
                            RelativeSizeAxes = Axes.Both
                        }),
                    }
                })
            });

            base.Content.Add(new TouchInputInterceptor());
            base.Content.Add(hitErrorTracker);

            KeyBindingStore = new RealmKeyBindingStore(realm, keyCombinationProvider);
            KeyBindingStore.Register(globalBindings, RulesetStore.AvailableRulesets);
            dependencies.Cache(KeyBindingStore);

            dependencies.Cache(globalBindings);

            Ruleset.BindValueChanged(onRulesetChanged);
            Beatmap.BindValueChanged(onBeatmapChanged);

            // make config aware of how to lookup skins for on-screen display purposes.
            // if this becomes a more common thing, tracked settings should be reconsidered to allow local DI.
            LocalConfig.LookupSkinName = id => SkinManager.Query(s => s.ID == id)?.ToString() ?? "Unknown";
            LocalConfig.LookupKeyBindings = l => KeyBindingStore.GetBindingsStringFor(l);

            initOboeAudio();
        }

        // Torii: Android low-latency Oboe audio bridge. Both null on non-Android or when
        // EnableOboeAudio is OFF. oboeEnabledBindable is held in a strong field per
        // GetBindable's contract (a discarded intermediate would let the GC silence the
        // binding — symptom: "the Oboe toggle stops applying after a while").
        private osu.Framework.Audio.OboeBridgeManager? oboeBridgeManager;
        private osu.Framework.Audio.OboeAudioRedirector? oboeAudioRedirector;
        private Bindable<bool>? oboeEnabledBindable;

        /// <summary>
        /// Subclass hook for the device's native output sample rate (Android
        /// PROPERTY_OUTPUT_SAMPLE_RATE) — needed for Oboe's AAudio MMAP-exclusive output.
        /// Default 0 (let Oboe pick); overridden in OsuGameAndroid.
        /// </summary>
        protected virtual int GetAndroidNativeOutputSampleRate() => 0;

        /// <summary>
        /// Boot/tear down the Android Oboe low-latency audio path from OsuSetting.EnableOboeAudio.
        /// Routes BASS into a decode-only global mixer that Oboe pulls from at the AAudio output
        /// (~15–30 ms vs vanilla 60–200 ms), falls back to OpenSL ES, and silently no-ops if the
        /// bundled libosu_native.so can't load. Hot-swappable. No-op on Desktop / iOS.
        /// </summary>
        private void initOboeAudio()
        {
            if (RuntimeInfo.OS != RuntimeInfo.Platform.Android)
                return;

            if (LocalConfig == null)
                return;

            oboeEnabledBindable = LocalConfig.GetBindable<bool>(OsuSetting.EnableOboeAudio);
            oboeEnabledBindable.BindValueChanged(e =>
            {
                if (e.NewValue)
                    startOboeBridge();
                else
                    stopOboeBridge();
            }, true);
        }

        private void startOboeBridge()
        {
            if (oboeBridgeManager != null)
                return; // already running

            try
            {
                int sampleRate = GetAndroidNativeOutputSampleRate();

                oboeAudioRedirector = new osu.Framework.Audio.OboeAudioRedirector(Audio);
                oboeBridgeManager = new osu.Framework.Audio.OboeBridgeManager();
                oboeBridgeManager.StartOboeBridge(
                    oboeAudioRedirector.Provider,
                    sampleRate: sampleRate,
                    onStarted: actualSampleRate => oboeAudioRedirector.RefreshMixers(actualSampleRate));

                Logger.Log($"[Torii] Oboe low-latency audio bridge started (requested rate={sampleRate}).");
            }
            catch (Exception e)
            {
                Logger.Error(e, "[Torii] Oboe audio start failed; staying on vanilla BASS output.");
                oboeBridgeManager?.Dispose();
                oboeAudioRedirector?.Dispose();
                oboeBridgeManager = null;
                oboeAudioRedirector = null;
            }
        }

        private void stopOboeBridge()
        {
            if (oboeBridgeManager == null)
                return; // already stopped

            try { oboeBridgeManager.StopOboeBridge(); } catch { }
            try { oboeAudioRedirector?.Dispose(); } catch { }
            oboeBridgeManager = null;
            oboeAudioRedirector = null;

            Logger.Log("[Torii] Oboe low-latency audio bridge stopped (vanilla BASS output restored).");
        }

        private void updateLanguage() => CurrentLanguage.Value = LanguageExtensions.GetLanguageFor(frameworkLocale.Value, localisationParameters.Value);

        private void addFilesWarning()
        {
            const string filename = "IMPORTANT READ ME.txt";

            if (!Storage.Exists(filename))
            {
                using (var stream = Storage.CreateFileSafely(filename))
                using (var textWriter = new StreamWriter(stream))
                {
                    textWriter.WriteLine(@"This folder contains all your user files and configuration.");
                    textWriter.WriteLine(@"Please DO NOT make manual changes to this folder.");
                    textWriter.WriteLine();
                    textWriter.WriteLine(@"- If you want to back up your game files, please back up THE ENTIRETY OF THIS DIRECTORY.");
                    textWriter.WriteLine(@"- If you want to delete all of your game files, please delete THE ENTIRETY OF THIS DIRECTORY.");
                    textWriter.WriteLine();
                    textWriter.WriteLine(@"To be very clear, the ""files/"" directory inside this directory stores all the raw pieces of your beatmaps, skins, and replays.");
                    textWriter.WriteLine(@"Importantly, it is NOT the only directory you need a backup of to avoid losing data. If you copy only the ""files/"" directory, YOU WILL LOSE DATA.");
                    textWriter.WriteLine();
                    textWriter.WriteLine(@"For more information on how these files are organised,");
                    textWriter.WriteLine(@"see https://github.com/ppy/osu/wiki/User-file-storage");
                }
            }
        }

        private void onTrackChanged(WorkingBeatmap beatmap, TrackChangeDirection direction) => beatmapClock.ChangeSource(beatmap.Track);

        protected virtual void InitialiseFonts()
        {
            AddFont(Resources, @"Fonts/Torus/Torus-Regular");
            AddFont(Resources, @"Fonts/Torus/Torus-Light");
            AddFont(Resources, @"Fonts/Torus/Torus-SemiBold");
            AddFont(Resources, @"Fonts/Torus/Torus-Bold");

            AddFont(Resources, @"Fonts/Torus-Alternate/Torus-Alternate-Regular");
            AddFont(Resources, @"Fonts/Torus-Alternate/Torus-Alternate-Light");
            AddFont(Resources, @"Fonts/Torus-Alternate/Torus-Alternate-SemiBold");
            AddFont(Resources, @"Fonts/Torus-Alternate/Torus-Alternate-Bold");

            AddFont(Resources, @"Fonts/Inter/Inter-Regular");
            AddFont(Resources, @"Fonts/Inter/Inter-RegularItalic");
            AddFont(Resources, @"Fonts/Inter/Inter-Light");
            AddFont(Resources, @"Fonts/Inter/Inter-LightItalic");
            AddFont(Resources, @"Fonts/Inter/Inter-SemiBold");
            AddFont(Resources, @"Fonts/Inter/Inter-SemiBoldItalic");
            AddFont(Resources, @"Fonts/Inter/Inter-Bold");
            AddFont(Resources, @"Fonts/Inter/Inter-BoldItalic");

            AddFont(Resources, @"Fonts/Noto/Noto-Basic");
            AddFont(Resources, @"Fonts/Noto/Noto-Bopomofo");
            AddFont(Resources, @"Fonts/Noto/Noto-CJK-Basic");
            AddFont(Resources, @"Fonts/Noto/Noto-CJK-Compatibility");
            AddFont(Resources, @"Fonts/Noto/Noto-Hangul");
            AddFont(Resources, @"Fonts/Noto/Noto-Thai");

            AddFont(Resources, @"Fonts/Venera/Venera-Light");
            AddFont(Resources, @"Fonts/Venera/Venera-Bold");
            AddFont(Resources, @"Fonts/Venera/Venera-Black");

            // Torii: osu!stable's "Aller" UI font, for the legacy (stable-style) song-select UI.
            AddFont(Resources, @"Fonts/Aller/Aller-Regular");
            AddFont(Resources, @"Fonts/Aller/Aller-Light");
            AddFont(Resources, @"Fonts/Aller/Aller-Bold");

            Fonts.AddStore(new OsuIcon.OsuIconStore(Textures));
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            var localeMappings = Enum.GetValues<Language>().Select(language =>
            {
#if DEBUG
                if (language == Language.debug)
                    return new LocaleMapping("debug", new DebugLocalisationStore());
#endif

                string cultureCode = language.ToCultureCode();

                try
                {
                    return new LocaleMapping(new ResourceManagerLocalisationStore(cultureCode));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Could not load localisations for language \"{cultureCode}\"");
                    return null;
                }
            }).Where(m => m != null);

            Localisation.AddLocaleMappings(localeMappings);
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent) =>
            dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

        public override void SetHost(GameHost host)
        {
            base.SetHost(host);

            // may be non-null for certain tests
            Storage ??= host.Storage;

            LocalConfig ??= UseDevelopmentServer
                ? new DevelopmentOsuConfigManager(Storage)
                : new OsuConfigManager(Storage);

            host.ExceptionThrown += onExceptionThrown;
        }

        #region Exit handling

        /// <summary>
        /// Use to programatically exit the game as if the user was triggering via alt-f4.
        /// By default, will keep persisting until an exit occurs (exit may be blocked multiple times).
        /// May be interrupted (see <see cref="OsuGame"/>'s override).
        /// </summary>
        public virtual void AttemptExit()
        {
            if (!OnExiting())
                Exit();
            else
                Scheduler.AddDelayed(AttemptExit, 2000);
        }

        /// <summary>
        /// An action that restarts the application after it has exited.
        /// </summary>
        [CanBeNull]
        public Action RestartOnExitAction { private get; set; }

        /// <summary>
        /// Signals that the application should not be restarted after it is exited.
        /// </summary>
        public void CancelRestartOnExit()
        {
            RestartOnExitAction = null;
        }

        /// <summary>
        /// If supported by the platform, the game will automatically restart after the next exit.
        /// </summary>
        /// <returns>Whether a restart operation was queued.</returns>
        public virtual bool RestartAppWhenExited() => false;

        #endregion

        /// <summary>
        /// Perform migration of user data to a specified path.
        /// </summary>
        /// <param name="path">The path to migrate to.</param>
        /// <returns>Whether migration succeeded to completion. If <c>false</c>, some files were left behind.</returns>
        /// <exception cref="TimeoutException"></exception>
        public bool MigrateUserData(string path)
        {
            Logger.Log($@"Migrating osu! data from ""{Storage.GetFullPath(string.Empty)}"" to ""{path}""...");

            IDisposable realmBlocker = null;

            try
            {
                ManualResetEventSlim readyToRun = new ManualResetEventSlim();

                bool success = false;

                Scheduler.Add(() =>
                {
                    try
                    {
                        realmBlocker = realm.BlockAllOperations("migration");
                        success = true;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Attempting to block all operations failed: {ex}", LoggingTarget.Database);
                    }

                    readyToRun.Set();
                }, false);

                if (!readyToRun.Wait(30000) || !success)
                    throw new TimeoutException("Attempting to block for migration took too long.");

                bool? cleanupSucceeded = (Storage as OsuStorage)?.Migrate(Host.GetStorage(path));

                Logger.Log(@"Migration complete!");
                return cleanupSucceeded != false;
            }
            finally
            {
                realmBlocker?.Dispose();
            }
        }

        protected virtual IBeatmapUpdater CreateBeatmapUpdater() => new BeatmapUpdater(BeatmapManager, difficultyCache, API, Storage);

        protected override UserInputManager CreateUserInputManager() => new OsuUserInputManager();

        protected virtual BatteryInfo CreateBatteryInfo() => null;

        protected virtual Container CreateScalingContainer() => new DrawSizePreservingFillContainer();

        protected override Storage CreateStorage(GameHost host, Storage defaultStorage) => new OsuStorage(host, defaultStorage);

        /// <summary>
        /// Creates an input settings subsection for an <see cref="InputHandler"/>.
        /// </summary>
        /// <remarks>Should be overriden per-platform to provide settings for platform-specific handlers.</remarks>
        public virtual SettingsSubsection CreateSettingsSubsectionFor(InputHandler handler)
        {
            // One would think that this could be moved to the `OsuGameDesktop` class, but doing so means that
            // OsuGameTestScenes will not show any input options (as they are based on OsuGame not OsuGameDesktop).
            //
            // This in turn makes it hard for ruleset creators to adjust input settings while testing their ruleset
            // within the test browser interface.
            if (RuntimeInfo.IsDesktop)
            {
                switch (handler)
                {
                    case ITabletHandler th:
                        return new TabletSettings(th);
                }
            }

            switch (handler)
            {
                case MouseHandler mh:
                    return new MouseSettings(mh);

                case JoystickHandler jh:
                    return new JoystickSettings(jh);

                case TouchHandler th:
                    return new TouchSettings(th);

                case PenHandler ph:
                    return new PenSettings(ph);

                case MidiHandler:
                    return new InputSubsection(handler);

                // return null for handlers that shouldn't have settings.
                default:
                    return null;
            }
        }

        private void onBeatmapChanged(ValueChangedEvent<WorkingBeatmap> beatmap)
        {
            if (IsLoaded && !ThreadSafety.IsUpdateThread)
                throw new InvalidOperationException("Global beatmap bindable must be changed from update thread.");

            Logger.Log($"Game-wide working beatmap updated to {beatmap.NewValue}");
        }

        private void onRulesetChanged(ValueChangedEvent<RulesetInfo> r)
        {
            if (IsLoaded && !ThreadSafety.IsUpdateThread)
                throw new InvalidOperationException("Global ruleset bindable must be changed from update thread.");

            Ruleset instance = null;

            if (r.NewValue?.Available == true)
            {
                try
                {
                    instance = r.NewValue.CreateInstance();
                }
                catch (Exception e)
                {
                    Rulesets.RulesetStore.LogRulesetFailure(r.NewValue, e);
                }
            }

            if (instance == null)
            {
                // reject the change if the ruleset is not available.
                revertRulesetChange();
                return;
            }

            var dict = new Dictionary<ModType, IReadOnlyList<Mod>>();

            try
            {
                foreach (ModType type in Enum.GetValues<ModType>())
                {
                    dict[type] = instance.GetModsFor(type)
                                         // Rulesets should never return null mods, but let's be defensive just in case.
                                         // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                                         .Where(mod => mod != null)
                                         .ToList();
                }
            }
            catch (Exception e)
            {
                Rulesets.RulesetStore.LogRulesetFailure(r.NewValue, e);
                revertRulesetChange();
                return;
            }

            AvailableMods.Value = dict;

            if (SelectedMods.Disabled)
                return;

            var convertedMods = SelectedMods.Value.Select(mod =>
            {
                var newMod = instance.CreateModFromAcronym(mod.Acronym);
                newMod?.CopyCommonSettingsFrom(mod);
                return newMod;
            }).Where(newMod => newMod != null).ToList();

            if (!ModUtils.CheckValidForGameplay(convertedMods, out var invalid))
                invalid.ForEach(newMod => convertedMods.Remove(newMod));

            SelectedMods.Value = convertedMods;

            void revertRulesetChange() => Ruleset.Value = r.OldValue?.Available == true ? r.OldValue : RulesetStore.AvailableRulesets.First();
        }

        private int allowableExceptions;

        /// <summary>
        /// Allows a maximum of one unhandled exception, per second of execution.
        /// </summary>
        /// <returns>Whether to ignore the exception and continue running.</returns>
        private bool onExceptionThrown(Exception ex)
        {
            if (Interlocked.Decrement(ref allowableExceptions) < 0)
            {
                Logger.Log("Too many unhandled exceptions, crashing out.");
                RulesetStore?.TryDisableCustomRulesetsCausing(ex);
                return false;
            }

            Logger.Log($"Unhandled exception has been allowed with {allowableExceptions} more allowable exceptions.");
            // restore the stock of allowable exceptions after a short delay.
            Task.Delay(1000).ContinueWith(_ => Interlocked.Increment(ref allowableExceptions));

            return true;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            // Torii: tear down the Oboe bridge before LocalConfig so any final lifecycle
            // touch happens while config is still alive.
            try { oboeEnabledBindable?.UnbindAll(); } catch { }
            stopOboeBridge();

            RulesetStore?.Dispose();
            LocalConfig?.Dispose();

            beatmapUpdater?.Dispose();

            realm?.Dispose();

            if (Host != null)
                Host.ExceptionThrown -= onExceptionThrown;

            RestartOnExitAction?.Invoke();
        }

        ControlPointInfo IBeatSyncProvider.ControlPoints => Beatmap.Value.BeatmapLoaded ? Beatmap.Value.Beatmap.ControlPointInfo : null;
        IClock IBeatSyncProvider.Clock => beatmapClock;
        ChannelAmplitudes IHasAmplitudes.CurrentAmplitudes => Beatmap.Value.TrackLoaded ? Beatmap.Value.Track.CurrentAmplitudes : ChannelAmplitudes.Empty;
    }
}
