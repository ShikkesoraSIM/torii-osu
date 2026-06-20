// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    /// <summary>
    /// torii: opciones de performance / graphics. potato mode (preset agresivo, restart) y el rate de
    /// los threads input/audio (tambien espejado en Settings -> Graphics -> Renderer, mismo bindable).
    /// </summary>
    public partial class ToriiGraphicsSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Graphics";

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                new PotatoModeToggleAndRestart(),
                new SettingsItemV2(new FormEnumDropdown<ToriiInputAudioHzMode>
                {
                    Caption = "Input/audio thread rate",
                    HintText = "How fast the input, audio and update threads run. Higher rates suit high-polling-rate mice (e.g. 8000 Hz) but cost more CPU. 2000 Hz is a safe default. Applies instantly.",
                    Current = config.GetBindable<ToriiInputAudioHzMode>(OsuSetting.ToriiInputAudioHz),
                    NewFeatureId = NewFeatureRegistry.InputAudioHz,
                })
                {
                    Keywords = new[] { @"hz", @"polling", @"rate", @"input", @"audio", @"thread", @"latency", @"8000", @"performance" },
                },
            };
        }
    }
}
