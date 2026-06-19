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
    /// torii: mirror de <see cref="TaikoSettingsSubsection"/> en la seccion torii. mismos toggles,
    /// pero bajo Settings -> Torii -> Taiko ademas de la ubicacion nativa Settings -> Rulesets ->
    /// osu!taiko. las dos pantallas sacan sus bindables del mismo <see cref="TaikoRulesetConfigManager"/>
    /// cacheado (via <see cref="Config"/>), asi tocar un toggle en cualquiera actualiza la otra en vivo.
    /// vive en el assembly de taiko porque ahi estan el enum tipado y el config manager; referenciarlos
    /// desde osu.Game invertiria la direccion de dependencia. ToriiSection lo trae via el hook generico
    /// <see cref="Ruleset.CreateToriiSettingsSubsection"/>.
    /// </summary>
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
                    Caption = RulesetSettingsStrings.RateAdjustedHitAnimation,
                    HintText = RulesetSettingsStrings.RateAdjustedHitAnimationTooltip,
                    Current = config.GetBindable<bool>(TaikoRulesetSetting.RateAdjustedHitAnimation)
                })
                {
                    Keywords = new[] { "taiko", "hit", "animation", "celebrate", "stable", "vanish" }
                },
            };
        }
    }
}
