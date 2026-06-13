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

        public virtual EndpointConfiguration CreateEndpoints()
        {
            EndpointConfiguration config = UseDevelopmentServer ? new DevelopmentEndpointConfiguration() : new ProductionEndpointConfiguration();

            string customUrl = LocalConfig?.Get<string>(OsuSetting.CustomApiUrl);

            if (!string.IsNullOrEmpty(customUrl))
            {
                customUrl = customUrl.TrimEnd('/');

                // Ensure the custom URL has a protocol scheme
                if (!customUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !customUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    customUrl = "https://" + customUrl;
                }

                config.APIUrl = customUrl;
                // Derive a separate website URL from the API URL, instead
                // of using the API URL for both. The API subdomain (e.g.
                // ``lazer-api.shikkesora.com``) serves only JSON; the
                // website at the matching ``lazer.shikkesora.com`` is
                // what should resolve URLs like ``/b/<id>``,
                // ``/users/<id>``, ``/wiki/...``. This matters for two
                // surfaces:
                //   - /np (NowPlayingCommand) reads Endpoints.WebsiteUrl
                //     to build its chat link; if that's the API host,
                //     clicking the link externally 404s on JSON.
                //   - MessageFormatter's WebsiteRootUrl is initialised
                //     from this value; the in-app chat parser uses it
                //     to decide whether a URL opens a beatmap overlay
                //     locally or punts to the browser.
                // Heuristic: ``<region>-api.<root>`` → ``<region>.<root>``
                // and ``api.<root>`` → ``<root>``. Falls back to the API
                // URL when neither pattern fits, preserving prior
                // behaviour for unknown deployments.
                config.WebsiteUrl = deriveWebsiteUrlFromApiUrl(customUrl);
                config.SpectatorUrl = $"{customUrl}/signalr/spectator";
                config.MultiplayerUrl = $"{customUrl}/signalr/multiplayer";
                config.MetadataUrl = $"{customUrl}/signalr/metadata";
                config.BeatmapSubmissionServiceUrl = $"{customUrl}/beatmap-submission";
            }

            // Log the resolved API URL on startup so users (and we) can
            // verify their custom server URL persisted across restarts —
            // the symptom we want to make visible if it ever regresses
            // again is "I set my server URL but the client comes up
            // pointing at the default."
            Logger.Log($"API endpoint resolved to {config.APIUrl} (custom URL setting={(string.IsNullOrEmpty(customUrl) ? "<empty>" : customUrl)})", LoggingTarget.Network);

            return config;
        }

        /// <summary>
        /// Derive the website URL from the configured API URL. Public so
        /// subclasses + tests can share the heuristic; the OsuGameBase
        /// custom-URL flow uses it on every endpoint refresh.
        /// </summary>
        /// <remarks>
        /// Two patterns supported, both common in osu! private-server
        /// deployments:
        /// <list type="bullet">
        /// <item><description><c>&lt;region&gt;-api.&lt;root&gt;</c> (e.g. <c>lazer-api.shikkesora.com</c>)
        /// →  <c>&lt;region&gt;.&lt;root&gt;</c> (<c>lazer.shikkesora.com</c>).</description></item>
        /// <item><description><c>api.&lt;root&gt;</c> (e.g. <c>api.example.dev</c>)
        /// →  <c>&lt;root&gt;</c> (<c>example.dev</c>).</description></item>
        /// </list>
        /// Falls back to the input URL when neither pattern matches —
        /// preserves prior behaviour for deployments where the API and
        /// website are co-hosted at the same hostname.
        /// </remarks>
        public static string DeriveWebsiteUrlFromApiUrl(string apiUrl) => deriveWebsiteUrlFromApiUrl(apiUrl);

        /// <summary>
        /// Extract the host portion of a URL ("https://foo.example/bar" → "foo.example"),
        /// matching the same shape <see cref="MessageFormatter.WebsiteRootUrl"/>'s
        /// setter produces. Returns the input on parse failure so callers
        /// don't need their own try/catch.
        /// </summary>
        private static string extractHost(string url)
        {
            try
            {
                return new Uri(url).Host;
            }
            catch (UriFormatException)
            {
                return url;
            }
        }

        private static string deriveWebsiteUrlFromApiUrl(string apiUrl)
        {
            if (string.IsNullOrEmpty(apiUrl))
                return apiUrl;

            try
            {
                var uri = new Uri(apiUrl);
                string host = uri.Host;
                string newHost = host;

                // ``<region>-api.<root>`` → ``<region>.<root>`` is the
                // most common pattern (Torii uses lazer-api / lazer).
                // We replace ``-api.`` rather than just ``-api`` so we
                // don't mangle a hypothetical ``foo-api-staging.host``
                // that legitimately contains "-api" mid-label.
                if (host.Contains(@"-api.", StringComparison.OrdinalIgnoreCase))
                {
                    newHost = host.Replace(@"-api.", ".", StringComparison.OrdinalIgnoreCase);
                }
                else if (host.StartsWith(@"api.", StringComparison.OrdinalIgnoreCase))
                {
                    // ``api.example.com`` → ``example.com``. Only triggers
                    // when ``api`` is the leading subdomain label, not
                    // when it appears mid-host.
                    newHost = host.Substring(4);
                }

                if (newHost == host)
                    return apiUrl; // No transformation; preserve as-is.

                return $"{uri.Scheme}://{newHost}";
            }
            catch (UriFormatException)
            {
                // Malformed URL — never blow up the boot path over a
                // bad config string. Fall through to the original.
                return apiUrl;
            }
        }

        protected override OnlineStore CreateOnlineStore() => new TrustedDomainOnlineStore(LocalConfig);

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

        protected SessionStatics SessionStatics { get; private set; }

        protected OsuColour Colours { get; private set; }

        protected BeatmapManager BeatmapManager { get; private set; }

        protected BeatmapModelDownloader BeatmapDownloader { get; private set; }

        protected ScoreManager ScoreManager { get; private set; }

        protected ScoreModelDownloader ScoreDownloader { get; private set; }

        protected SkinManager SkinManager { get; private set; }

        protected RealmRulesetStore RulesetStore { get; private set; }
        protected RulesetHashCache RulesetHashCache { get; private set; }

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

        private Bindable<string> frameworkLocale = null!;

        private IBindable<LocalisationParameters> localisationParameters = null!;

        /// <summary>
        /// Held as a field (not a local in <see cref="SetHost"/>) so the
        /// bound copy survives past SetHost's stack frame. Osu-framework
        /// bindables keep child references via a weak list, so a local
        /// gets silently dropped on the next GC and the value-changed
        /// chain to <c>host.ToriiInputAudioHz</c> stops firing. That was
        /// the "el dropdown no hace NADA" bug — the wiring was correct
        /// for the first paint, then GC nuked it.
        /// </summary>
        private Bindable<ToriiInputAudioHzMode> inputAudioHzSetting = null!;

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

            dependencies.CacheAs(RulesetHashCache = new RulesetHashCache(RulesetStore));

            Decoder.RegisterDependencies(RulesetStore);

            dependencies.CacheAs(Storage);

            var largeStore = new LargeTextureStore(Host.Renderer, Host.CreateTextureLoaderStore(new NamespacedResourceStore<byte[]>(Resources, @"Textures")));
            largeStore.AddTextureSource(Host.CreateTextureLoaderStore(CreateOnlineStore()));
            dependencies.Cache(largeStore);

            dependencies.CacheAs(LocalConfig);
            dependencies.CacheAs<IGameplaySettings>(LocalConfig);
            ToriiPpVariantState.Initialise(LocalConfig);

            // Torii cosmetics (cursor-trail store): app-wide owned/equipped/
            // points state, shared by the cursor containers + the store overlay.
            var toriiCosmetics = new osu.Game.Cosmetics.ToriiCosmeticsManager(LocalConfig);
            dependencies.Cache(toriiCosmetics);

            // Torii: let the username decorator (UserAuraContainer) resolve the
            // FULL local user on surfaces that only carry a stripped user object
            // (a leaderboard score's user has no groups), so the local player's
            // aura + equipped name colour show up everywhere their name appears,
            // not just on their profile. Lambdas read live state when invoked.
            osu.Game.Graphics.UserEffects.UserAuraContainer.LocalUserProvider = () => API?.LocalUser.Value;
            osu.Game.Graphics.UserEffects.UserAuraContainer.LocalUserHasNameColour =
                () => !string.IsNullOrEmpty(toriiCosmetics.EquippedNameColourId.Value);

            // Accessibility / perf toggles: "reduced motion" calms the glow +
            // hides particles; "ignore cosmetics" renders plain usernames.
            osu.Game.Graphics.UserEffects.UserAuraContainer.ReducedMotion =
                () => LocalConfig.Get<bool>(OsuSetting.CosmeticsReducedMotion);
            osu.Game.Graphics.UserEffects.UserAuraContainer.CosmeticsSuppressed =
                () => LocalConfig.Get<bool>(OsuSetting.CosmeticsHidden);

            // ...and the same colour through ToriiColourHelper, which is the
            // username-colour authority the leaderboards already read (they set
            // the name colour via a transform, so they have to pull it from here
            // rather than have it painted on top).
            osu.Game.Online.ToriiColourHelper.LocalEquippedNameColourProvider = () =>
            {
                string colourId = toriiCosmetics.EquippedNameColourId.Value;
                if (string.IsNullOrEmpty(colourId))
                    return null;

                var resolved = osu.Game.Cosmetics.CosmeticNameColourCatalog.GetById(colourId, API?.LocalUser.Value);
                return resolved == null
                    ? (Colour4?)null
                    : new Colour4(resolved.Primary.R, resolved.Primary.G, resolved.Primary.B, resolved.Primary.A);
            };

            // Broadcast: when the local user equips/unequips a BOUGHT name colour,
            // tell the server so every other client paints their username with it
            // (mirrors the aura broadcast). Role colours (name-group-*) and "none"
            // clear the stored value, so others fall back to the group/role colour
            // resolved from API groups. Central bind so every equip surface (store,
            // settings, inventory) broadcasts without each having to wire it. Fires
            // only on change, never on the initial config load.
            toriiCosmetics.EquippedNameColourId.BindValueChanged(e =>
            {
                if (API?.IsLoggedIn != true)
                    return;

                string id = e.NewValue;
                bool buyable = osu.Game.Cosmetics.CosmeticNameColourCatalog.IsBuyable(id);
                API.Queue(new osu.Game.Online.API.Requests.UpdateEquippedNameColourRequest(buyable ? id : null));
            });

            // Torii: side-car JSON store backing the "[NEW]" pill on
            // settings + menus. Cached here (rather than living inside
            // SkinManager like PinnedSkinsStore) because the badge is
            // resolved across multiple UI surfaces — settings, soon
            // the toolbar / main menu — and shouldn't be tied to the
            // skin subsystem's lifetime.
            dependencies.Cache(new NewFeatureTracker(Storage));

            InitialiseFonts();

            addFilesWarning();

            Audio.Samples.PlaybackConcurrency = SAMPLE_CONCURRENCY;

            dependencies.Cache(SkinManager = new SkinManager(Storage, realm, Host, Resources, Audio, Scheduler));
            dependencies.CacheAs<ISkinSource>(SkinManager);

            EndpointConfiguration endpoints = CreateEndpoints();

            MessageFormatter.WebsiteRootUrl = endpoints.WebsiteUrl;

            // Register the API host as a secondary "known" host for chat
            // link parsing too. Without this, /np links produced by older
            // client releases (which used to set WebsiteUrl == APIUrl)
            // would not be recognised as beatmap links and would punt
            // to the browser. See MessageFormatter.AdditionalKnownHosts.
            MessageFormatter.AdditionalKnownHosts = !string.IsNullOrEmpty(endpoints.APIUrl) && endpoints.APIUrl != endpoints.WebsiteUrl
                ? new[] { extractHost(endpoints.APIUrl) }
                : System.Array.Empty<string>();

            frameworkLocale = frameworkConfig.GetBindable<string>(FrameworkSetting.Locale);
            frameworkLocale.BindValueChanged(_ => updateLanguage());

            localisationParameters = Localisation.CurrentParameters.GetBoundCopy();
            localisationParameters.BindValueChanged(_ => updateLanguage(), true);

            CurrentLanguage.BindValueChanged(val => frameworkLocale.Value = val.NewValue.ToCultureCode());

            dependencies.CacheAs(API ??= new APIAccess(this, LocalConfig, endpoints, VersionHash, RulesetHashCache));

            var defaultBeatmap = new DummyWorkingBeatmap(Audio, Textures);

            dependencies.Cache(difficultyCache = new BeatmapDifficultyCache());

            // ordering is important here to ensure foreign keys rules are not broken in ModelStore.Cleanup()
            dependencies.Cache(ScoreManager = new ScoreManager(RulesetStore, () => BeatmapManager, Storage, realm, API, LocalConfig));

            dependencies.Cache(BeatmapManager = new BeatmapManager(Storage, realm, API, Audio, Resources, Host, defaultBeatmap, difficultyCache, performOnlineLookups: true));
            dependencies.CacheAs<IWorkingBeatmapCache>(BeatmapManager);

            dependencies.Cache(BeatmapDownloader = new BeatmapModelDownloader(BeatmapManager, API));
            dependencies.Cache(ScoreDownloader = new ScoreModelDownloader(ScoreManager, API));

            // Add after all the above cache operations as it depends on them.
            base.Content.Add(difficultyCache);

            // TODO: OsuGame or OsuGameBase?
            dependencies.CacheAs(beatmapUpdater = CreateBeatmapUpdater());
            dependencies.CacheAs(SpectatorClient = new OnlineSpectatorClient(endpoints));
            dependencies.CacheAs(MultiplayerClient = new OnlineMultiplayerClient(endpoints));
            dependencies.CacheAs(metadataClient = new OnlineMetadataClient(endpoints));

            // Server broadcasts UserUpdated for any user whose public-facing
            // payload changed (cosmetic settings, group membership, custom
            // title, …). When the broadcast targets the locally-signed-in
            // user we trigger a full LocalUser refresh so badges + supporter
            // tag + every other surface bound to api.LocalUser repaints
            // without the user having to reopen anything. UserAuraContainer
            // handles the per-user case for OTHER users.
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

            // Torii: pin the cosmetic chrome theme BEFORE OsuColour is
            // constructed — its instance fields (Pink, Purple, Blue,
            // etc.) read the static `useGrayscale` flag in their
            // initialisers via OsuColour.fromHex(). If the theme is
            // applied after this line, OsuColour ships its default
            // saturated palette and the user only gets grayscale on
            // OverlayColourProvider (which is dynamic). The dropdown
            // in Settings → Skins / → Torii prompts for a restart on
            // change so this single read at startup is sufficient.
            OsuColour.SetThemeFromConfig(LocalConfig.Get<UIThemeOption>(OsuSetting.UITheme));

            // Torii: pin Potato Mode (extreme-perf preset for weak PCs)
            // alongside the theme. Like the theme it's a read-once flag
            // that heavy subsystems check at construction, so it has to be
            // known before the visual tree builds. Restart-gated via the
            // settings toggle for the same reason the theme is.
            Performance.PotatoMode.SetFromConfig(LocalConfig.Get<bool>(OsuSetting.ToriiPotatoMode));

            // Torii: Potato Mode forces the legacy (BASS) audio path instead of the
            // experimental low-latency WASAPI output. WASAPI's tiny buffer gets starved
            // by GC pauses on weak machines (audible stutter); legacy BASS keeps a larger
            // buffer that rides over those hitches. Applied in-memory at startup. Not
            // auto-restored on disable, so a weak PC stays on the safe backend; the user
            // can re-enable WASAPI any time in Settings -> Audio.
            if (Performance.PotatoMode.Active)
                frameworkConfig.SetValue(FrameworkSetting.AudioUseExperimentalWasapi, false);

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

        // Holds the Android low-latency audio bridge across the game lifetime.
        // Both null on non-Android or when EnableOboeAudio is OFF.
        private OboeBridgeManager? oboeBridgeManager;
        private OboeAudioRedirector? oboeAudioRedirector;
        // Held in a strong field per ConfigManager.GetBindable's contract
        // ("ensure to hold a local reference" — the returned bindable is
        // weakly bound to the config backing). The previous implementation
        // chained .GetBoundCopy() off this directly, dropped the intermediate
        // on the floor, and once the GC collected it the binding chain to
        // our subscribed copy went silent — the visible symptom was
        // "the Oboe toggle stops applying in real time after a while".
        private Bindable<bool>? oboeEnabledBindable;

        /// <summary>
        /// Subclass hook for fetching the device's native output sample rate
        /// from <c>android.media.AudioManager.PROPERTY_OUTPUT_SAMPLE_RATE</c>.
        /// Critical for Oboe to engage AAudio MMAP-exclusive output — without
        /// the correct rate, the stream falls back to a higher-latency mode
        /// that still works but doesn't deliver the full MMAP win.
        ///
        /// Default returns 0 (let Oboe pick a fallback rate). Overridden in
        /// <c>OsuGameAndroid</c> with the real Android-specific lookup.
        /// </summary>
        protected virtual int GetAndroidNativeOutputSampleRate() => 0;

        /// <summary>
        /// Boot or tear down the Android Oboe low-latency audio path based on
        /// the <see cref="OsuSetting.EnableOboeAudio"/> bindable.
        ///
        /// On Android, vanilla BASS plays audio through Java AudioTrack with
        /// 60–200 ms of layered buffering; this swap routes BASS into a
        /// decode-only global mixer that Oboe pulls from at the AAudio
        /// MMAP-exclusive output, dropping latency to ~15–30 ms on supported
        /// devices. The bridge auto-falls-back to OpenSL ES if AAudio is
        /// unavailable, and silently no-ops if the bundled
        /// <c>libosu_native.so</c> can't be loaded (e.g. Samsung security
        /// policies blocking <c>dlopen</c>) — in those cases the user stays
        /// on vanilla BASS audio with no error surface, just no improvement.
        ///
        /// Hot-swap supported: toggling the setting at runtime starts or stops
        /// the bridge live (the Oboe AudioRedirector recreates the master mixer
        /// when needed; the framework's BASS layer reattaches transparently).
        ///
        /// No-op on Desktop / iOS — there's no Oboe build for those platforms
        /// and the bridge wouldn't have a native lib to load anyway.
        /// </summary>
        private void initOboeAudio()
        {
            if (RuntimeInfo.OS != RuntimeInfo.Platform.Android)
                return;

            if (LocalConfig == null)
                return;

            // Bind once; the lambda dispatches start/stop based on the new value.
            // Assigning GetBindable directly to the field (rather than chaining
            // a further .GetBoundCopy() and discarding the intermediate) is what
            // keeps the binding chain GC-rooted — see the field declaration above
            // for why this matters.
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

                oboeAudioRedirector = new OboeAudioRedirector(Audio);
                oboeBridgeManager = new OboeBridgeManager();
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

            Fonts.AddStore(new OsuIcon.OsuIconStore(Textures));
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

            // Torii: wire the user-facing input/audio thread Hz setting to
            // the host's bindable so the framework picks up the value and
            // re-evaluates frame-sync rates whenever the user changes it.
            // The Hz enum's numeric value IS the actual Hz (see
            // ToriiInputAudioHzMode), so the cast back to int just unwraps
            // the underlying integer for the framework's BindableInt.
            //
            // Initial assignment happens via the immediate-fire flag on
            // BindValueChanged — eq sense as the framework's other config
            // bindings — so a fresh start picks up the saved preference on
            // the first updateFrameSyncMode pass rather than the framework's
            // own default of 2000.
            // Torii: on the very first launch, seed the Hz default from the machine's
            // rough capability so a weak/old PC doesn't open at the 2000 competitive
            // default and hiccup. One-shot (guarded by ToriiInputAudioHzAutoTuned); the
            // user's dropdown choice wins on every launch after this.
            if (!LocalConfig.Get<bool>(OsuSetting.ToriiInputAudioHzAutoTuned))
            {
                var tunedHz = ToriiInputAudioHzDefaults.ForThisMachine();
                Logger.Log($"Torii: first-launch input/audio Hz auto-tuned to {(int)tunedHz} ({Environment.ProcessorCount} cores).");
                LocalConfig.SetValue(OsuSetting.ToriiInputAudioHz, tunedHz);
                LocalConfig.SetValue(OsuSetting.ToriiInputAudioHzAutoTuned, true);
            }

            inputAudioHzSetting = LocalConfig.GetBindable<ToriiInputAudioHzMode>(OsuSetting.ToriiInputAudioHz);
            inputAudioHzSetting.BindValueChanged(e => host.ToriiInputAudioHz.Value = (int)e.NewValue, true);
        }

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
        /// If supported by the platform, the game will automatically restart after the next exit.
        /// </summary>
        /// <returns>Whether a restart operation was queued.</returns>
        public virtual bool RestartAppWhenExited() => false;

        /// <summary>
        /// Perform migration of user data to a specified path.
        /// </summary>
        /// <param name="path">The path to migrate to.</param>
        /// <returns>Whether migration succeeded to completion. If <c>false</c>, some files were left behind.</returns>
        /// <exception cref="TimeoutException"></exception>
        public bool Migrate(string path)
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

            // Tear down the Oboe bridge before LocalConfig so any final lifecycle
            // logging it writes is preserved across shutdown. No-op if init failed
            // or never ran (non-Android / setting OFF / already torn down via toggle).
            try { oboeEnabledBindable?.UnbindAll(); } catch { }
            stopOboeBridge();

            RulesetStore?.Dispose();
            LocalConfig?.Dispose();

            beatmapUpdater?.Dispose();

            realm?.Dispose();

            if (Host != null)
                Host.ExceptionThrown -= onExceptionThrown;
        }

        ControlPointInfo IBeatSyncProvider.ControlPoints => Beatmap.Value.BeatmapLoaded ? Beatmap.Value.Beatmap.ControlPointInfo : null;
        IClock IBeatSyncProvider.Clock => beatmapClock;
        ChannelAmplitudes IHasAmplitudes.CurrentAmplitudes => Beatmap.Value.TrackLoaded ? Beatmap.Value.Track.CurrentAmplitudes : ChannelAmplitudes.Empty;
    }
}
