// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    /// <summary>
    /// Torii-specific gameplay-flow tweaks. Currently houses the mid-map break-skip
    /// toggles; intended as the home for any future "modify how the gameplay screens
    /// behave" preferences so they don't pollute Interface (visuals) or Server (networking).
    /// (The long-attempt Retry/Quit confirm toggle is deferred until its GameplayMenuOverlay
    /// gating is ported, to avoid shipping a dead toggle.)
    /// </summary>
    public partial class ToriiGameplaySettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Gameplay";

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = ToriiSettingsStrings.SkipBreaks,
                    Current = config.GetBindable<bool>(OsuSetting.ToriiSkipBreaksEnabled),
                    HintText = ToriiSettingsStrings.SkipBreaksHint,
                    NewFeatureId = NewFeatureRegistry.SkipBreaks,
                })
                {
                    Keywords = new[] { @"skip", @"break", @"breaks", @"mid", @"map", @"fast", @"forward", @"torii" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = ToriiSettingsStrings.SkipBreaksSingleConfirmation,
                    Current = config.GetBindable<bool>(OsuSetting.ToriiSkipBreaksSingleConfirmation),
                    HintText = ToriiSettingsStrings.SkipBreaksSingleConfirmationHint,
                    NewFeatureId = NewFeatureRegistry.SkipBreaksSingleConfirmation,
                })
                {
                    Keywords = new[] { @"skip", @"break", @"breaks", @"single", @"confirmation", @"double", @"press", @"misclick", @"torii" },
                },
            };
        }
    }
}
