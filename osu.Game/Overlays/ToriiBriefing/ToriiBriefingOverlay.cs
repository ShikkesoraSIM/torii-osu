// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.IO.Serialization;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Rulesets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.ToriiBriefing
{
    /// <summary>
    /// Post-login welcome overlay that summarises the changes the user
    /// missed since their previous session — rank/pp movement, recalcs,
    /// unread chat, dojo radar events.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This file is the controller / data-flow layer. The view layer is
    /// split across <see cref="BriefingTheme"/> (visual constants),
    /// <see cref="BriefingGlass"/> (the Liquid Glass material), the four
    /// drawable files (<see cref="BriefingCard"/>, <see cref="BriefingRecalcCard"/>,
    /// <see cref="BriefingPill"/>, <see cref="BriefingSectionHeader"/>),
    /// and the model file (<c>BriefingModels.cs</c>). The decomposition
    /// replaced a 1700-line monolith.
    /// </para>
    /// </remarks>
    public partial class ToriiBriefingOverlay : OsuFocusedOverlayContainer
    {
        private const string snapshot_filename = @"briefing-state.json";
        private const string last_briefing_filename = @"last-briefing.json";

        private readonly ChannelManager channelManager;
        private readonly HashSet<string> shownThisSession = new HashSet<string>();
        private readonly HashSet<string> pendingThisSession = new HashSet<string>();
        private readonly IBindable<APIState> apiState = new Bindable<APIState>();
        private readonly IBindable<APIUser> localUser = new Bindable<APIUser>();
        private int latestBriefingRequestId;

        private IAPIProvider api;
        private RulesetStore rulesets;
        private Storage briefingStorage;
        private TextureStore textures;

        private BriefingGlass panel;

        /// <summary>
        /// Test-only accessor for the panel so visual A/B test scenes
        /// can swap shadow / surface settings live without rebuilding
        /// the briefing each time.
        /// </summary>
        internal BriefingGlass PanelGlass => panel;

        private OsuSpriteText title;
        private OsuSpriteText subtitle;
        private FillFlowContainer cardFlow;

        protected override string PopInSampleName => @"UI/overlay-big-pop-in";
        protected override string PopOutSampleName => @"UI/overlay-big-pop-out";

        public override bool BlockScreenWideMouse => true;

        public ToriiBriefingOverlay(ChannelManager channelManager)
        {
            this.channelManager = channelManager;
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load(IAPIProvider api, RulesetStore rulesets, Storage storage, TextureStore textures)
        {
            this.api = api;
            this.rulesets = rulesets;
            this.textures = textures;
            briefingStorage = storage.GetStorageForDirectory(@"torii");

            InternalChildren = new Drawable[]
            {
                // Scrim. Slightly less opaque than before — lets the panel's
                // glow bleed onto whatever is behind, which sells the "lifted
                // glass" feel. The user's eye still parks on the panel because
                // it's the brightest thing on screen.
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(
                        Color4.Black.Opacity(0.62f),
                        Color4.Black.Opacity(0.72f)),
                },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.92f, 0.9f),
                    FillMode = FillMode.Fit,
                    FillAspectRatio = 1.62f,
                    Child = panel = new BriefingGlass
                    {
                        RelativeSizeAxes = Axes.Both,
                        // Panel uses fixed (Both-relative) content sizing rather than the
                        // default card mode — the GridContainer inside fills the whole panel.
                        RelativeContentSize = Axes.Both,
                        CornerSize = BriefingTheme.CornerLg,
                        // Default panel shadow — neutral black "soft deep distant" recipe
                        // (winning vote from the visual test browser). Big radius + low
                        // opacity = the panel feels far away from whatever is behind it,
                        // cinematic feel without competing with brand colours.
                        //
                        // On mobile the radius is throttled hard: a Gaussian blur's GPU
                        // cost scales with radius² so a 60-px radius shadow on a tablet
                        // screen costs ~12× more than an 18-px one. Opacity is bumped a
                        // touch to compensate for the visual weight loss.
                        ShadowColor = Color4.Black,
                        ShadowOpacity = osu.Framework.RuntimeInfo.IsDesktop ? 0.30f : 0.45f,
                        ShadowRadius = osu.Framework.RuntimeInfo.IsDesktop ? 60f : 18f,
                        ShadowRoundness = osu.Framework.RuntimeInfo.IsDesktop ? 16f : 8f,
                        ShadowOffset = new Vector2(0, osu.Framework.RuntimeInfo.IsDesktop ? 24f : 8f),
                        SurfaceLift = 1.0f, // panel = base; cards lift above it
                        SpecularStrength = 0.18f,
                        SpecularHeight = 80f, // bigger ribbon for the panel's larger surface
                        Child = new Container
                        {
                            RelativeSizeAxes = Axes.Both,
                            Children = new Drawable[]
                            {
                                // Decorative Torii silhouette anchored bottom-right at very
                                // low opacity — gives the panel something visually behind
                                // the card stack so the cards feel "above" rather than
                                // "stickered onto a flat dark slab". Only the corner edges
                                // peek through; cards cover the centre.
                                createPanelDecoration(),
                                new Container
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Padding = new MarginPadding
                                    {
                                        Horizontal = BriefingTheme.SpacingXl,
                                        Vertical = BriefingTheme.SpacingLg + 4,
                                    },
                                    Child = new GridContainer
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        RowDimensions = new[]
                                        {
                                            new Dimension(GridSizeMode.AutoSize),
                                            new Dimension(),
                                            new Dimension(GridSizeMode.AutoSize),
                                        },
                                        Content = new[]
                                        {
                                            new Drawable[] { createHeader() },
                                            new Drawable[]
                                            {
                                                new OsuScrollContainer
                                                {
                                                    RelativeSizeAxes = Axes.Both,
                                                    ScrollbarOverlapsContent = false,
                                                    Padding = new MarginPadding
                                                    {
                                                        Top = BriefingTheme.SpacingLg,
                                                        Right = BriefingTheme.SpacingXs + 2,
                                                    },
                                                    Child = cardFlow = new FillFlowContainer
                                                    {
                                                        RelativeSizeAxes = Axes.X,
                                                        AutoSizeAxes = Axes.Y,
                                                        Direction = FillDirection.Vertical,
                                                        // Generous gutter (14) so cards breathe individually instead
                                                        // of merging into a stripe; combined with the small contact
                                                        // shadow above, no card's shadow reaches into the next one.
                                                        Spacing = new Vector2(0, BriefingTheme.SpacingMd - 2),
                                                    },
                                                },
                                            },
                                            new Drawable[] { createFooter() },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        /// <summary>
        /// Faded Torii brand logo anchored to the bottom-right corner of the
        /// panel — gives the panel a "behind" element so cards read as
        /// floating above something rather than stickered onto a flat slab.
        /// Tinted pink at very low alpha so it blends into the panel
        /// gradient rather than competing with it.
        /// </summary>
        private Drawable createPanelDecoration()
        {
            var logo = textures?.Get(@"Torii/logo");

            if (logo == null)
                return Empty();

            return new Sprite
            {
                Anchor = Anchor.BottomRight,
                Origin = Anchor.BottomRight,
                X = -BriefingTheme.SpacingLg,
                Y = -BriefingTheme.SpacingLg,
                Size = new Vector2(280),
                FillMode = FillMode.Fit,
                Texture = logo,
                Alpha = 0.04f,
                Colour = BriefingTheme.AccentPink,
            };
        }

        private Drawable createHeader()
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 92,
                Children = new Drawable[]
                {
                    // Logo tile — matches the saturated-fill + white-icon vocabulary
                    // every card uses. Same chrome as BriefingCard.buildIconTile, just
                    // bigger (60px vs 36px) so the header reads as the heaviest tile in
                    // the visual hierarchy.
                    new Container
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(60),
                        Masking = true,
                        CornerRadius = BriefingTheme.CornerMd,
                        CornerExponent = BriefingTheme.SquircleExponent,
                        MaskingSmoothness = 1.4f,
                        Children = new Drawable[]
                        {
                            // Saturated pink fill — same gradient pattern as the card tiles.
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientVertical(
                                    BriefingTheme.AccentPink.Lighten(0.1f),
                                    BriefingTheme.AccentPink.Darken(0.1f)),
                            },
                            // Soft inner top-edge highlight — same lit-from-above cue as
                            // the card tiles, scaled up for the bigger surface.
                            new Container
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 22,
                                Anchor = Anchor.TopCentre,
                                Origin = Anchor.TopCentre,
                                Child = new Box
                                {
                                    RelativeSizeAxes = Axes.Both,
                                    Colour = ColourInfo.GradientVertical(
                                        Color4.White.Opacity(0.20f),
                                        Color4.White.Opacity(0)),
                                },
                            },
                            createToriiLogo(),
                        },
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        X = 60 + BriefingTheme.SpacingMd,
                        Padding = new MarginPadding { Right = 60 + BriefingTheme.SpacingMd },
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, BriefingTheme.SpacingXs - 1),
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(BriefingTheme.SpacingSm + 2, 0),
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Text = "Torii briefing",
                                        Font = OsuFont.GetFont(size: BriefingTheme.TypeDisplay, weight: FontWeight.Bold),
                                    },
                                    new BriefingPill("daily portal", BriefingTheme.AccentCyan)
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                    },
                                },
                            },
                            title = new OsuSpriteText
                            {
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeHeadline, weight: FontWeight.SemiBold),
                                Colour = Color4.White.Opacity(0.78f),
                            },
                            subtitle = new OsuSpriteText
                            {
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                                Colour = Color4.White.Opacity(BriefingTheme.InkTertiary),
                            },
                        },
                    },
                    // Floating close button — small soft tile that hovers in the top-right corner.
                    new CloseTile
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Action = Hide,
                    },
                },
            };
        }

        /// <summary>
        /// Header logo glyph — the FontAwesome torii-gate icon rendered in pure
        /// white on top of the saturated pink tile.
        /// </summary>
        /// <remarks>
        /// The earlier implementation loaded the bundled <c>Torii/logo</c> texture
        /// (a coloured red/pink torii bitmap) and stacked it on top of a pink
        /// gradient tile. Two pinks competing on the same square produced a
        /// muddy purple-ish blend in the middle and made the silhouette hard to
        /// read at the small header scale. FontAwesome's vector
        /// <c>ToriiGate</c> glyph is monochrome by design, so a single
        /// <see cref="Color4.White"/> fill gives high contrast against any
        /// accent-colour tile underneath — the same vocabulary every card icon
        /// already uses, so the whole briefing reads as one system.
        /// </remarks>
        private Drawable createToriiLogo()
        {
            return new SpriteIcon
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Size = new Vector2(30),
                Icon = FontAwesome.Solid.ToriiGate,
                Colour = Color4.White,
            };
        }

        private Drawable createFooter()
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 50,
                Margin = new MarginPadding { Top = BriefingTheme.SpacingMd },
                Children = new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Text = "Generated from live Torii API data and your local session snapshot.",
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                        Colour = Color4.White.Opacity(BriefingTheme.InkTertiary),
                    },
                    new RoundedButton
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Width = 196,
                        Height = 44,
                        Text = "enter Torii",
                        BackgroundColour = BriefingTheme.AccentPink,
                        Action = Hide,
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            apiState.BindTo(api.State);
            localUser.BindTo(api.LocalUser);

            apiState.BindValueChanged(_ => queueBriefingIfReady(), true);
            localUser.BindValueChanged(_ => queueBriefingIfReady(), true);

            // Login restoration can complete before this overlay is fully loaded, depending on startup timing.
            // A few cheap retries make the briefing resilient without polling forever.
            Scheduler.AddDelayed(queueBriefingIfReady, 500);
            Scheduler.AddDelayed(queueBriefingIfReady, 2500);
            Scheduler.AddDelayed(queueBriefingIfReady, 7000);
        }

        private void queueBriefingIfReady()
        {
            if (!api.IsLoggedIn || apiState.Value != APIState.Online || localUser.Value?.Id <= 1)
                return;

            var user = localUser.Value;
            var ruleset = getCurrentRuleset(user);
            string variant = ToriiPpVariantState.UsePpDevVariant ? "pp_dev" : "stable";
            string sessionKey = $"{user.Id}:{ruleset.ShortName}:{variant}";

            if (shownThisSession.Contains(sessionKey) || !pendingThisSession.Add(sessionKey))
                return;

            Logger.Log($"Torii briefing queued for {sessionKey}.");
            fetchBriefingData(sessionKey, user.Id, ruleset.ShortName, ToriiPpVariantState.UsePpDevVariant);
        }

        private RulesetInfo getCurrentRuleset(APIUser user)
        {
            if (!string.IsNullOrEmpty(user.PlayMode))
            {
                var userRuleset = rulesets.GetRuleset(user.PlayMode);

                if (userRuleset != null)
                    return userRuleset;
            }

            return rulesets.GetRuleset("osu") ?? rulesets.GetRuleset(0) ?? rulesets.AvailableRulesets.First();
        }

        private void fetchBriefingData(string sessionKey, int userId, string rulesetName, bool usePpDev)
        {
            if (!api.IsLoggedIn || apiState.Value != APIState.Online || localUser.Value?.Id <= 1)
            {
                Logger.Log($"Torii briefing fetch skipped for {sessionKey} (loggedIn={api.IsLoggedIn}, state={apiState.Value}, localUser={localUser.Value?.Id}).");
                pendingThisSession.Remove(sessionKey);
                return;
            }

            var ruleset = rulesets.GetRuleset(rulesetName) ?? rulesets.GetRuleset("osu") ?? rulesets.GetRuleset(0) ?? rulesets.AvailableRulesets.First();
            var pending = new PendingBriefing(++latestBriefingRequestId, sessionKey, localUser.Value, ruleset, usePpDev);

            Logger.Log($"Torii briefing fetching for {sessionKey}.");

            var userRequest = new GetUserRequest(userId, ruleset);
            userRequest.Success += response =>
            {
                pending.User = response;
                pending.MarkBlockingComplete();
                showWhenComplete(pending);
            };
            userRequest.Failure += _ =>
            {
                pending.MarkBlockingComplete();
                showWhenComplete(pending);
            };

            var scoresRequest = new GetUserScoresRequest(userId, ScoreType.Best, new PaginationParameters(20), ruleset);
            scoresRequest.Success += response =>
            {
                pending.TopScores = response;
                pending.MarkBlockingComplete();
                showWhenComplete(pending);
            };
            scoresRequest.Failure += _ =>
            {
                pending.MarkBlockingComplete();
                showWhenComplete(pending);
            };

            // Radar is an enhancement: if it's slow or unavailable (older server), the briefing
            // still has plenty to show. Fire-and-forget so it never delays the overlay appearing.
            var radarRequest = new GetToriiBriefingRadarRequest(ruleset);
            radarRequest.Success += response =>
            {
                pending.Radar = response;
            };
            radarRequest.Failure += _ =>
            {
                // Older servers won't have this endpoint yet. Keep the briefing useful using local fallback cards.
            };

            try
            {
                api.Queue(userRequest);
                api.Queue(scoresRequest);
                api.Queue(radarRequest);
            }
            catch
            {
                pendingThisSession.Remove(sessionKey);
            }
        }

        private void showWhenComplete(PendingBriefing pending)
        {
            if (!pending.IsComplete)
                return;

            if (pending.RequestId != latestBriefingRequestId)
            {
                pendingThisSession.Remove(pending.SessionKey);
                Logger.Log($"Torii briefing ignored stale request {pending.RequestId} for {pending.SessionKey} (latest={latestBriefingRequestId}).");
                return;
            }

            var payload = createPayload(pending);

            if (payload == null)
            {
                pendingThisSession.Remove(pending.SessionKey);
                return;
            }

            displayPayload(payload);
            pendingThisSession.Remove(pending.SessionKey);
            shownThisSession.Add(pending.SessionKey);
            Logger.Log($"Torii briefing shown for {pending.SessionKey}.");
            Show();
        }

        private BriefingPayload createPayload(PendingBriefing pending)
        {
            var user = pending.User ?? pending.LocalUser;

            if (user == null)
                return null;

            var scores = pending.TopScores ?? new List<SoloScoreInfo>();
            string variant = pending.UsePpDev ? "pp_dev" : "stable";
            string rulesetShortName = pending.Ruleset?.ShortName ?? "osu";
            string snapshotKey = $"{user.Id}:{rulesetShortName}:{variant}";
            string stableSnapshotKey = $"{user.Id}:{rulesetShortName}:stable";
            string promotionMigrationKey = $"{user.Id}:{rulesetShortName}:ppdev-promotion";

            var currentSnapshot = new BriefingSnapshot
            {
                UserId = user.Id,
                Username = user.Username,
                Ruleset = rulesetShortName,
                Variant = variant,
                CapturedAt = DateTimeOffset.UtcNow,
                GlobalRank = user.Statistics?.GlobalRank,
                CountryRank = user.Statistics?.CountryRank,
                PP = toDouble(user.Statistics?.PP),
                TopScores = scores.Where(s => s != null).Select(createScoreSnapshot).Where(s => s.ScoreId > 0).ToList(),
            };

            var state = loadSnapshotState();
            state.Users.TryGetValue(snapshotKey, out var previousSnapshot);

            if (pending.UsePpDev
                && !state.ConsumedPromotionMigrations.Contains(promotionMigrationKey)
                && state.Users.TryGetValue(stableSnapshotKey, out var stableSnapshot))
            {
                // The first pp-dev briefing after Torii promoted pp-dev to the main osu! variant
                // should compare against the user's last stable snapshot, even if a blank/partial
                // pp-dev snapshot was already written earlier in the same update window.
                previousSnapshot = stableSnapshot;
                state.ConsumedPromotionMigrations.Add(promotionMigrationKey);
            }

            state.Users[snapshotKey] = currentSnapshot;
            saveSnapshotState(state);

            return new BriefingPayload
            {
                User = user,
                Ruleset = pending.Ruleset,
                Variant = variant,
                Current = currentSnapshot,
                Previous = previousSnapshot,
                ScoreChanges = getScoreChanges(previousSnapshot, currentSnapshot),
                UnreadMessages = getUnreadMessages(user.Id),
                RadarFirstSnapshot = pending.Radar?.FirstSnapshot ?? false,
                RadarTrackedCount = pending.Radar?.TrackedCount ?? 0,
                RadarEvents = getRadarEvents(previousSnapshot, currentSnapshot, pending.Radar),
            };
        }

        private BriefingScoreSnapshot createScoreSnapshot(SoloScoreInfo score)
        {
            return new BriefingScoreSnapshot
            {
                ScoreId = score.ID ?? 0,
                Title = getScoreTitle(score),
                Rank = score.Rank.ToString(),
                PP = score.PP,
                Accuracy = score.Accuracy,
            };
        }

        private string getScoreTitle(SoloScoreInfo score)
        {
            string artist = score.Beatmap?.BeatmapSet?.Artist ?? "Unknown artist";
            string title = score.Beatmap?.BeatmapSet?.Title ?? "Unknown title";
            string difficulty = score.Beatmap?.DifficultyName ?? "Unknown difficulty";

            return $"{artist} - {title} [{difficulty}]";
        }

        private List<BriefingScoreChange> getScoreChanges(BriefingSnapshot previous, BriefingSnapshot current)
        {
            if (previous?.TopScores == null || previous.TopScores.Count == 0)
                return new List<BriefingScoreChange>();

            var previousById = previous.TopScores.ToDictionary(s => s.ScoreId);
            var changes = new List<BriefingScoreChange>();

            foreach (var score in current.TopScores)
            {
                if (!score.PP.HasValue || !previousById.TryGetValue(score.ScoreId, out var oldScore) || !oldScore.PP.HasValue)
                    continue;

                double delta = score.PP.Value - oldScore.PP.Value;

                if (Math.Abs(delta) < 0.05)
                    continue;

                changes.Add(new BriefingScoreChange
                {
                    Title = score.Title,
                    OldPP = oldScore.PP.Value,
                    NewPP = score.PP.Value,
                    Delta = delta,
                });
            }

            return changes.OrderByDescending(c => Math.Abs(c.Delta)).ToList();
        }

        private List<BriefingMessage> getUnreadMessages(int localUserId)
        {
            return channelManager.JoinedChannels
                                 .SelectMany(c => c.UnreadMessages.Select(m => new { Channel = c, Message = m }))
                                 .Where(m => m.Message.Sender?.Id != localUserId)
                                 .OrderByDescending(m => m.Message.Timestamp)
                                 .Take(4)
                                 .Select(m => new BriefingMessage
                                 {
                                     Sender = m.Message.Sender?.Username ?? m.Channel.Name ?? "someone",
                                     Channel = m.Channel.Name ?? "chat",
                                     Preview = !string.IsNullOrEmpty(m.Message.DisplayContent) ? m.Message.DisplayContent : m.Message.Content ?? string.Empty,
                                 })
                                 .ToList();
        }

        private List<BriefingRadarEvent> getRadarEvents(BriefingSnapshot previous, BriefingSnapshot current, ToriiBriefingRadarResponse serverRadar)
        {
            var serverEvents = serverRadar?.Events?
                                          .Where(e => !string.IsNullOrEmpty(e.Headline) || !string.IsNullOrEmpty(e.Detail))
                                          .Select(e => new BriefingRadarEvent
                                          {
                                              Title = string.IsNullOrEmpty(e.Headline) ? "Dojo radar shift" : e.Headline,
                                              Detail = e.Detail,
                                              Severity = e.Severity,
                                          })
                                          .ToList();

            if (serverEvents?.Count > 0)
                return serverEvents;

            if (previous?.TopScores == null || previous.TopScores.Count == 0)
                return new List<BriefingRadarEvent>();

            // Local fallback for dev/old servers. The real snipe feed comes from /api/v2/torii/briefing/radar.
            var currentScoreIds = current.TopScores.Select(s => s.ScoreId).ToHashSet();
            return previous.TopScores
                           .Where(s => s.ScoreId > 0 && !currentScoreIds.Contains(s.ScoreId))
                           .Take(3)
                           .Select(s => new BriefingRadarEvent
                           {
                               Title = s.Title,
                               Detail = "Left your locally tracked top-play set.",
                               Severity = "info",
                           })
                           .ToList();
        }

        private void displayPayload(BriefingPayload payload)
        {
            // Snapshot persistence is fire-and-forget on a worker thread —
            // it's a few KB of JSON written to disk, but on slow flash
            // (mobile / portable installs) the synchronous write was
            // adding 20-100 ms to the briefing-show frame. We don't need
            // to wait for it: a failure logs and is recoverable on the
            // next briefing.
            Task.Run(() => saveLastBriefing(payload));

            title.Text = $"Welcome back, {payload.User.Username}.";
            var capturedAt = payload.Current?.CapturedAt.ToLocalTime() ?? DateTimeOffset.Now;
            subtitle.Text = $"{payload.Ruleset.Name} - {(payload.Variant == "pp_dev" ? "latest pp-dev calculations" : "standard calculations")} - {capturedAt:MMM d, HH:mm}";

            cardFlow.Clear();

            // Build all items first then batch-add, so the FillFlowContainer
            // re-runs its layout once instead of N times. Each card is a
            // composite of ~12 drawables, so 7 sequential adds were
            // triggering O(N²) layout invalidations on the briefing-show
            // frame — visible as a brief jitter on lower-end hardware.
            var items = new Drawable[]
            {
                new BriefingSectionHeader("your session", "changes from your plays, rank, and pp snapshots"),
                createRankCard(payload),
                createScoreCard(payload),
                createSyncCard(payload),
                new BriefingSectionHeader("dojo radar", "things that changed around you while you were away", BriefingTheme.AccentPink),
                createMessageCard(payload),
                createRadarCard(payload),
            };

            for (int i = 0; i < items.Length; i++)
            {
                items[i].Alpha = 0;
                items[i].Y = BriefingTheme.SpacingMd - 4;
            }

            cardFlow.AddRange(items);

            for (int i = 0; i < items.Length; i++)
            {
                double delay = BriefingTheme.EntranceStagger * i;
                items[i].Delay(delay).FadeIn(BriefingTheme.EntranceDuration * 0.7, Easing.OutQuint);
                items[i].Delay(delay).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
            }
        }

        public void ForceBriefingRefresh()
        {
            if (!api.IsLoggedIn || localUser.Value?.Id <= 1)
                return;

            var user = localUser.Value;
            var ruleset = getCurrentRuleset(user);
            string variant = ToriiPpVariantState.UsePpDevVariant ? "pp_dev" : "stable";
            string sessionKey = $"{user.Id}:{ruleset.ShortName}:{variant}";

            latestBriefingRequestId++;
            shownThisSession.Remove(sessionKey);
            pendingThisSession.Remove(sessionKey);
            queueBriefingIfReady();
        }

        public void ShowLastBriefing()
        {
            var stored = loadLastBriefing();

            var payload = stored != null
                ? restoreStoredBriefing(stored)
                : restoreBriefingFromSnapshots();

            if (payload == null)
                return;

            displayPayload(payload);
            Show();
        }

        private BriefingPayload restoreBriefingFromSnapshots()
        {
            if (localUser.Value?.Id <= 1)
                return null;

            var user = localUser.Value;
            var ruleset = getCurrentRuleset(user);
            var state = loadSnapshotState();
            string preferredVariant = ToriiPpVariantState.UsePpDevVariant ? "pp_dev" : "stable";

            BriefingSnapshot current = null;
            BriefingSnapshot previous = null;
            string selectedVariant = preferredVariant;

            string currentKey = $"{user.Id}:{ruleset.ShortName}:{preferredVariant}";

            if (state.Users.TryGetValue(currentKey, out current))
            {
                if (preferredVariant == "pp_dev")
                    state.Users.TryGetValue($"{user.Id}:{ruleset.ShortName}:stable", out previous);
            }
            else if (preferredVariant == "pp_dev"
                     && state.Users.TryGetValue($"{user.Id}:{ruleset.ShortName}:pp_dev", out current))
            {
                selectedVariant = "pp_dev";
                state.Users.TryGetValue($"{user.Id}:{ruleset.ShortName}:stable", out previous);
            }
            else if (state.Users.TryGetValue($"{user.Id}:{ruleset.ShortName}:stable", out current))
            {
                selectedVariant = "stable";
            }

            if (current == null)
                return null;

            return new BriefingPayload
            {
                User = user,
                Ruleset = ruleset,
                Variant = selectedVariant,
                Current = current,
                Previous = previous,
                ScoreChanges = getScoreChanges(previous, current),
                UnreadMessages = getUnreadMessages(user.Id),
                RadarEvents = new List<BriefingRadarEvent>(),
                RadarFirstSnapshot = false,
                RadarTrackedCount = 0,
            };
        }

        /// <summary>
        /// Show the most recent briefing that has non-empty score changes — the
        /// "Replay last recalc" button surface in Torii Settings.
        ///
        /// History
        /// -------
        /// Originally this was <c>ShowPpDevPromotionBriefing</c>: a one-shot
        /// stable → pp-dev migration view. Once pp-dev became the only PP
        /// system, the <c>:stable</c> snapshots stopped being captured and the
        /// view went stale (no rows to compare against). Repurposed to surface
        /// "the last time my top plays got mass-rebalanced" — the typical
        /// trigger now is a server-side recalc (e.g. after a pp-dev bump).
        ///
        /// Resolution order
        /// ----------------
        /// 1. Last stored briefing payload, IF it has score changes.
        /// 2. A briefing reconstructed from local snapshot history, IF it
        ///    produces score changes — this is what the user sees right after
        ///    a server recalc on first open, because the local <c>:pp_dev</c>
        ///    snapshot is still pre-recalc.
        /// 3. Last stored briefing payload regardless — better to show the
        ///    most recent briefing than nothing.
        /// 4. Snapshot reconstruction with empty changes — last resort.
        ///
        /// (1) wins when the user already opened a fresh briefing post-recalc;
        /// (2) wins on first open after a recalc before any explicit Generate
        /// click. Either path lands on the same <see cref="BriefingRecalcCard"/>
        /// rendering with TOP GAINS / TOP LOSSES sections.
        /// </summary>
        public void ShowLastRecalcBriefing()
        {
            // Prefer a stored briefing that actually has score changes — that's
            // the one most likely to represent a real recalc event.
            var stored = loadLastBriefing();
            BriefingPayload payload = null;

            if (stored != null)
            {
                var restored = restoreStoredBriefing(stored);
                if (restored?.ScoreChanges != null && restored.ScoreChanges.Count > 0)
                    payload = restored;
            }

            // Fall back to reconstructing from snapshots if the stored one has
            // no changes (e.g. a quiet daily briefing overwrote the recalc one).
            if (payload == null)
            {
                var fromSnapshots = restoreBriefingFromSnapshots();
                if (fromSnapshots?.ScoreChanges != null && fromSnapshots.ScoreChanges.Count > 0)
                    payload = fromSnapshots;
            }

            // Last resort: show *something* even if no diff is available, so
            // the click isn't a silent no-op.
            if (payload == null && stored != null)
                payload = restoreStoredBriefing(stored);

            if (payload == null)
                payload = restoreBriefingFromSnapshots();

            if (payload == null)
                return;

            displayPayload(payload);
            Show();
        }

        public void ShowSampleBriefing()
        {
            var ruleset = rulesets?.GetRuleset("osu") ?? rulesets?.GetRuleset(0);

            if (ruleset == null)
                return;

            var sampleUser = new APIUser
            {
                Id = 19,
                Username = "Shikkesora",
            };

            displayPayload(new BriefingPayload
            {
                User = sampleUser,
                Ruleset = ruleset,
                Variant = "pp_dev",
                Previous = new BriefingSnapshot
                {
                    UserId = sampleUser.Id,
                    Username = sampleUser.Username,
                    Ruleset = ruleset.ShortName,
                    Variant = "pp_dev",
                    CapturedAt = DateTimeOffset.UtcNow.AddDays(-1),
                    GlobalRank = 24,
                    CountryRank = 3,
                    PP = 2301.42,
                    TopScores = new List<BriefingScoreSnapshot>
                    {
                        new BriefingScoreSnapshot { ScoreId = 1001, Title = "FAIRY FORE - Vivid [Insane]", PP = 83.28 },
                        new BriefingScoreSnapshot { ScoreId = 1002, Title = "Will Stetson - Of Our Time [Clipfarm Edit]", PP = 348.10 },
                    },
                },
                Current = new BriefingSnapshot
                {
                    UserId = sampleUser.Id,
                    Username = sampleUser.Username,
                    Ruleset = ruleset.ShortName,
                    Variant = "pp_dev",
                    CapturedAt = DateTimeOffset.UtcNow,
                    GlobalRank = 19,
                    CountryRank = 2,
                    PP = 2325.13,
                    TopScores = new List<BriefingScoreSnapshot>
                    {
                        new BriefingScoreSnapshot { ScoreId = 1001, Title = "FAIRY FORE - Vivid [Insane]", PP = 53.60 },
                        new BriefingScoreSnapshot { ScoreId = 1002, Title = "Will Stetson - Of Our Time [Clipfarm Edit]", PP = 351.30 },
                    },
                },
                ScoreChanges = new List<BriefingScoreChange>
                {
                    new BriefingScoreChange { Title = "FAIRY FORE - Vivid [Insane]", OldPP = 83.28, NewPP = 53.60, Delta = -29.68 },
                    new BriefingScoreChange { Title = "Will Stetson - Of Our Time [Clipfarm Edit]", OldPP = 348.10, NewPP = 351.30, Delta = 3.20 },
                },
                UnreadMessages = new List<BriefingMessage>
                {
                    new BriefingMessage { Channel = "general", Sender = "Seba", Preview = "that pp-dev thing actually works now?" },
                    new BriefingMessage { Channel = "staff", Sender = "ToriiHalo", Preview = "2 scores were recalculated overnight." },
                },
                RadarEvents = new List<BriefingRadarEvent>
                {
                    new BriefingRadarEvent { Title = "Shoujo A (Cut Ver.) [gwb's Extreme]", Detail = "MommyAcheron pushed you from #1 to #2." },
                },
            });

            Show();
        }

        /// <summary>
        /// Adds a card to the flow with the staggered spring entrance
        /// animation that gives the briefing its sequenced feel. Each item
        /// fades in + rises up by its index × <see cref="BriefingTheme.EntranceStagger"/>.
        /// </summary>
        private BriefingCard createRankCard(BriefingPayload payload)
        {
            var previous = payload.Previous;
            var current = payload.Current;
            var accent = BriefingTheme.AccentCyan;

            string headline;
            string detail;

            if (previous == null)
            {
                headline = "First briefing snapshot created";
                detail = "I will compare rank and pp movement from this point onward.";
            }
            else
            {
                headline = getRankHeadline(previous.GlobalRank, current.GlobalRank, out accent);
                string ppLine = getPpDelta(previous.PP, current.PP);
                detail = $"{formatRank(previous.GlobalRank)} -> {formatRank(current.GlobalRank)} / {ppLine}";
            }

            return new BriefingCard(FontAwesome.Solid.ChartLine, "rank pulse", headline, detail, accent)
            {
                TooltipText = previous == null
                    ? "No previous local snapshot exists yet."
                    : $"Previous country rank: {formatRank(previous.CountryRank)}\nCurrent country rank: {formatRank(current.CountryRank)}",
            };
        }

        private Drawable createScoreCard(BriefingPayload payload)
        {
            return new BriefingRecalcCard(payload.ScoreChanges)
            {
                TooltipText = payload.ScoreChanges.Count == 0
                    ? "When PP changes are detected, the changed scores will be listed here."
                    : string.Join("\n", payload.ScoreChanges.Select(c => $"{c.Title}: {formatPP(c.OldPP)} -> {formatPP(c.NewPP)} ({formatSignedPP(c.Delta)})")),
            };
        }

        private BriefingCard createMessageCard(BriefingPayload payload)
        {
            var accent = payload.UnreadMessages.Count > 0 ? BriefingTheme.AccentAmber : BriefingTheme.AccentCyan;
            string headline = payload.UnreadMessages.Count == 0
                ? "No unread chat pings"
                : $"{payload.UnreadMessages.Count} unread chat {(payload.UnreadMessages.Count == 1 ? "ping" : "pings")}";

            string detail = payload.UnreadMessages.Count == 0
                ? "Nothing urgent from joined chat channels yet."
                : string.Join("\n", payload.UnreadMessages.Take(2).Select(m => $"{m.Sender}: {trim(m.Preview, 54)}"));

            return new BriefingCard(FontAwesome.Solid.Comments, "dojo whispers", headline, detail, accent)
            {
                TooltipText = payload.UnreadMessages.Count == 0
                    ? "Open chat to see live channels."
                    : string.Join("\n", payload.UnreadMessages.Select(m => $"#{m.Channel} - {m.Sender}: {m.Preview}")),
            };
        }

        private BriefingCard createRadarCard(BriefingPayload payload)
        {
            var accent = payload.RadarEvents.Count > 0 ? BriefingTheme.AccentGain : BriefingTheme.AccentSky;
            string headline = payload.RadarEvents.Count == 0
                ? payload.RadarFirstSnapshot ? "Dojo radar baseline synced" : "No map radar alerts"
                : $"{payload.RadarEvents.Count} tracked {(payload.RadarEvents.Count == 1 ? "shift" : "shifts")} noticed";

            string detail = payload.RadarEvents.Count == 0
                ? payload.RadarFirstSnapshot
                    ? $"Watching {payload.RadarTrackedCount} map positions from now on."
                    : $"No watched map positions moved since the last briefing ({payload.RadarTrackedCount} tracked)."
                : string.Join("\n", payload.RadarEvents.Take(2).Select(e => $"{trim(e.Title, 48)}: {e.Detail}"));

            return new BriefingCard(FontAwesome.Solid.Crosshairs, "dojo radar", headline, detail, accent)
            {
                TooltipText = payload.RadarEvents.Count == 0
                    ? "Torii tracks your watched map leaderboard positions server-side and compares them on each briefing."
                    : string.Join("\n", payload.RadarEvents.Select(e => $"{e.Title}: {e.Detail}")),
            };
        }

        private BriefingCard createSyncCard(BriefingPayload payload)
        {
            string variantName = payload.Variant == "pp_dev" ? "pp-dev" : "stable";
            var accent = payload.Variant == "pp_dev" ? BriefingTheme.AccentSky : BriefingTheme.AccentGain;

            return new BriefingCard(FontAwesome.Solid.InfoCircle, "session mode", $"{variantName} profile synced", $"Tracking {payload.Current.TopScores.Count} top plays for future briefings.", accent)
            {
                TooltipText = "This briefing is generated client-side from Torii API responses and local snapshots.",
            };
        }

        private string getRankHeadline(int? previousRank, int? currentRank, out Color4 accent)
        {
            accent = BriefingTheme.AccentCyan;

            if (!previousRank.HasValue || !currentRank.HasValue)
                return "Rank data is warming up";

            int delta = previousRank.Value - currentRank.Value;

            if (delta > 0)
            {
                accent = BriefingTheme.AccentGain;
                return $"You gained {delta.ToString("N0", CultureInfo.InvariantCulture)} ranks";
            }

            if (delta < 0)
            {
                accent = BriefingTheme.AccentLoss;
                return $"You lost {Math.Abs(delta).ToString("N0", CultureInfo.InvariantCulture)} ranks";
            }

            return "Your rank held steady";
        }

        private string getPpDelta(double? previousPP, double? currentPP)
        {
            if (!previousPP.HasValue || !currentPP.HasValue)
                return "pp warming up";

            double delta = currentPP.Value - previousPP.Value;
            return $"{formatPP(previousPP.Value)} -> {formatPP(currentPP.Value)} ({formatSignedPP(delta)})";
        }

        private BriefingState loadSnapshotState()
        {
            try
            {
                if (!briefingStorage.Exists(snapshot_filename))
                    return normaliseState(new BriefingState());

                using (var stream = briefingStorage.GetStream(snapshot_filename, FileAccess.Read, FileMode.Open))
                using (var reader = new StreamReader(stream))
                    return normaliseState(reader.ReadToEnd().Deserialize<BriefingState>() ?? new BriefingState());
            }
            catch
            {
                return normaliseState(new BriefingState());
            }
        }

        private StoredBriefing loadLastBriefing()
        {
            try
            {
                if (!briefingStorage.Exists(last_briefing_filename))
                    return null;

                using (var stream = briefingStorage.GetStream(last_briefing_filename, FileAccess.Read, FileMode.Open))
                using (var reader = new StreamReader(stream))
                    return reader.ReadToEnd().Deserialize<StoredBriefing>();
            }
            catch
            {
                return null;
            }
        }

        private void saveSnapshotState(BriefingState state)
        {
            try
            {
                state = normaliseState(state);

                using (var stream = briefingStorage.GetStream(snapshot_filename, FileAccess.Write, FileMode.Create))
                using (var writer = new StreamWriter(stream))
                    writer.Write(state.Serialize());
            }
            catch
            {
                // Briefing snapshots are a convenience feature; never break login if local storage is unavailable.
            }
        }

        private static BriefingState normaliseState(BriefingState state)
        {
            state ??= new BriefingState();
            state.Users ??= new Dictionary<string, BriefingSnapshot>();
            state.ConsumedPromotionMigrations ??= new HashSet<string>();
            return state;
        }

        private void saveLastBriefing(BriefingPayload payload)
        {
            try
            {
                var stored = new StoredBriefing
                {
                    UserId = payload.User?.Id ?? 0,
                    Username = payload.User?.Username ?? payload.Current?.Username ?? "player",
                    Ruleset = payload.Ruleset?.ShortName ?? payload.Current?.Ruleset ?? "osu",
                    Variant = payload.Variant ?? payload.Current?.Variant ?? "stable",
                    Current = payload.Current,
                    Previous = payload.Previous,
                    ScoreChanges = payload.ScoreChanges ?? new List<BriefingScoreChange>(),
                    UnreadMessages = payload.UnreadMessages ?? new List<BriefingMessage>(),
                    RadarEvents = payload.RadarEvents ?? new List<BriefingRadarEvent>(),
                    RadarFirstSnapshot = payload.RadarFirstSnapshot,
                    RadarTrackedCount = payload.RadarTrackedCount,
                };

                using (var stream = briefingStorage.GetStream(last_briefing_filename, FileAccess.Write, FileMode.Create))
                using (var writer = new StreamWriter(stream))
                    writer.Write(stored.Serialize());
            }
            catch
            {
                // Re-showing the last briefing is a convenience feature; never break the live overlay.
            }
        }

        private BriefingPayload restoreStoredBriefing(StoredBriefing stored)
        {
            var ruleset = rulesets?.GetRuleset(stored.Ruleset) ?? rulesets?.GetRuleset("osu") ?? rulesets?.GetRuleset(0);

            if (ruleset == null)
                return null;

            return new BriefingPayload
            {
                User = new APIUser
                {
                    Id = stored.UserId,
                    Username = stored.Username,
                },
                Ruleset = ruleset,
                Variant = stored.Variant,
                Current = stored.Current,
                Previous = stored.Previous,
                ScoreChanges = stored.ScoreChanges ?? new List<BriefingScoreChange>(),
                UnreadMessages = stored.UnreadMessages ?? new List<BriefingMessage>(),
                RadarEvents = stored.RadarEvents ?? new List<BriefingRadarEvent>(),
                RadarFirstSnapshot = stored.RadarFirstSnapshot,
                RadarTrackedCount = stored.RadarTrackedCount,
            };
        }

        protected override void PopIn()
        {
            // Spring-y entrance: scale up from 0.94 → 1.0 with a slight overshoot
            // (OutBack at low strength) and the panel rising into place from below.
            // OsuFocusedOverlayContainer.PopIn() handles the alpha fade for us, so
            // we just animate the panel transform.
            this.FadeIn(220, Easing.OutQuint);
            panel.ScaleTo(0.94f).Then().ScaleTo(1, BriefingTheme.EntranceDuration, Easing.OutBack);
            panel.MoveToY(22).Then().MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            base.PopOut();
            this.FadeOut(BriefingTheme.DismissDuration - 40, Easing.OutQuint);
            panel.ScaleTo(0.985f, BriefingTheme.DismissDuration, Easing.OutQuint);
            panel.MoveToY(10, BriefingTheme.DismissDuration, Easing.OutQuint);
        }

        private static double? toDouble(decimal? value) => value.HasValue ? (double)value.Value : null;

        private static string formatRank(int? rank) => rank.HasValue ? $"#{rank.Value.ToString("N0", CultureInfo.InvariantCulture)}" : "unranked";

        private static string formatPP(double pp) => $"{pp:N2}pp";

        private static string formatSignedPP(double pp) => $"{(pp >= 0 ? "+" : string.Empty)}{pp:N2}pp";

        private static string trim(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return $"{text[..Math.Max(0, maxLength - 3)]}...";
        }

        /// <summary>
        /// Floating close button used in the header. Soft-tinted squircle
        /// tile that lifts on hover and dims on press, matching the rest
        /// of the briefing's Liquid Glass vocabulary.
        /// </summary>
        private partial class CloseTile : OsuClickableContainer
        {
            private Box hoverBox;
            private Container tile;

            public CloseTile()
            {
                Size = new Vector2(36);
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Children = new Drawable[]
                {
                    tile = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = BriefingTheme.CornerSm,
                        CornerExponent = BriefingTheme.SquircleExponent,
                        MaskingSmoothness = 1.2f,
                        BorderThickness = 1f,
                        BorderColour = Color4.White.Opacity(0.10f),
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = Color4.White.Opacity(0.06f),
                            },
                            hoverBox = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Alpha = 0,
                                Colour = Color4.White.Opacity(0.10f),
                            },
                            new SpriteIcon
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Size = new Vector2(13),
                                Icon = FontAwesome.Solid.Times,
                                Colour = Color4.White.Opacity(0.78f),
                            },
                        },
                    },
                };
            }

            protected override bool OnHover(osu.Framework.Input.Events.HoverEvent e)
            {
                hoverBox.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
                tile.ScaleTo(1.06f, BriefingTheme.HoverDuration, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(osu.Framework.Input.Events.HoverLostEvent e)
            {
                hoverBox.FadeOut(BriefingTheme.HoverDuration, Easing.OutQuint);
                tile.ScaleTo(1f, BriefingTheme.HoverDuration, Easing.OutQuint);
                base.OnHoverLost(e);
            }

            protected override bool OnMouseDown(osu.Framework.Input.Events.MouseDownEvent e)
            {
                tile.ScaleTo(0.94f, 80, Easing.OutQuint);
                return base.OnMouseDown(e);
            }

            protected override void OnMouseUp(osu.Framework.Input.Events.MouseUpEvent e)
            {
                tile.ScaleTo(1.06f, 200, Easing.OutQuint);
                base.OnMouseUp(e);
            }
        }
    }
}
