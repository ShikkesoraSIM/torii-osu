// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays.Settings;

namespace osu.Game.Overlays.ToriiWelcome
{
    /// <summary>
    /// torii: ultimo paso, las opciones propias que un jugador nuevo querria decidir de entrada. Son
    /// pocas y elegidas: las que cambian como se SIENTE el juego el primer dia, no las de tuning fino
    /// (debounce, hz de los threads, potato mode) que solo tienen sentido cuando ya hay un problema.
    ///
    /// Todo lo de aca esta bindeado al mismo key de OsuConfigManager que su copia en Settings > Torii,
    /// asi que tocarlo aca es exactamente igual que tocarlo alla.
    /// </summary>
    [LocalisableDescription(typeof(ToriiWelcomeStrings), nameof(ToriiWelcomeStrings.OptionsTitle))]
    public partial class ScreenToriiOptions : WizardScreen
    {
        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Content.Children = new Drawable[]
            {
                new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    Text = ToriiWelcomeStrings.OptionsDescription,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = ToriiSettingsStrings.SkipBreaks,
                    HintText = ToriiSettingsStrings.SkipBreaksHint,
                    Current = config.GetBindable<bool>(OsuSetting.ToriiSkipBreaksEnabled),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = ToriiWelcomeStrings.OptionsConfirmDangerousButtons,
                    HintText = ToriiWelcomeStrings.OptionsConfirmDangerousButtonsHint,
                    Current = config.GetBindable<bool>(OsuSetting.ToriiConfirmDangerousButtonsOnLongAttempts),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = ToriiWelcomeStrings.OptionsAutoHideToolbar,
                    HintText = ToriiWelcomeStrings.OptionsAutoHideToolbarHint,
                    Current = config.GetBindable<bool>(OsuSetting.ToriiAutoHideToolbar),
                }),
                // prenderlo dispara el disclaimer de "esto es experimental" que ya escucha OsuGame sobre
                // este mismo bindable, asi que el aviso sale igual que si lo prendieran desde Settings.
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = ToriiWelcomeStrings.OptionsLegacySongSelect,
                    HintText = ToriiWelcomeStrings.OptionsLegacySongSelectHint,
                    Current = config.GetBindable<bool>(OsuSetting.ToriiLegacyFooterUseSkin),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = ToriiWelcomeStrings.OptionsStableResults,
                    HintText = @"Show the classic osu!(stable) results screen after a play instead of the lazer one.",
                    Current = config.GetBindable<bool>(OsuSetting.ToriiStableResults),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = ToriiWelcomeStrings.OptionsUserAuras,
                    HintText = ToriiWelcomeStrings.OptionsUserAurasHint,
                    Current = config.GetBindable<bool>(OsuSetting.UserAuraEnabled),
                }),
            };
        }
    }
}
