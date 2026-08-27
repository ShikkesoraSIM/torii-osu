// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Rulesets;

namespace osu.Game.Tests.Rulesets
{
    [TestFixture]
    public class CustomRulesetGuardTest
    {
        private static readonly string[] official =
        {
            "osu.Game.Rulesets.Osu",
            "osu.Game.Rulesets.Taiko",
            "osu.Game.Rulesets.Catch",
            "osu.Game.Rulesets.Mania",
        };

        [Test]
        public void TestOfficialRulesetsAreNotFlagged()
        {
            Assert.That(CustomRulesetGuard.Filter(official), Is.Empty);
        }

        [Test]
        public void TestCustomRulesetIsFlagged()
        {
            var result = CustomRulesetGuard.Filter(new[] { "osu.Game.Rulesets.Osu", "osu.Game.Rulesets.Mosu" });

            Assert.That(result, Is.EquivalentTo(new[] { "osu.Game.Rulesets.Mosu" }));
        }

        [Test]
        public void TestCasingDoesNotLetOneThrough()
        {
            // Windows no distingue mayusculas en nombres de archivo, asi que renombrar el dll
            // a OSU.GAME.RULESETS.OSU.dll no puede servir para hacerse pasar por oficial.
            Assert.That(CustomRulesetGuard.Filter(new[] { "OSU.GAME.RULESETS.OSU" }), Is.Empty);
        }

        [Test]
        public void TestNamesWithoutThePrefixAreStillFlagged()
        {
            // Un ruleset no esta obligado a llamarse osu.Game.Rulesets.*: si el juego lo
            // cargo, cuenta, se llame como se llame.
            Assert.That(CustomRulesetGuard.Filter(new[] { "TotallyLegitRuleset" }),
                Is.EquivalentTo(new[] { "TotallyLegitRuleset" }));
        }

        [Test]
        public void TestNullAndEmptyAreIgnored()
        {
            Assert.That(CustomRulesetGuard.Filter(new string?[] { null, "", "osu.Game.Rulesets.Osu" }), Is.Empty);
        }

        [Test]
        public void TestDuplicatesCollapse()
        {
            var result = CustomRulesetGuard.Filter(new[] { "osu.Game.Rulesets.Mosu", "osu.Game.Rulesets.Mosu" });

            Assert.That(result.Length, Is.EqualTo(1));
        }
    }
}
