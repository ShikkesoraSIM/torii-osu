// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
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

        private Container panel;
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

            var accentBlue = Color4Extensions.FromHex(@"69d7ff");
            var accentPink = Color4Extensions.FromHex(@"ff66b3");
            var deepNavy = Color4Extensions.FromHex(@"090b26");

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black.Opacity(0.58f),
                },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.92f, 0.9f),
                    FillMode = FillMode.Fit,
                    FillAspectRatio = 1.62f,
                    Child = panel = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 26,
                        BorderThickness = 1.6f,
                        BorderColour = accentBlue.Opacity(0.35f),
                        EdgeEffect = new EdgeEffectParameters
                        {
                            Type = EdgeEffectType.Shadow,
                            Colour = accentPink.Opacity(0.22f),
                            Radius = 28,
                            Offset = new Vector2(0, 10),
                        },
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientVertical(deepNavy.Opacity(0.96f), Color4Extensions.FromHex(@"15112c").Opacity(0.96f)),
                            },
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = ColourInfo.GradientHorizontal(accentPink.Opacity(0.08f), accentBlue.Opacity(0.08f)),
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding { Horizontal = 32, Vertical = 28 },
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
                                        new Drawable[] { createHeader(accentBlue, accentPink) },
                                        new Drawable[]
                                        {
                                            new OsuScrollContainer
                                            {
                                                RelativeSizeAxes = Axes.Both,
                                                Padding = new MarginPadding { Top = 28, Right = 6 },
                                                Child = cardFlow = new FillFlowContainer
                                                {
                                                    RelativeSizeAxes = Axes.X,
                                                    AutoSizeAxes = Axes.Y,
                                                    Direction = FillDirection.Vertical,
                                                    Spacing = new Vector2(0, 14),
                                                },
                                            },
                                        },
                                        new Drawable[]
                                        {
                                            new Container
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                Height = 52,
                                                Margin = new MarginPadding { Top = 20 },
                                                Children = new Drawable[]
                                                {
                                                    new OsuSpriteText
                                                    {
                                                        Anchor = Anchor.CentreLeft,
                                                        Origin = Anchor.CentreLeft,
                                                        Text = "Generated from live Torii API data and your local session snapshot.",
                                                        Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                                                        Colour = Color4.White.Opacity(0.46f),
                                                    },
                                                    new RoundedButton
                                                    {
                                                        Anchor = Anchor.CentreRight,
                                                        Origin = Anchor.CentreRight,
                                                        Width = 190,
                                                        Height = 46,
                                                        Text = "enter Torii",
                                                        BackgroundColour = accentPink,
                                                        Action = Hide,
                                                    },
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        private Drawable createHeader(Color4 accentBlue, Color4 accentPink)
        {
            return new Container
            {
                RelativeSizeAxes = Axes.X,
                Height = 104,
                Children = new Drawable[]
                {
                    new CircularContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        Size = new Vector2(70),
                        Masking = true,
                        BorderThickness = 1.4f,
                        BorderColour = accentPink.Opacity(0.36f),
                        EdgeEffect = new EdgeEffectParameters
                        {
                            Type = EdgeEffectType.Shadow,
                            Colour = accentPink.Opacity(0.3f),
                            Radius = 18,
                        },
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = Color4Extensions.FromHex(@"141336"),
                            },
                            createToriiLogo(accentPink),
                        },
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        X = 92,
                        Padding = new MarginPadding { Right = 210 },
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 6),
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(10, 0),
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Text = "Torii briefing",
                                        Font = OsuFont.GetFont(size: 34, weight: FontWeight.Bold),
                                    },
                                    new BriefingPill("daily portal", accentBlue),
                                },
                            },
                            title = new OsuSpriteText
                            {
                                Font = OsuFont.GetFont(size: 19, weight: FontWeight.SemiBold),
                                Colour = Color4.White.Opacity(0.78f),
                            },
                            subtitle = new OsuSpriteText
                            {
                                Font = OsuFont.GetFont(size: 15, weight: FontWeight.SemiBold),
                                Colour = Color4.White.Opacity(0.46f),
                            },
                        },
                    },
                    new IconButton
                    {
                        Anchor = Anchor.TopRight,
                        Origin = Anchor.TopRight,
                        Icon = FontAwesome.Solid.Times,
                        Colour = Color4.White.Opacity(0.64f),
                        Action = Hide,
                    },
                },
            };
        }

        private Drawable createToriiLogo(Color4 accentPink)
        {
            var logo = textures?.Get(@"Torii/logo");

            if (logo != null)
            {
                return new Sprite
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(50),
                    FillMode = FillMode.Fit,
                    Texture = logo,
                };
            }

            return new OsuSpriteText
            {
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                Text = "T",
                Font = OsuFont.GetFont(size: 34, weight: FontWeight.Bold),
                Colour = accentPink,
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
                pending.MarkComplete();
                showWhenComplete(pending);
            };
            userRequest.Failure += _ =>
            {
                pending.MarkComplete();
                showWhenComplete(pending);
            };

            var scoresRequest = new GetUserScoresRequest(userId, ScoreType.Best, new PaginationParameters(20), ruleset);
            scoresRequest.Success += response =>
            {
                pending.TopScores = response;
                pending.MarkComplete();
                showWhenComplete(pending);
            };
            scoresRequest.Failure += _ =>
            {
                pending.MarkComplete();
                showWhenComplete(pending);
            };

            var radarRequest = new GetToriiBriefingRadarRequest(ruleset);
            radarRequest.Success += response =>
            {
                pending.Radar = response;
                pending.MarkComplete();
                showWhenComplete(pending);
            };
            radarRequest.Failure += _ =>
            {
                // Older servers won't have this endpoint yet. Keep the briefing useful using local fallback cards.
                pending.MarkComplete();
                showWhenComplete(pending);
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
            string snapshotKey = $"{user.Id}:{pending.Ruleset.ShortName}:{variant}";
            string stableSnapshotKey = $"{user.Id}:{pending.Ruleset.ShortName}:stable";
            string promotionMigrationKey = $"{user.Id}:{pending.Ruleset.ShortName}:ppdev-promotion";

            var currentSnapshot = new BriefingSnapshot
            {
                UserId = user.Id,
                Username = user.Username,
                Ruleset = pending.Ruleset.ShortName,
                Variant = variant,
                CapturedAt = DateTimeOffset.UtcNow,
                GlobalRank = user.Statistics?.GlobalRank,
                CountryRank = user.Statistics?.CountryRank,
                PP = toDouble(user.Statistics?.PP),
                TopScores = scores.Select(createScoreSnapshot).Where(s => s.ScoreId > 0).ToList(),
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
            saveLastBriefing(payload);

            title.Text = $"Welcome back, {payload.User.Username}.";
            var capturedAt = payload.Current?.CapturedAt.ToLocalTime() ?? DateTimeOffset.Now;
            subtitle.Text = $"{payload.Ruleset.Name} - {(payload.Variant == "pp_dev" ? "latest pp-dev calculations" : "standard calculations")} - {capturedAt:MMM d, HH:mm}";

            cardFlow.Clear();

            addItem(new BriefingSectionHeader("your session", "changes from your plays, rank, and pp snapshots"));
            addItem(createRankCard(payload));
            addItem(createScoreCard(payload));
            addItem(createSyncCard(payload));

            addItem(new BriefingSectionHeader("dojo radar", "things that changed around you while you were away"));
            addItem(createMessageCard(payload));
            addItem(createRadarCard(payload));
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

        public void ShowPpDevPromotionBriefing()
        {
            if (localUser.Value?.Id <= 1)
                return;

            var user = localUser.Value;
            var ruleset = getCurrentRuleset(user);
            var state = loadSnapshotState();

            string stableSnapshotKey = $"{user.Id}:{ruleset.ShortName}:stable";
            string ppDevSnapshotKey = $"{user.Id}:{ruleset.ShortName}:pp_dev";

            if (!state.Users.TryGetValue(stableSnapshotKey, out var stableSnapshot) || !state.Users.TryGetValue(ppDevSnapshotKey, out var ppDevSnapshot))
                return;

            var payload = new BriefingPayload
            {
                User = user,
                Ruleset = ruleset,
                Variant = "pp_dev",
                Current = ppDevSnapshot,
                Previous = stableSnapshot,
                ScoreChanges = getScoreChanges(stableSnapshot, ppDevSnapshot),
                UnreadMessages = new List<BriefingMessage>(),
                RadarEvents = new List<BriefingRadarEvent>(),
                RadarFirstSnapshot = false,
                RadarTrackedCount = 0,
            };

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

        private void addItem(Drawable drawable)
        {
            int index = cardFlow.Count;

            drawable.Alpha = 0;
            drawable.Y = 10;
            cardFlow.Add(drawable);
            drawable.Delay(60 * index).FadeIn(320, Easing.OutQuint);
            drawable.Delay(60 * index).MoveToY(0, 420, Easing.OutQuint);
        }

        private BriefingCard createRankCard(BriefingPayload payload)
        {
            var previous = payload.Previous;
            var current = payload.Current;
            var accent = Color4Extensions.FromHex(@"69d7ff");

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
            var accent = payload.UnreadMessages.Count > 0 ? Color4Extensions.FromHex(@"ffd36e") : Color4Extensions.FromHex(@"69d7ff");
            string headline = payload.UnreadMessages.Count == 0
                ? "No unread chat pings"
                : $"{payload.UnreadMessages.Count} unread chat {(payload.UnreadMessages.Count == 1 ? "ping" : "pings")}";

            string detail = payload.UnreadMessages.Count == 0
                ? "Nothing urgent from joined chat channels yet."
                : string.Join("\n", payload.UnreadMessages.Take(2).Select(m => $"{m.Sender}: {trim(m.Preview, 54)}"));

            return new BriefingCard(FontAwesome.Solid.Comments, "dojo whispers", headline, detail, accent, payload.UnreadMessages.Count > 0 ? 142 : 126)
            {
                TooltipText = payload.UnreadMessages.Count == 0
                    ? "Open chat to see live channels."
                    : string.Join("\n", payload.UnreadMessages.Select(m => $"#{m.Channel} - {m.Sender}: {m.Preview}")),
            };
        }

        private BriefingCard createRadarCard(BriefingPayload payload)
        {
            var accent = payload.RadarEvents.Count > 0 ? Color4Extensions.FromHex(@"8bffcf") : Color4Extensions.FromHex(@"73b7ff");
            string headline = payload.RadarEvents.Count == 0
                ? payload.RadarFirstSnapshot ? "Dojo radar baseline synced" : "No map radar alerts"
                : $"{payload.RadarEvents.Count} tracked {(payload.RadarEvents.Count == 1 ? "shift" : "shifts")} noticed";

            string detail = payload.RadarEvents.Count == 0
                ? payload.RadarFirstSnapshot
                    ? $"Watching {payload.RadarTrackedCount} map positions from now on."
                    : $"No watched map positions moved since the last briefing ({payload.RadarTrackedCount} tracked)."
                : string.Join("\n", payload.RadarEvents.Take(2).Select(e => $"{trim(e.Title, 48)}: {e.Detail}"));

            return new BriefingCard(FontAwesome.Solid.Crosshairs, "dojo radar", headline, detail, accent, payload.RadarEvents.Count > 0 ? 142 : 126)
            {
                TooltipText = payload.RadarEvents.Count == 0
                    ? "Torii tracks your watched map leaderboard positions server-side and compares them on each briefing."
                    : string.Join("\n", payload.RadarEvents.Select(e => $"{e.Title}: {e.Detail}")),
            };
        }

        private BriefingCard createSyncCard(BriefingPayload payload)
        {
            string variantName = payload.Variant == "pp_dev" ? "pp-dev" : "stable";
            var accent = payload.Variant == "pp_dev" ? Color4Extensions.FromHex(@"73b7ff") : Color4Extensions.FromHex(@"8bffcf");

            return new BriefingCard(FontAwesome.Solid.InfoCircle, "session mode", $"{variantName} profile synced", $"Tracking {payload.Current.TopScores.Count} top plays for future briefings.", accent)
            {
                TooltipText = "This briefing is generated client-side from Torii API responses and local snapshots.",
            };
        }

        private string getRankHeadline(int? previousRank, int? currentRank, out Color4 accent)
        {
            accent = Color4Extensions.FromHex(@"69d7ff");

            if (!previousRank.HasValue || !currentRank.HasValue)
                return "Rank data is warming up";

            int delta = previousRank.Value - currentRank.Value;

            if (delta > 0)
            {
                accent = Color4Extensions.FromHex(@"8bffcf");
                return $"You gained {delta.ToString("N0", CultureInfo.InvariantCulture)} ranks";
            }

            if (delta < 0)
            {
                accent = Color4Extensions.FromHex(@"ff8f9c");
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
            this.FadeIn(220, Easing.OutQuint);
            panel.ScaleTo(0.965f).Then().ScaleTo(1, 360, Easing.OutQuint);
            panel.MoveToY(18).Then().MoveToY(0, 360, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            base.PopOut();
            this.FadeOut(180, Easing.OutQuint);
            panel.ScaleTo(0.985f, 180, Easing.OutQuint);
            panel.MoveToY(10, 180, Easing.OutQuint);
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

        private sealed class PendingBriefing
        {
            private int remainingRequests = 3;

            public readonly int RequestId;
            public readonly APIUser LocalUser;
            public readonly RulesetInfo Ruleset;
            public readonly bool UsePpDev;
            public readonly string SessionKey;

            public APIUser User;
            public List<SoloScoreInfo> TopScores;
            public ToriiBriefingRadarResponse Radar;
            public bool IsComplete => remainingRequests <= 0;

            public PendingBriefing(int requestId, string sessionKey, APIUser localUser, RulesetInfo ruleset, bool usePpDev)
            {
                RequestId = requestId;
                SessionKey = sessionKey;
                LocalUser = localUser;
                Ruleset = ruleset;
                UsePpDev = usePpDev;
            }

            public void MarkComplete() => remainingRequests--;
        }

        private sealed class BriefingPayload
        {
            public APIUser User;
            public RulesetInfo Ruleset;
            public string Variant;
            public BriefingSnapshot Current;
            public BriefingSnapshot Previous;
            public List<BriefingScoreChange> ScoreChanges;
            public List<BriefingMessage> UnreadMessages;
            public List<BriefingRadarEvent> RadarEvents;
            public bool RadarFirstSnapshot;
            public int RadarTrackedCount;
        }

        private sealed class BriefingState
        {
            [JsonProperty("users")]
            public Dictionary<string, BriefingSnapshot> Users { get; set; } = new Dictionary<string, BriefingSnapshot>();

            [JsonProperty("consumed_promotion_migrations")]
            public HashSet<string> ConsumedPromotionMigrations { get; set; } = new HashSet<string>();
        }

        private sealed class StoredBriefing
        {
            public int UserId { get; set; }
            public string Username { get; set; }
            public string Ruleset { get; set; }
            public string Variant { get; set; }
            public BriefingSnapshot Current { get; set; }
            public BriefingSnapshot Previous { get; set; }
            public List<BriefingScoreChange> ScoreChanges { get; set; } = new List<BriefingScoreChange>();
            public List<BriefingMessage> UnreadMessages { get; set; } = new List<BriefingMessage>();
            public List<BriefingRadarEvent> RadarEvents { get; set; } = new List<BriefingRadarEvent>();
            public bool RadarFirstSnapshot { get; set; }
            public int RadarTrackedCount { get; set; }
        }

        private sealed class BriefingSnapshot
        {
            public int UserId { get; set; }
            public string Username { get; set; }
            public string Ruleset { get; set; }
            public string Variant { get; set; }
            public DateTimeOffset CapturedAt { get; set; }
            public int? GlobalRank { get; set; }
            public int? CountryRank { get; set; }
            public double? PP { get; set; }
            public List<BriefingScoreSnapshot> TopScores { get; set; } = new List<BriefingScoreSnapshot>();
        }

        private sealed class BriefingScoreSnapshot
        {
            public ulong ScoreId { get; set; }
            public string Title { get; set; }
            public string Rank { get; set; }
            public double? PP { get; set; }
            public double Accuracy { get; set; }
        }

        private sealed class BriefingScoreChange
        {
            public string Title { get; set; }
            public double OldPP { get; set; }
            public double NewPP { get; set; }
            public double Delta { get; set; }
        }

        private sealed class BriefingMessage
        {
            public string Sender { get; set; }
            public string Channel { get; set; }
            public string Preview { get; set; }
        }

        private sealed class BriefingRadarEvent
        {
            public string Title { get; set; }
            public string Detail { get; set; }
            public string Severity { get; set; }
        }

        private partial class BriefingCard : CompositeDrawable, IHasTooltip
        {
            public LocalisableString TooltipText { get; set; }

            public BriefingCard(IconUsage icon, string kicker, string headline, string detail, Color4 accent, float height = 126)
            {
                RelativeSizeAxes = Axes.X;
                Height = height;
                Masking = true;
                CornerRadius = 20;
                BorderThickness = 1;
                BorderColour = accent.Opacity(0.28f);
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Colour = accent.Opacity(0.12f),
                    Radius = 14,
                };

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4Extensions.FromHex(@"18162d").Opacity(0.92f),
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientHorizontal(accent.Opacity(0.08f), Color4.Transparent),
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Width = 0.018f,
                        Colour = accent,
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Horizontal = 22, Vertical = 18 },
                        Children = new Drawable[]
                        {
                            new CircularContainer
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Size = new Vector2(58),
                                Masking = true,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = accent.Opacity(0.16f),
                                    },
                                    new SpriteIcon
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Size = new Vector2(22),
                                        Icon = icon,
                                        Colour = accent,
                                    },
                                },
                            },
                            new FillFlowContainer
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                X = 76,
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Padding = new MarginPadding { Right = 78 },
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 5),
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Text = kicker.ToUpperInvariant(),
                                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                                        Colour = accent,
                                    },
                                    new OsuSpriteText
                                    {
                                        Text = headline,
                                        Font = OsuFont.GetFont(size: 22, weight: FontWeight.Bold),
                                    },
                                    new OsuTextFlowContainer(t =>
                                    {
                                        t.Font = OsuFont.GetFont(size: 14.5f, weight: FontWeight.SemiBold);
                                        t.Colour = Color4.White.Opacity(0.62f);
                                    })
                                    {
                                        RelativeSizeAxes = Axes.X,
                                        AutoSizeAxes = Axes.Y,
                                        Text = detail,
                                    },
                                },
                            },
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                Size = new Vector2(14),
                                Icon = FontAwesome.Solid.InfoCircle,
                                Colour = Color4.White.Opacity(0.32f),
                            },
                        },
                    },
                };
            }
        }

        private partial class BriefingSectionHeader : CompositeDrawable
        {
            public BriefingSectionHeader(string title, string subtitle)
            {
                RelativeSizeAxes = Axes.X;
                Height = 34;
                Margin = new MarginPadding { Top = 4 };

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.X,
                        Height = 1,
                        Colour = Color4.White.Opacity(0.08f),
                    },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(10, 0),
                        Children = new Drawable[]
                        {
                            new BriefingPill(title, Color4Extensions.FromHex(@"69d7ff")),
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = subtitle,
                                Font = OsuFont.GetFont(size: 12.5f, weight: FontWeight.SemiBold),
                                Colour = Color4.White.Opacity(0.42f),
                            },
                        },
                    },
                };
            }
        }

        private partial class BriefingRecalcCard : CompositeDrawable, IHasTooltip
        {
            private static readonly Color4 accent_changes = Color4Extensions.FromHex(@"ff66b3");
            private static readonly Color4 accent_no_changes = Color4Extensions.FromHex(@"69d7ff");
            private static readonly Color4 color_gain = Color4Extensions.FromHex(@"8bffcf");
            private static readonly Color4 color_loss = Color4Extensions.FromHex(@"ff8f9c");

            public LocalisableString TooltipText { get; set; }

            public BriefingRecalcCard(List<BriefingScoreChange> changes)
            {
                var accent = changes.Count > 0 ? accent_changes : accent_no_changes;
                bool hasChanges = changes.Count > 0;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Masking = true;
                CornerRadius = 20;
                BorderThickness = 1;
                BorderColour = accent.Opacity(0.28f);
                EdgeEffect = new EdgeEffectParameters
                {
                    Type = EdgeEffectType.Shadow,
                    Colour = accent.Opacity(0.12f),
                    Radius = 14,
                };

                var textFlow = new FillFlowContainer
                {
                    X = 76,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Padding = new MarginPadding { Right = 30 },
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 4),
                };

                // Kicker
                textFlow.Add(new OsuSpriteText
                {
                    Text = "RECALCULATION WATCH",
                    Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                    Colour = accent,
                });

                // Headline
                string headline = hasChanges
                    ? $"{changes.Count} top {(changes.Count == 1 ? "score" : "scores")} recalculated"
                    : "No top play recalcs detected";

                textFlow.Add(new OsuSpriteText
                {
                    Text = headline,
                    Font = OsuFont.GetFont(size: 22, weight: FontWeight.Bold),
                    Margin = new MarginPadding { Bottom = 2 },
                });

                if (!hasChanges)
                {
                    textFlow.Add(new OsuSpriteText
                    {
                        Text = "Your top plays match the last briefing snapshot.",
                        Font = OsuFont.GetFont(size: 14.5f, weight: FontWeight.SemiBold),
                        Colour = Color4.White.Opacity(0.62f),
                    });
                }
                else
                {
                    // Top separator
                    textFlow.Add(new Box
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 1,
                        Colour = Color4.White.Opacity(0.10f),
                        Margin = new MarginPadding { Vertical = 3 },
                        BypassAutoSizeAxes = Axes.None,
                    });

                    // Split into top gains and top losses
                    var gains = changes.Where(c => c.Delta > 0).OrderByDescending(c => c.Delta).ToList();
                    var losses = changes.Where(c => c.Delta < 0).OrderBy(c => c.Delta).ToList();

                    // -- TOP GAINS --
                    if (gains.Count > 0)
                    {
                        textFlow.Add(new OsuSpriteText
                        {
                            Text = "TOP GAINS",
                            Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                            Colour = color_gain.Opacity(0.72f),
                            Margin = new MarginPadding { Top = 2, Bottom = 1 },
                        });

                        int gainDisplay = Math.Min(5, gains.Count);
                        for (int i = 0; i < gainDisplay; i++)
                        {
                            var change = gains[i];
                            textFlow.Add(new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(6, 0),
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Text = $"▲ {formatSignedPP(change.Delta)}",
                                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                                        Colour = color_gain,
                                        Width = 88,
                                    },
                                    new OsuSpriteText
                                    {
                                        Text = trim(change.Title, 44),
                                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                                        Colour = Color4.White.Opacity(0.78f),
                                    },
                                },
                            });
                        }

                        if (gains.Count > 5)
                        {
                            textFlow.Add(new OsuSpriteText
                            {
                                Text = $"  + {gains.Count - 5} more gains",
                                Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                                Colour = Color4.White.Opacity(0.38f),
                            });
                        }
                    }

                    // Separator between sections (only when both exist)
                    if (gains.Count > 0 && losses.Count > 0)
                    {
                        textFlow.Add(new Box
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 1,
                            Colour = Color4.White.Opacity(0.08f),
                            Margin = new MarginPadding { Vertical = 5 },
                        });
                    }

                    // -- TOP LOSSES --
                    if (losses.Count > 0)
                    {
                        textFlow.Add(new OsuSpriteText
                        {
                            Text = "TOP LOSSES",
                            Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                            Colour = color_loss.Opacity(0.72f),
                            Margin = new MarginPadding { Top = 2, Bottom = 1 },
                        });

                        int lossDisplay = Math.Min(5, losses.Count);
                        for (int i = 0; i < lossDisplay; i++)
                        {
                            var change = losses[i];
                            textFlow.Add(new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(6, 0),
                                Children = new Drawable[]
                                {
                                    new OsuSpriteText
                                    {
                                        Text = $"▼ {formatSignedPP(change.Delta)}",
                                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.Bold),
                                        Colour = color_loss,
                                        Width = 88,
                                    },
                                    new OsuSpriteText
                                    {
                                        Text = trim(change.Title, 44),
                                        Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                                        Colour = Color4.White.Opacity(0.78f),
                                    },
                                },
                            });
                        }

                        if (losses.Count > 5)
                        {
                            textFlow.Add(new OsuSpriteText
                            {
                                Text = $"  + {losses.Count - 5} more losses",
                                Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                                Colour = Color4.White.Opacity(0.38f),
                            });
                        }
                    }

                    // Highlight footer (best gain / worst loss summary)
                    if (gains.Count > 0 || losses.Count > 0)
                    {
                        textFlow.Add(new Box
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 1,
                            Colour = Color4.White.Opacity(0.10f),
                            Margin = new MarginPadding { Vertical = 3 },
                        });

                        if (gains.Count > 0)
                        {
                            var g = gains[0];
                            textFlow.Add(buildHighlightRow("▲ BEST GAIN", g.Title, formatSignedPP(g.Delta), color_gain));
                        }

                        if (losses.Count > 0)
                        {
                            var l = losses[0];
                            textFlow.Add(buildHighlightRow("▼ WORST LOSS", l.Title, formatSignedPP(l.Delta), color_loss));
                        }
                    }
                }

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4Extensions.FromHex(@"18162d").Opacity(0.92f),
                        BypassAutoSizeAxes = Axes.Y,
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = ColourInfo.GradientHorizontal(accent.Opacity(0.08f), Color4.Transparent),
                        BypassAutoSizeAxes = Axes.Y,
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Width = 0.018f,
                        Colour = accent,
                        BypassAutoSizeAxes = Axes.Y,
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding { Horizontal = 22, Vertical = 18 },
                        Children = new Drawable[]
                        {
                            new CircularContainer
                            {
                                Anchor = Anchor.TopLeft,
                                Origin = Anchor.TopLeft,
                                Y = 6,
                                Size = new Vector2(58),
                                Masking = true,
                                Children = new Drawable[]
                                {
                                    new Box
                                    {
                                        RelativeSizeAxes = Axes.Both,
                                        Colour = accent.Opacity(0.16f),
                                    },
                                    new SpriteIcon
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Size = new Vector2(22),
                                        Icon = FontAwesome.Solid.Sync,
                                        Colour = accent,
                                    },
                                },
                            },
                            textFlow,
                        },
                    },
                };
            }

            private static FillFlowContainer buildHighlightRow(string label, string title, string value, Color4 color)
            {
                return new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(6, 0),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = label,
                            Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                            Colour = color,
                            Width = 94,
                        },
                        new OsuSpriteText
                        {
                            Text = trim(title, 32),
                            Font = OsuFont.GetFont(size: 12, weight: FontWeight.SemiBold),
                            Colour = Color4.White.Opacity(0.82f),
                        },
                        new OsuSpriteText
                        {
                            Text = $"  {value}",
                            Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                            Colour = color,
                        },
                    },
                };
            }
        }

        private partial class BriefingPill : CompositeDrawable
        {
            public BriefingPill(string text, Color4 accent)
            {
                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 12;
                Padding = new MarginPadding { Horizontal = 12, Vertical = 5 };

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = accent.Opacity(0.12f),
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Text = text.ToUpperInvariant(),
                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                        Colour = accent,
                    },
                };
            }
        }
    }
}
