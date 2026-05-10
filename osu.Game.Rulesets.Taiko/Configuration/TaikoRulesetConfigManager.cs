// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;

namespace osu.Game.Rulesets.Taiko.Configuration
{
    public class TaikoRulesetConfigManager : RulesetConfigManager<TaikoRulesetSetting>
    {
        public TaikoRulesetConfigManager(SettingsStore? settings, RulesetInfo ruleset, int? variant = null)
            : base(settings, ruleset, variant)
        {
        }

        protected override void InitialiseDefaults()
        {
            base.InitialiseDefaults();

            SetDefault(TaikoRulesetSetting.TouchControlScheme, TaikoTouchControlScheme.KDDK);

            // ON by default = lazer's existing behaviour. Players who prefer the
            // stable-style "objects vanish on hit" feel (or train for it) flip
            // it OFF; see the toggle in TaikoSettingsSubsection + the Torii
            // mirror in ToriiTaikoSettings, both bind to this single source.
            SetDefault(TaikoRulesetSetting.HitAnimation, true);
        }
    }

    public enum TaikoRulesetSetting
    {
        TouchControlScheme,
        HitAnimation
    }
}
