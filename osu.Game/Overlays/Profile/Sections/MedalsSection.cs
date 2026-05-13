// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.Profile.Sections.Medals;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Users;
using osuTK;

namespace osu.Game.Overlays.Profile.Sections
{
    public partial class MedalsSection : ProfileSection
    {
        public override LocalisableString Title => UsersStrings.ShowExtraMedalsTitle;

        public override string Identifier => @"medals";

        public MedalsSection()
        {
            Children = new Drawable[]
            {
                new MedalsContainer(User)
            };
        }

        private partial class MedalsContainer : FillFlowContainer
        {
            private readonly Bindable<UserProfileData?> user;

            [Resolved]
            private IAPIProvider api { get; set; } = null!;

            public MedalsContainer(Bindable<UserProfileData?> user)
            {
                this.user = user;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Direction = FillDirection.Vertical;
                Spacing = new Vector2(0, 24);
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                user.BindValueChanged(_ => updateMedals(), true);
            }

            private void updateMedals()
            {
                Clear();

                var profileData = user.Value;
                var userAchievements = profileData?.User?.Achievements ?? Array.Empty<APIUserAchievement>();

                // Get medals database
                var medalsDb = MedalDatabase.GetMedals();
                var allMedalIds = medalsDb.Keys.OrderBy(k => k).ToList();

                // Create a map of achieved medals with their achievement dates
                var achievedMap = new Dictionary<int, APIUserAchievement>();
                foreach (var achievement in userAchievements)
                {
                    achievedMap[achievement.ID] = achievement;
                }

                // Get latest 3 achieved medals (most recent first)
                var latestAchieved = userAchievements
                    .OrderByDescending(a => a.AchievedAt)
                    .Take(3)
                    .ToList();

                // Add "Latest" section if there are any achievements (prioritize this)
                if (latestAchieved.Count > 0)
                {
                    Add(new LatestMedalsSection(latestAchieved, medalsDb));
                }

                // Group all medals by category, separating achieved and unachieved
                var categories = new Dictionary<string, (List<(int id, Medal medal, bool achieved, string? achievedAt)> achieved, List<(int id, Medal medal, bool achieved, string? achievedAt)> unachieved)>();
                var categoryOrder = new[] { "Skill & Dedication", "Combo Milestones", "Mod Introduction", "Hush-Hush" };

                foreach (var id in allMedalIds)
                {
                    if (medalsDb.TryGetValue(id, out var medal))
                    {
                        var category = MedalDatabase.GetCategory(id);
                        if (!categories.ContainsKey(category))
                            categories[category] = (new List<(int, Medal, bool, string?)>(), new List<(int, Medal, bool, string?)>());

                        var isAchieved = achievedMap.TryGetValue(id, out var achievement);
                        var achievedAt = isAchieved ? achievement.AchievedAt.ToString("d") : null;
                        var medalData = (id, medal, isAchieved, achievedAt);

                        if (isAchieved)
                            categories[category].achieved.Add(medalData);
                        else
                            categories[category].unachieved.Add(medalData);
                    }
                }

                // Add category sections in order - achieved medals first, then unachieved (deferred)
                foreach (var categoryName in categoryOrder)
                {
                    if (categories.TryGetValue(categoryName, out var categoryMedals))
                    {
                        // Create section with achieved medals first (load immediately)
                        var allMedals = new List<(int id, Medal medal, bool achieved, string? achievedAt)>();
                        allMedals.AddRange(categoryMedals.achieved);
                        allMedals.AddRange(categoryMedals.unachieved);

                        var section = new MedalCategorySection(categoryName, allMedals, categoryMedals.achieved.Count);
                        Add(section);
                    }
                }
            }
        }

        private partial class LatestMedalsSection : CompositeDrawable
        {
            private readonly List<APIUserAchievement> latestAchievements;
            private readonly IReadOnlyDictionary<int, Medal> medalsDb;

            public LatestMedalsSection(List<APIUserAchievement> latestAchievements, IReadOnlyDictionary<int, Medal> medalsDb)
            {
                this.latestAchievements = latestAchievements;
                this.medalsDb = medalsDb;
            }

            [BackgroundDependencyLoader]
            private void load(OsuColour colours)
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;

                var medalDisplays = new List<Drawable>();

