// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Dashboard.Friends;
using osu.Game.Overlays.Dashboard.CurrentlyOnline;
using osu.Game.Overlays.Dashboard;
using osu.Game.Configuration;
using osu.Framework.Testing;
using System.Linq;
using osu.Game.Tests.Resources;

namespace osu.Game.Tests.Visual.Online
{
    public partial class TestSceneDashboardOverlay : OsuTestScene
    {
        private readonly DashboardOverlay overlay;

        public TestSceneDashboardOverlay()
        {
            Add(overlay = new DashboardOverlay());
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            int supportLevel = 0;

            for (int i = 0; i < 1000; i++)
            {
                supportLevel++;

                if (supportLevel > 3)
                    supportLevel = 0;

                ((DummyAPIAccess)API).LocalUserState.Friends.Add(new APIRelation
                {
                    TargetID = 2,
                    RelationType = RelationType.Friend,
                    Mutual = true,
                    TargetUser = new APIUser
                    {
                        Username = @"peppy",
                        Id = 2,
                        Colour = "99EB47",
                        CoverUrl = TestResources.COVER_IMAGE_3,
                        IsSupporter = supportLevel > 0,
                        SupportLevel = supportLevel
                    }
                });
            }
        }

        [Test]
        public void TestShow()
        {
            AddStep("Show", overlay.Show);
        }

        [Test]
        public void TestHide()
        {
            AddStep("Hide", overlay.Hide);
        }

        /// <summary>
        /// Torii: el dashboard abre siempre en "currently online" y ordenado por rank.
        /// </summary>
        /// <remarks>
        /// El caso que importa es el segundo: cerrar, y al volver a abrir tiene que estar
        /// de nuevo en online. Sin eso el arreglo seria "la primera vez si", que es
        /// justamente lo que se sentia mal.
        /// </remarks>
        [Test]
        public void TestAlwaysOpensOnCurrentlyOnlineSortedByRank()
        {
            AddStep("abrir", overlay.Show);
            AddUntilStep("abre en currently online", () => overlay.ChildrenOfType<CurrentlyOnlineDisplay>().Any());
            AddAssert("ordenado por rank", () => config.Get<UserSortCriteria>(OsuSetting.DashboardSortMode) == UserSortCriteria.Rank);

            AddStep("irse a amigos", () => overlay.ChildrenOfType<TabControlOverlayHeader<DashboardOverlayTabs>>().Single().Current.Value = DashboardOverlayTabs.Friends);
            AddUntilStep("quedo en amigos", () => overlay.ChildrenOfType<FriendDisplay>().Any());

            AddStep("cerrar", overlay.Hide);
            AddStep("abrir de nuevo", overlay.Show);
            AddUntilStep("volvio a currently online", () => overlay.ChildrenOfType<CurrentlyOnlineDisplay>().Any());
        }

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;
    }
}
