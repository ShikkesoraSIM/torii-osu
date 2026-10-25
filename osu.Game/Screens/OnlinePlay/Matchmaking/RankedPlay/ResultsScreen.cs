// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Scoring;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay
{
    public partial class ResultsScreen : RankedPlaySubScreen
    {
        public override LocalisableString StageHeading => "Results";

        public override bool ShowBeatmapBackground => true;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private MultiplayerClient client { get; set; } = null!;

        [Resolved]
        private ScoreManager scoreManager { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        [Resolved]
        private RankedPlayMatchInfo matchInfo { get; set; } = null!;

        [Resolved]
        private BeatmapLookupCache beatmapLookupCache { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> globalBeatmap { get; set; } = null!;

        [Resolved]
        private IBindable<RulesetInfo> globalRuleset { get; set; } = null!;

        [Resolved]
        private IBindable<IReadOnlyList<Mod>> globalMods { get; set; } = null!;

        private LoadingSpinner loadingSpinner = null!;
        private MainPanel? mainPanel;
        private IndexPlaylistScoresRequest? scoreRequest;
        private bool exited;

        [BackgroundDependencyLoader]
        private void load()
        {
            CornerPieceVisibility.Value = Visibility.Hidden;

            AddRangeInternal(new Drawable[]
            {
                loadingSpinner = new LoadingSpinner
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre
                },
            });
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            loadingSpinner.Show();

            fetchFinalScores().FireAndForget();
        }

        private async Task fetchFinalScores()
        {
            try
            {
                if (client.Room == null)
                    return;

                // la sala puede avanzar mientras esperamos los scores.
                var roomState = matchInfo.RoomState;
                var beatmapInfo = globalBeatmap.Value.BeatmapInfo;
                var ruleset = globalRuleset.Value;
                var mods = globalMods.Value.ToArray();
                int localUserId = api.LocalUser.Value.OnlineID;
                int opponentId = roomState.Users.Keys.Single(it => it != localUserId);
                var playerDamageInfo = roomState.Users[localUserId].DamageInfo!;
                var opponentDamageInfo = roomState.Users[opponentId].DamageInfo!;
                int roundNumber = roomState.CurrentRound;

                TaskCompletionSource<List<MultiplayerScore>> scoreLookup = new TaskCompletionSource<List<MultiplayerScore>>(TaskCreationOptions.RunContinuationsAsynchronously);

                var request = scoreRequest = new IndexPlaylistScoresRequest(client.Room.RoomID, client.Room.Settings.PlaylistItemId);

                request.Success += req => scoreLookup.TrySetResult(req.Scores);
                request.Failure += error => scoreLookup.TrySetException(error);

                api.Queue(request);

                List<MultiplayerScore> apiScores = await scoreLookup.Task.ConfigureAwait(false);

                if (exited)
                    return;

                ScoreInfo[] scores = apiScores.Select(s => s.CreateScoreInfo(scoreManager, rulesets, beatmapInfo)).ToArray();

                Debug.Assert(scores.Length <= 2);

                ScoreInfo playerScore = scores.SingleOrDefault(s => s.UserID == localUserId) ?? new ScoreInfo
                {
                    Rank = ScoreRank.F,
                    Ruleset = ruleset,
                    User = new APIUser { Id = localUserId }
                };

                ScoreInfo opponentScore = scores.SingleOrDefault(s => s.UserID == opponentId) ?? new ScoreInfo
                {
                    Rank = ScoreRank.F,
                    Ruleset = ruleset,
                    User = new APIUser { Id = opponentId }
                };

                // Should complete instantaneously due to prior lookups.
                APIBeatmap beatmap = (await beatmapLookupCache.GetBeatmapAsync(beatmapInfo.OnlineID).ConfigureAwait(false))!;

                Schedule(() =>
                {
                    if (exited)
                        return;

                    LoadComponentAsync(new MainPanel
                    {
                        RelativeSizeAxes = Axes.Both,
                        // A little bit of room for the countdown timer...
                        Margin = new MarginPadding { Top = 45 },
                        PlayerScore = playerScore,
                        OpponentScore = opponentScore,
                        PlayerDamageInfo = playerDamageInfo,
                        OpponentDamageInfo = opponentDamageInfo,
                        RoundNumber = roundNumber,
                        Beatmap = beatmap,
                        Mods = mods,
                    }, loaded =>
                    {
                        if (exited)
                        {
                            loaded.Dispose();
                            return;
                        }

                        AddInternal(loaded);
                        mainPanel = loaded;
                    });
                });
            }
            catch (OperationCanceledException) when (exited)
            {
            }
            catch (Exception e)
            {
                Logger.Error(e, "Failed to load scores for playlist item.");
                throw;
            }
            finally
            {
                Scheduler.Add(() => loadingSpinner.Hide());
            }
        }

        public override void OnExiting(RankedPlaySubScreen? next)
        {
            cancelScoreLookup();
            mainPanel?.StopAllSamples();
            base.OnExiting(next);
        }

        private void cancelScoreLookup()
        {
            exited = true;
            scoreRequest?.Cancel();
        }

        protected override void Dispose(bool isDisposing)
        {
            cancelScoreLookup();
            base.Dispose(isDisposing);
        }
    }
}
