// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Testing;
using osu.Game.Database;
using osu.Game.Skinning;
using osu.Game.Skinning.Triangles;
using osu.Game.Tests.Visual;

namespace osu.Game.Tests.Skins
{
    [HeadlessTest]
    public partial class TestSceneSkinPinning : OsuTestScene
    {
        [Resolved]
        private SkinManager skins { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        // Each test seeds its own uniquely-named user skins to avoid cross-test pollution
        // (the realm persists across tests within an OsuTestScene-derived class).
        private string testTag = null!;
        private List<Guid> seededIds = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            // The realm carries over between tests in this fixture, so reset any pin state
            // a previous case may have left behind before seeding this case's skins. Without
            // this, ordering / cycle assertions can pick up stale pinned items and intermittently fail.
            AddStep("clear pin state on every skin", () => realm.Write(r =>
            {
                foreach (var s in r.All<SkinInfo>())
                    s.Pinned = false;
            }));

            AddStep("seed test skins", () =>
            {
                testTag = $"pinning-{Guid.NewGuid():N}";
                seededIds = new List<Guid>();

                realm.Write(r =>
                {
                    foreach (string suffix in new[] { "alpha", "bravo", "charlie" })
                    {
                        var info = new SkinInfo(name: $"{testTag}-{suffix}", creator: "test", instantiationInfo: typeof(TrianglesSkin).AssemblyQualifiedName);
                        r.Add(info);
                        seededIds.Add(info.ID);
                    }
                });
            });
        }

        [Test]
        public void TestNewSkinIsUnpinnedByDefault()
        {
            AddAssert("seeded skins start unpinned", () => realm.Run(r => seededIds.All(id => !r.Find<SkinInfo>(id)!.Pinned)));
        }

        [Test]
        public void TestTogglePinnedFlipsState()
        {
            AddStep("toggle pin on bravo", () =>
            {
                var live = realm.Run(r => r.Find<SkinInfo>(seededIds[1])!.ToLive(realm));
                skins.TogglePinned(live);
            });
            AddAssert("bravo is pinned", () => realm.Run(r => r.Find<SkinInfo>(seededIds[1])!.Pinned));

            AddStep("toggle pin again on bravo", () =>
            {
                var live = realm.Run(r => r.Find<SkinInfo>(seededIds[1])!.ToLive(realm));
                skins.TogglePinned(live);
            });
            AddAssert("bravo is no longer pinned", () => realm.Run(r => !r.Find<SkinInfo>(seededIds[1])!.Pinned));
        }

        [Test]
        public void TestPinnedSkinsSurfaceFirst()
        {
            AddStep("pin charlie", () => realm.Write(r => r.Find<SkinInfo>(seededIds[2])!.Pinned = true));

            AddAssert("charlie precedes alpha and bravo in dropdown", () =>
            {
                var ids = userSkinIdsFromDropdown();
                int charlie = ids.IndexOf(seededIds[2]);
                int alpha = ids.IndexOf(seededIds[0]);
                int bravo = ids.IndexOf(seededIds[1]);
                return charlie >= 0 && alpha > charlie && bravo > charlie;
            });
        }

        [Test]
        public void TestPinnedBucketKeepsAlphabeticalOrder()
        {
            AddStep("pin alpha and charlie (skip bravo)", () => realm.Write(r =>
            {
                r.Find<SkinInfo>(seededIds[0])!.Pinned = true;
                r.Find<SkinInfo>(seededIds[2])!.Pinned = true;
            }));

            AddAssert("alpha precedes charlie within the pinned bucket", () =>
            {
                var ids = userSkinIdsFromDropdown();
                int alpha = ids.IndexOf(seededIds[0]);
                int charlie = ids.IndexOf(seededIds[2]);
                int bravo = ids.IndexOf(seededIds[1]);

                // Both pinned skins surface ahead of the unpinned one, and within the pinned bucket
                // the underlying alphabetical ordering is preserved by the stable sort.
                return alpha >= 0 && charlie > alpha && bravo > charlie;
            });
        }

        [Test]
        public void TestCycleVisitsEverySkinByDefault()
        {
            assertCycleVisitsAllSeededSkins(favouritesOnly: false);
        }

        [Test]
        public void TestCycleRestrictsToFavouritesWhenEnoughArePinned()
        {
            AddStep("pin alpha and charlie", () => realm.Write(r =>
            {
                r.Find<SkinInfo>(seededIds[0])!.Pinned = true;
                r.Find<SkinInfo>(seededIds[2])!.Pinned = true;
            }));

            AddStep("select alpha", () => skins.CurrentSkinInfo.Value = realm.Run(r => r.Find<SkinInfo>(seededIds[0])!.ToLive(realm)));

            AddStep("cycle next", () => skins.SelectNextSkin(favouritesOnly: true));
            AddAssert("now on a pinned skin", () => skins.CurrentSkinInfo.Value.PerformRead(s => s.Pinned));

            AddStep("cycle next again", () => skins.SelectNextSkin(favouritesOnly: true));
            AddAssert("still on a pinned skin (bravo skipped)", () => skins.CurrentSkinInfo.Value.PerformRead(s => s.Pinned && s.ID != seededIds[1]));
        }

        [Test]
        public void TestCycleFallsBackToAllSkinsWithFewerThanTwoPinned()
        {
            AddStep("pin only alpha", () => realm.Write(r => r.Find<SkinInfo>(seededIds[0])!.Pinned = true));

            // With a single pinned skin the cycle would lock the user on that one entry.
            // Verify the manager falls back to the full list so every keypress still moves selection.
            assertCycleVisitsAllSeededSkins(favouritesOnly: true);
        }

        private void assertCycleVisitsAllSeededSkins(bool favouritesOnly)
        {
            HashSet<Guid> visited = null!;

            AddStep("select alpha", () => skins.CurrentSkinInfo.Value = realm.Run(r => r.Find<SkinInfo>(seededIds[0])!.ToLive(realm)));
            AddStep("walk the cycle", () =>
            {
                visited = new HashSet<Guid> { skins.CurrentSkinInfo.Value.ID };

                // Walk far enough to exhaust the cycle even with the protected default skins also in the loop.
                int totalSkins = skins.GetAllUsableSkins().Count;

                for (int i = 0; i < totalSkins * 2; i++)
                {
                    skins.SelectNextSkin(favouritesOnly);
                    visited.Add(skins.CurrentSkinInfo.Value.ID);
                }
            });

            AddAssert("every seeded skin was reached", () => seededIds.All(id => visited.Contains(id)));
        }

        private List<Guid> userSkinIdsFromDropdown()
        {
            return skins.GetAllUsableSkins()
                        .Select(s => s.Value.ID)
                        .Where(id => seededIds.Contains(id))
                        .ToList();
        }
    }
}
