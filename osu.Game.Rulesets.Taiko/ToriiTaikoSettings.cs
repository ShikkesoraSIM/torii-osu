// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Taiko.Configuration;

namespace osu.Game.Rulesets.Taiko
{
    /// <summary>
    /// Torii-section mirror of <see cref="TaikoSettingsSubsection"/>: same
    /// toggles, surfaced under <c>Settings → Torii → Taiko</c> in addition
    /// to the native <c>Settings → Rulesets → osu!taiko</c> location.
    /// </summary>
    /// <remarks>
    /// Both UI surfaces resolve their bindables from the same cached
    /// <see cref="TaikoRulesetConfigManager"/> (via <see cref="Config"/>),
    /// so flipping a toggle on either page updates the other live.
    ///
    /// Lives in the Taiko ruleset assembly because that's where the
    /// strongly-typed enum (<see cref="TaikoRulesetSetting"/>) and config
    /// manager are defined — referencing them from osu.Game would invert
    /// the project dependency direction. <c>ToriiSection</c> in osu.Game
    /// pulls this subsection in via the generic
    /// <see cref="Ruleset.CreateToriiSettingsSubsection"/> hook.
    /// </remarks>
    public partial class ToriiTaikoSettings : RulesetSettingsSubsection
    {
        protected override LocalisableString Header => "Taiko";

        public ToriiTaikoSettings()
            : base(new TaikoRuleset())
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var config = (TaikoRulesetConfigManager)Config;

            Children = new Drawable[]
            {
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = RulesetSettingsStrings.HitAnimation,
                    HintText = RulesetSettingsStrings.HitAnimationDescription,
                    Current = config.GetBindable<bool>(TaikoRulesetSetting.HitAnimation)
                })
                {
                    Keywords = new[] { "taiko", "hit", "animation", "celebrate", "stable", "vanish" }
                },
            };
        }
    }
}
