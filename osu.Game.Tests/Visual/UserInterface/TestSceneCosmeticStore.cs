// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Game.Configuration;
using osu.Game.Cosmetics;
using osu.Game.Overlays.Cosmetics;

namespace osu.Game.Tests.Visual.UserInterface
{
    /// <summary>
    /// Preview the cosmetic store overlay in the test browser. Buy / equip /
    /// customise are functional here (a manager is cached against the test
    /// config), but the equipped trail only shows on the real cursor in the
    /// full game (the gameplay / menu cursor containers).
    /// </summary>
    [TestFixture]
    public partial class TestSceneCosmeticStore : OsuTestScene
    {
        private DependencyContainer dependencies;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
            => dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            dependencies.Cache(new ToriiCosmeticsManager(config));

            var store = new CosmeticStoreOverlay();
            Add(store);

            AddStep("show store", store.Show);
            AddStep("toggle store", store.ToggleVisibility);
        }
    }
}
