// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Framework.Threading;
using osu.Game.Beatmaps.Drawables.Cards;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Beatmaps;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Resources.Localisation.Web;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.BeatmapListing
{
    public partial class BeatmapListingFilterControl : CompositeDrawable
    {
        /// <summary>
        /// Fired when a search finishes.
        /// </summary>
        public Action<SearchResult> SearchFinished;

        /// <summary>
        /// Fired when search criteria change.
        /// </summary>
        public Action SearchStarted;

        /// <summary>
        /// Any time the search text box receives key events (even while masked).
        /// </summary>
        public Action TypingStarted;

        /// <summary>
        /// True when pagination has reached the end of available results.
        /// </summary>
        private bool noMoreResults;

        /// <summary>
        /// Whether there are more pages available from the API.
        /// False when the cursor is exhausted. Used by the overlay to decide
        /// whether to show a "not found" dead-end or fetch the next page.
        /// </summary>
        public bool HasMorePages => !noMoreResults;

        private HashSet<int> downloadedBeatmapSetIds = new HashSet<int>();

        /// <summary>
        /// The current page fetched of results (zero index).
        /// </summary>
        public int CurrentPage { get; private set; }

        /// <summary>
        /// The currently selected <see cref="BeatmapCardSize"/>.
        /// </summary>
        public IBindable<BeatmapCardSize> CardSize => cardSize;

        private readonly Bindable<BeatmapCardSize> cardSize = new Bindable<BeatmapCardSize>();

        private readonly BeatmapListingSearchControl searchControl;
        private readonly BeatmapListingSortTabControl sortControl;
        private readonly Box sortControlBackground;

        private ScheduledDelegate queryChangedDebounce;

        private SearchBeatmapSetsRequest getSetsRequest;
        private SearchBeatmapSetsResponse lastResponse;

        [Resolved]
        private IAPIProvider api { get; set; }

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        private IBindable<APIUser> apiUser;

        public BeatmapListingFilterControl()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 10),
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Masking = true,
                        EdgeEffect = new EdgeEffectParameters
                        {
                            Colour = Color4.Black.Opacity(0.25f),
                            Type = EdgeEffectType.Shadow,
                            Radius = 3,
                            Offset = new Vector2(0f, 1f),
                        },
                        Child = searchControl = new BeatmapListingSearchControl
                        {
                            TypingStarted = () => TypingStarted?.Invoke()
                        }
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 40,
                        Children = new Drawable[]
                        {
                            sortControlBackground = new Box
                            {
                                RelativeSizeAxes = Axes.Both
                            },
                            sortControl = new BeatmapListingSortTabControl
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Margin = new MarginPadding { Left = 20 }
                            },
                            new BeatmapListingCardSizeTabControl
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                Margin = new MarginPadding { Right = 20 },
                                Current = { BindTarget = CardSize }
                            }
                        }
                    }
                }
            };
        }

        [Resolved]
        private OsuConfigManager config { get; set; }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider, IAPIProvider api)
        {
            sortControlBackground.Colour = colourProvider.Background4;
        }

        public void Search(string query)
        => Schedule(() => searchControl.Query.Value = query);

        public void FilterGenre(SearchGenre genre)
        => Schedule(() => searchControl.Genre.Value = genre);

        public void FilterLanguage(SearchLanguage language)
        => Schedule(() => searchControl.Language.Value = language);

        protected override void LoadComplete()
        {
            base.LoadComplete();

            config.BindWith(OsuSetting.BeatmapListingCardSize, cardSize);

            searchControl.Query.BindValueChanged(_ =>
            {
                resetSortControl();
                queueUpdateSearch(true);
            });

            searchControl.Category.BindValueChanged(_ =>
            {
                resetSortControl();
                queueUpdateSearch();
            });

            searchControl.General.CollectionChanged += (_, _) => queueUpdateSearch();
            searchControl.Ruleset.BindValueChanged(_ => queueUpdateSearch());
            searchControl.Genre.BindValueChanged(_ => queueUpdateSearch());
            searchControl.Language.BindValueChanged(_ => queueUpdateSearch());
            searchControl.Extra.CollectionChanged += (_, _) => queueUpdateSearch();
            searchControl.Ranks.CollectionChanged += (_, _) => queueUpdateSearch();
            searchControl.Played.BindValueChanged(_ => queueUpdateSearch());
            searchControl.Downloaded.BindValueChanged(_ =>
            {
                refreshDownloadedIds();
                queueUpdateSearch();
            });
            searchControl.ExplicitContent.BindValueChanged(_ => queueUpdateSearch());

            sortControl.Current.BindValueChanged(_ => queueUpdateSearch());
            sortControl.SortDirection.BindValueChanged(_ => queueUpdateSearch());

            apiUser = api.LocalUser.GetBoundCopy();
            apiUser.BindValueChanged(_ => queueUpdateSearch());
        }

        public void TakeFocus() => searchControl.TakeFocus();

        /// <summary>
        /// Fetch the next page of results. May result in a no-op if a fetch is already in progress, or if there are no results left.
        /// </summary>
        public void FetchNextPage()
        {
            // there may be no results left.
            if (noMoreResults)
                return;

            // there may already be an active request.
            if (getSetsRequest != null)
                return;

            if (lastResponse != null)
                CurrentPage++;

            performRequest();
        }

        private void resetSortControl() => sortControl.Reset(searchControl.Category.Value, !string.IsNullOrEmpty(searchControl.Query.Value));

        private void queueUpdateSearch(bool queryTextChanged = false)
        {
            SearchStarted?.Invoke();

            resetSearch();

            if (!api.IsLoggedIn)
                return;

            queryChangedDebounce = Scheduler.AddDelayed(() =>
            {
                resetSearch();
                FetchNextPage();
            }, queryTextChanged ? 500 : 100);
        }

        /// <summary>
        /// Refreshes the cached set of locally-downloaded beatmap set IDs.
        /// Called when the Downloaded filter tab changes so the data stays
        /// current if maps are imported or deleted while the listing is open.
        /// </summary>
        private void refreshDownloadedIds()
        {
            if (realm == null) return;

            downloadedBeatmapSetIds = realm.Run(r =>
            {
                var sets = r.All<BeatmapSetInfo>()
                .Where(s => !s.DeletePending && s.OnlineID > 0)
                .AsEnumerable()
                .ToList();

                var ids = new HashSet<int>();
                foreach (var s in sets)
                    ids.Add(s.OnlineID);
                return ids;
            });
        }

        /// <summary>
        /// Queries the local Realm database for downloaded beatmaps and converts them
        /// to <see cref="APIBeatmapSet"/> objects for display. Used when the
        /// "Downloaded" filter is active — no API calls needed.
        /// </summary>
        /// <remarks>
        /// The following server-side filters have no local equivalent and are
        /// silently ignored when "Downloaded" is active:
        ///   - General (Recommended, Converts, Follows, Spotlights, Featured Artists)
        ///   - Genre
        ///   - Language
        ///   - Extra (Video, Storyboard)
        ///   - Ranks (score rank achieved)
        ///   - Explicit Content
        ///
        /// These filters rely on data only available from the osu! API and are
        /// not stored in the local beatmap database. Only Status (Category),
        /// Ruleset, Played, and text search are applied to local results.
        /// </remarks>
        private void performLocalSearch()
        {
            var query = searchControl.Query.Value ?? string.Empty;
            var category = searchControl.Category.Value;
            var ruleset = searchControl.Ruleset.Value;
            var played = searchControl.Played.Value;
            var sortCriteria = sortControl.Current.Value;
            var sortDirection = sortControl.SortDirection.Value;

            // Run the heavy realm query + filtering + projection off the update thread
            // to avoid hitching frames on large libraries.
            Task.Run(() =>
            {
                var apiSets = realm.Run(r =>
                {
                    var sets = r.All<BeatmapSetInfo>()
                    .Where(s => !s.DeletePending && s.OnlineID > 0)
                    .AsEnumerable()
                    .ToList();

                    // --- Filter: Status (Category) ---
                    if (category == SearchCategory.Leaderboard || category == SearchCategory.Ranked)
                        sets = sets.Where(s =>
                        s.Status == BeatmapOnlineStatus.Ranked ||
                        s.Status == BeatmapOnlineStatus.Approved).ToList();
                    else if (category == SearchCategory.Qualified)
                        sets = sets.Where(s => s.Status == BeatmapOnlineStatus.Qualified).ToList();
                    else if (category == SearchCategory.Loved)
                        sets = sets.Where(s => s.Status == BeatmapOnlineStatus.Loved).ToList();
                    else if (category == SearchCategory.Pending || category == SearchCategory.Wip)
                        sets = sets.Where(s =>
                        s.Status == BeatmapOnlineStatus.Pending ||
                        s.Status == BeatmapOnlineStatus.WIP).ToList();
                    else if (category == SearchCategory.Graveyard)
                        sets = sets.Where(s => s.Status == BeatmapOnlineStatus.Graveyard).ToList();

                    // --- Filter: Ruleset ---
                    if (ruleset.OnlineID >= 0)
                    {
                        sets = sets.Where(s =>
                        s.Beatmaps.Any(b => b.Ruleset.OnlineID == ruleset.OnlineID)
                        ).ToList();
                    }

                    // --- Filter: Played / Unplayed ---
                    if (played == SearchPlayed.Played)
                        sets = sets.Where(s => s.Beatmaps.Any(b => b.LastPlayed != null)).ToList();
                    else if (played == SearchPlayed.Unplayed)
                        sets = sets.Where(s => s.Beatmaps.All(b => b.LastPlayed == null)).ToList();

                    // --- Filter: Search text ---
                    if (!string.IsNullOrWhiteSpace(query))
                    {
                        var terms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        sets = sets.Where(s =>
                        terms.All(t =>
                        s.Beatmaps.Any(b =>
                        (b.Metadata.Title ?? "").Contains(t, StringComparison.OrdinalIgnoreCase) ||
                        (b.Metadata.TitleUnicode ?? "").Contains(t, StringComparison.OrdinalIgnoreCase) ||
                        (b.Metadata.Artist ?? "").Contains(t, StringComparison.OrdinalIgnoreCase) ||
                        (b.Metadata.ArtistUnicode ?? "").Contains(t, StringComparison.OrdinalIgnoreCase) ||
                        (b.Metadata.Author.Username ?? "").Contains(t, StringComparison.OrdinalIgnoreCase) ||
                        (b.Metadata.Source ?? "").Contains(t, StringComparison.OrdinalIgnoreCase) ||
                        (b.Metadata.Tags ?? "").Contains(t, StringComparison.OrdinalIgnoreCase) ||
                        b.OnlineID.ToString() == t
                        )
                        )
                        ).ToList();
                    }

                    // Convert BeatmapSetInfo → APIBeatmapSet inside realm.Run
                    return sets.Select(s =>
                    {
                        var first = s.Beatmaps.FirstOrDefault();
                        return new APIBeatmapSet
                        {
                            OnlineID = s.OnlineID,
                            Status = s.Status,
                            Title = first?.Metadata.Title ?? "",
                            TitleUnicode = first?.Metadata.TitleUnicode ?? "",
                            Artist = first?.Metadata.Artist ?? "",
                            ArtistUnicode = first?.Metadata.ArtistUnicode ?? "",
                            AuthorString = first?.Metadata.Author.Username ?? "",
                            AuthorID = first?.Metadata.Author.OnlineID ?? 0,
                            Source = first?.Metadata.Source ?? "",
                            Tags = first?.Metadata.Tags ?? "",
                            BPM = first?.BPM ?? 0,
                            Covers = new BeatmapSetOnlineCovers
                            {
                                CoverLowRes = $"https://assets.ppy.sh/beatmaps/{s.OnlineID}/covers/cover.jpg",
                                Cover = $"https://assets.ppy.sh/beatmaps/{s.OnlineID}/covers/cover@2x.jpg",
                                CardLowRes = $"https://assets.ppy.sh/beatmaps/{s.OnlineID}/covers/card.jpg",
                                Card = $"https://assets.ppy.sh/beatmaps/{s.OnlineID}/covers/card@2x.jpg",
                                ListLowRes = $"https://assets.ppy.sh/beatmaps/{s.OnlineID}/covers/list.jpg",
                                List = $"https://assets.ppy.sh/beatmaps/{s.OnlineID}/covers/list@2x.jpg",
                            },
                            Beatmaps = s.Beatmaps.Select(b => new APIBeatmap
                            {
                                DifficultyName = b.DifficultyName,
                                StarRating = b.StarRating,
                                OnlineID = b.OnlineID,
                                Length = b.Length,
                                BPM = b.BPM,
                                CircleSize = b.Difficulty.CircleSize,
                                ApproachRate = b.Difficulty.ApproachRate,
                                OverallDifficulty = b.Difficulty.OverallDifficulty,
                                DrainRate = b.Difficulty.DrainRate,
                                RulesetID = b.Ruleset.OnlineID,
                                Status = b.Status,
                            }).ToArray(),
                        };
                    }).ToList();
                });

                // --- Sort ---
                bool descending = sortDirection == SortDirection.Descending;
                apiSets = sortCriteria switch
                {
                    SortCriteria.Title => descending
                    ? apiSets.OrderByDescending(s => s.Title).ToList()
                    : apiSets.OrderBy(s => s.Title).ToList(),
                     SortCriteria.Artist => descending
                     ? apiSets.OrderByDescending(s => s.Artist).ToList()
                     : apiSets.OrderBy(s => s.Artist).ToList(),
                     SortCriteria.Difficulty => descending
                     ? apiSets.OrderByDescending(s => s.Beatmaps.Any() ? s.Beatmaps.Max(b => (double?)b.StarRating) ?? 0 : 0).ToList()
                     : apiSets.OrderBy(s => s.Beatmaps.Any() ? s.Beatmaps.Max(b => (double?)b.StarRating) ?? 0 : 0).ToList(),
                     SortCriteria.Updated => descending
                     ? apiSets.OrderByDescending(s => s.LastUpdated ?? DateTimeOffset.MinValue).ToList()
                     : apiSets.OrderBy(s => s.LastUpdated ?? DateTimeOffset.MinValue).ToList(),
                     _ => apiSets.OrderByDescending(s => s.Ranked).ToList(),
                };

                // Marshal results back to the update thread
                Scheduler.Add(() =>
                {
                    noMoreResults = true;

                    if (apiSets.Count > 0)
                        searchControl.BeatmapSet = apiSets.First();

                    SearchFinished?.Invoke(SearchResult.ResultsReturned(apiSets));
                });
            });
        }

        private void performRequest()
        {
            // "Downloaded" filter: use local database only, no API calls needed.
            if (searchControl.Downloaded.Value == SearchDownloaded.Downloaded)
            {
                performLocalSearch();
                return;
            }

            var downloadedIds = downloadedBeatmapSetIds;

            getSetsRequest = new SearchBeatmapSetsRequest(
                searchControl.Query.Value,
                searchControl.Ruleset.Value,
                lastResponse?.Cursor,
                searchControl.General,
                searchControl.Category.Value,
                sortControl.Current.Value,
                sortControl.SortDirection.Value,
                searchControl.Genre.Value,
                searchControl.Language.Value,
                searchControl.Extra,
                searchControl.Ranks,
                searchControl.Played.Value,
                searchControl.ExplicitContent.Value);

            getSetsRequest.Success += response =>
            {
                var sets = response.BeatmapSets.ToList();

                // Client-side filter: Not Downloaded
                if (searchControl.Downloaded.Value == SearchDownloaded.NotDownloaded)
                {
                    sets = sets.Where(s => !downloadedIds.Contains(s.OnlineID)).ToList();

                    // If every result on this page was already downloaded but more pages
                    // exist, skip to the next one. This only triggers when a page is
                    // completely empty, which is a rare edge case — the API load is
                    // equivalent to what the user would generate by manually paginating.
                    if (sets.Count == 0 && response.Cursor != null)
                    {
                        lastResponse = response;
                        getSetsRequest = null;
                        performRequest();
                        return;
                    }
                }

                if (response.Cursor == null)
                    noMoreResults = true;

                if (CurrentPage == 0)
                    searchControl.BeatmapSet = sets.FirstOrDefault();

                lastResponse = response;
                getSetsRequest = null;

                if (!api.LocalUser.Value.IsSupporter)
                {
                    List<LocalisableString> filters = new List<LocalisableString>();

                    if (searchControl.Played.Value != SearchPlayed.Any)
                        filters.Add(BeatmapsStrings.ListingSearchFiltersPlayed);

                    if (searchControl.Ranks.Any())
                        filters.Add(BeatmapsStrings.ListingSearchFiltersRank);

                    if (filters.Any())
                    {
                        var supporterOnlyFilters = SearchResult.SupporterOnlyFilters(filters);
                        SearchFinished?.Invoke(supporterOnlyFilters);
                        return;
                    }
                }

                var resultsReturned = SearchResult.ResultsReturned(sets);
                SearchFinished?.Invoke(resultsReturned);
            };

            api.Queue(getSetsRequest);
        }

        private void resetSearch()
        {
            noMoreResults = false;
            CurrentPage = 0;

            lastResponse = null;

            getSetsRequest?.Cancel();
            getSetsRequest = null;

            queryChangedDebounce?.Cancel();
        }

        protected override void Dispose(bool isDisposing)
        {
            resetSearch();

            base.Dispose(isDisposing);
        }

        /// <summary>
        /// Indicates the type of result of a user-requested beatmap search.
        /// </summary>
        public enum SearchResultType
        {
            /// <summary>
            /// Actual results have been returned from API.
            /// </summary>
            ResultsReturned,

            /// <summary>
            /// The user is not a supporter, but used supporter-only search filters.
            /// </summary>
            SupporterOnlyFilters
        }

        /// <summary>
        /// Describes the result of a user-requested beatmap search.
        /// </summary>
        public struct SearchResult
        {
            public SearchResultType Type { get; private set; }

            /// <summary>
            /// Contains the beatmap sets returned from API.
            /// Valid for read if and only if <see cref="Type"/> is <see cref="SearchResultType.ResultsReturned"/>.
            /// </summary>
            public List<APIBeatmapSet> Results { get; private set; }

            /// <summary>
            /// Contains the names of supporter-only filters requested by the user.
            /// Valid for read if and only if <see cref="Type"/> is <see cref="SearchResultType.SupporterOnlyFilters"/>.
            /// </summary>
            public List<LocalisableString> SupporterOnlyFiltersUsed { get; private set; }

            public static SearchResult ResultsReturned(List<APIBeatmapSet> results) => new SearchResult
            {
                Type = SearchResultType.ResultsReturned,
                Results = results,
            };

            public static SearchResult SupporterOnlyFilters(List<LocalisableString> filters) => new SearchResult
            {
                Type = SearchResultType.SupporterOnlyFilters,
                SupporterOnlyFiltersUsed = filters
            };
        }
    }
}
