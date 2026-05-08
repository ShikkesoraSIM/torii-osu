// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.Leaderboards;
using osu.Game.Rulesets;
using osu.Game.Scoring;
using osuTK;
using Realms;

namespace osu.Game.Screens.SelectV2
{
    public partial class PanelLocalRankDisplay : CompositeDrawable
    {
        private BeatmapInfo? beatmap;

        public BeatmapInfo? Beatmap
        {
            get => beatmap;
            set
            {
                beatmap = value;

                if (IsLoaded)
                    updateSubscription();
            }
        }

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        private IDisposable? scoreSubscription;

        private readonly UpdateableRank updateable;

        public bool HasRank => updateable.Rank != null;

        public PanelLocalRankDisplay(BeatmapInfo? beatmap = null)
        {
            AutoSizeAxes = Axes.Both;

            InternalChild = updateable = new UpdateableRank(animate: false)
            {
                Size = new Vector2(40, 20),
                Alpha = 0,
            };

            Beatmap = beatmap;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ruleset.BindValueChanged(_ => updateSubscription(), true);
        }

        private void updateSubscription()
        {
            scoreSubscription?.Dispose();
            setRankFromScore(null);

            if (beatmap == null)
                return;

            // Upstream perf fix (ppy/osu#37666). The original implementation filtered on
            // linked Realm objects (`ScoreInfo.BeatmapInfo.ID` + `ScoreInfo.Ruleset.ShortName`),
            // which Realm resolves by walking each candidate row's linked objects — extremely
            // expensive when scrolling a song select with thousands of scores in the DB. The
            // new pattern hits Realm with a flat field comparison (BeatmapHash) only, then
            // narrows by user/ruleset in .NET on the much smaller candidate set. Net savings
            // on the scheduler thread: 2-10ms per scroll tick on large libraries.
            //
            // Note: upstream also adds [Indexed] on ScoreInfo.BeatmapHash for an extra speed
            // bump. We deliberately don't (would require Realm schema bump 51→52, which would
            // break vanilla osu! lazer's ability to open shared realm folders — see
            // ScoreInfo.cs comment + RealmAccess.cs schema_version notes). The filter rewrite
            // alone — which is the main win per the PR — applies here as-is.
            scoreSubscription = realm.RegisterForNotifications(r =>
                    r.All<ScoreInfo>().Where(s => s.BeatmapHash == beatmap.Hash && !s.DeletePending),
                localScoresChanged);
        }

        private void localScoresChanged(IRealmCollection<ScoreInfo> sender, ChangeSet? changes)
        {
            // This subscription may fire from changes to linked beatmaps, which we don't care about.
            // It's currently not possible for a score to be modified after insertion, so we can safely ignore callbacks with only modifications.
            if (changes?.HasCollectionChanges() == false)
                return;

            ScoreInfo? topScore = sender
                                  // doing these post realm filter is most efficient.
                                  .Where(s => s.UserID == api.LocalUser.Value.Id || s.UserID <= 1)
                                  .Where(s => s.Ruleset.ShortName == ruleset.Value.ShortName)
                                  .MaxBy(info => (info.TotalScore, -info.Date.UtcDateTime.Ticks));

            setRankFromScore(topScore);
        }

        private void setRankFromScore(ScoreInfo? topScore)
        {
            updateable.Rank = topScore?.Rank;
            updateable.Alpha = topScore != null ? 1 : 0;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            scoreSubscription?.Dispose();
        }
    }
}