                foreach (var achievement in latestAchievements)
                {
                    if (medalsDb.TryGetValue(achievement.ID, out var medal))
                    {
                        medalDisplays.Add(new Container
                        {
                            Width = 64,
                            Height = 64,
                            Padding = new MarginPadding(4),
                            Child = new DrawableMedalDisplay(medal, achieved: true, achievedAt: achievement.AchievedAt.ToString("d"))
                            {
                                RelativeSizeAxes = Axes.Both,
                            },
                        });
                    }
                }

                InternalChild = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(12),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = "LATEST",
                            Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                            Colour = colours.Gray9,
                            Margin = new MarginPadding { Bottom = 8 }
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Spacing = new Vector2(12),
                            Children = medalDisplays
                        }
                    }
                };
            }
        }

        private partial class MedalCategorySection : CompositeDrawable
        {
            private readonly string categoryName;
            private readonly List<(int id, Medal medal, bool achieved, string? achievedAt)> medals;
            private GridContainer medalContainer = null!;

            public MedalCategorySection(string categoryName, List<(int id, Medal medal, bool achieved, string? achievedAt)> medals, int achievedCount)
            {
                this.categoryName = categoryName;
                this.medals = medals;
            }

            [BackgroundDependencyLoader]
            private void load(OsuColour colours)
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;

                // Separate achieved and unachieved medals for prioritized loading
                var achievedMedals = medals.Where(m => m.achieved).ToList();
                var unachievedMedals = medals.Where(m => !m.achieved).ToList();

                // Create achieved medals first (these load textures immediately)
                var achievedDisplays = achievedMedals.Select(m => new Container
                {
                    Width = 64,
                    Height = 64,
                    Padding = new MarginPadding(4),
                    Child = new DrawableMedalDisplay(m.medal, achieved: true, achievedAt: m.achievedAt)
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                }).Cast<Drawable>().ToList();

                // Calculate grid dimensions: 6 columns per row (matching the image layout)
                const int columnsPerRow = 6;
                int totalRows = (int)Math.Ceiling((double)medals.Count / columnsPerRow);

                // Create grid content
                var allMedalDisplays = new List<Drawable>(achievedDisplays);

                // Create unachieved medal containers (textures will load deferred)
                var unachievedDisplays = unachievedMedals.Select(m => new Container
                {
                    Width = 64,
                    Height = 64,
                    Padding = new MarginPadding(4),
                    Child = new DrawableMedalDisplay(m.medal, achieved: false, achievedAt: null)
                    {
                        RelativeSizeAxes = Axes.Both,
                    },
                }).Cast<Drawable>().ToList();

                // Combine all medals (achieved first, then unachieved)
                allMedalDisplays.AddRange(unachievedDisplays);

                // Create grid rows
                var gridRows = new List<Drawable[]>();
                for (int row = 0; row < totalRows; row++)
                {
                    var rowMedals = allMedalDisplays.Skip(row * columnsPerRow).Take(columnsPerRow).ToArray();
                    // Pad row if needed
                    if (rowMedals.Length < columnsPerRow)
                    {
                        var paddedRow = new Drawable[columnsPerRow];
                        Array.Copy(rowMedals, paddedRow, rowMedals.Length);
                        for (int i = rowMedals.Length; i < columnsPerRow; i++)
                        {
                            paddedRow[i] = new Container { Width = 64, Height = 64 };
                        }
                        gridRows.Add(paddedRow);
                    }
                    else
                    {
                        gridRows.Add(rowMedals);
                    }
                }

                medalContainer = new GridContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    ColumnDimensions = Enumerable.Range(0, columnsPerRow)
                        .Select(_ => new Dimension(GridSizeMode.Absolute, 64))
                        .ToArray(),
                    RowDimensions = Enumerable.Range(0, totalRows)
                        .Select(_ => new Dimension(GridSizeMode.Absolute, 64))
                        .ToArray(),
                    Content = gridRows.ToArray()
                };

                InternalChild = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(12),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = categoryName.ToUpperInvariant(),
                            Font = OsuFont.GetFont(size: 12, weight: FontWeight.Bold),
                            Colour = colours.Gray6,
                            Margin = new MarginPadding { Bottom = 8 }
                        },
                        medalContainer
                    }
                };
            }
        }
    }
}
