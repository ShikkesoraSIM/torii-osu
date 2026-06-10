// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Configuration;
using osu.Game.Cosmetics;
using osu.Game.Tests.Visual;

namespace osu.Game.Tests.NonVisual
{
    [HeadlessTest]
    public partial class ToriiCosmeticsManagerTest : OsuTestScene
    {
        private OsuConfigManager config = null!;
        private ToriiCosmeticsManager cosmetics = null!;

        [SetUpSteps]
        public void SetUpSteps()
        {
            AddStep("create manager", () =>
            {
                config = new OsuConfigManager(LocalStorage);
                config.SetValue(OsuSetting.ToriiPointsBalance, 6000);
                config.SetValue(OsuSetting.CustomUIAccentUnlocked, false);
                config.SetValue(OsuSetting.OwnedCursorTrails, string.Empty);
                cosmetics = new ToriiCosmeticsManager(config);
            });
        }

        [TearDownSteps]
        public void TearDownSteps() => AddStep("dispose config", () => config.Dispose());

        [Test]
        public void TestAccentPurchaseUsesServerFlow()
        {
            string? purchasedId = null;
            int purchasedPrice = 0;

            AddStep("wire server purchase", () => cosmetics.ServerPurchase = (id, price) =>
            {
                purchasedId = id;
                purchasedPrice = price;
            });
            AddAssert("purchase succeeds", () => cosmetics.BuyAccentUnlock(CosmeticEconomy.CustomAccentHueUnlock));
            AddAssert("accent is unlocked", () => cosmetics.AccentUnlocked);
            AddAssert("balance deducted", () => cosmetics.PointsBalance.Value == 1000);
            AddAssert("mutation bumped", () => cosmetics.MutationEpoch == 1);
            AddAssert("server got accent id", () => purchasedId == ToriiCosmeticsManager.AccentUnlockId);
            AddAssert("server got price", () => purchasedPrice == CosmeticEconomy.CustomAccentHueUnlock);
        }

        [Test]
        public void TestAccentPurchaseRollback()
        {
            AddStep("buy accent", () => cosmetics.BuyAccentUnlock(CosmeticEconomy.CustomAccentHueUnlock));
            AddStep("rollback accent", () => cosmetics.RollbackPurchase(ToriiCosmeticsManager.AccentUnlockId, CosmeticEconomy.CustomAccentHueUnlock));
            AddAssert("accent is locked", () => !cosmetics.AccentUnlocked);
            AddAssert("balance restored", () => cosmetics.PointsBalance.Value == 6000);
            AddAssert("mutations bumped", () => cosmetics.MutationEpoch == 2);
        }

        [Test]
        public void TestAccentOwnershipSync()
        {
            AddStep("sync owned accent", () => cosmetics.SyncOwned(new[] { ToriiCosmeticsManager.AccentUnlockId }));
            AddAssert("accent is unlocked", () => cosmetics.AccentUnlocked);
            AddStep("sync without accent", () => cosmetics.SyncOwned(Array.Empty<string>()));
            AddAssert("accent is locked", () => !cosmetics.AccentUnlocked);
        }
    }
}
