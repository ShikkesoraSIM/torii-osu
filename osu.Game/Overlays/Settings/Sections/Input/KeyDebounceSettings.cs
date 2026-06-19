// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;

namespace osu.Game.Overlays.Settings.Sections.Input
{
    /// <summary>
    /// torii: key debounce anti-chatter. filtra el doble-tap fantasma que tiran los teclados
    /// rapid-trigger / hall-effect y los switches mecanicos gastados, dropeando un re-press de
    /// tecla de gameplay que cae dentro del umbral despues de su ultimo release. solo teclas de
    /// gameplay, solo play en vivo (nunca toca replays).
    /// </summary>
    public partial class KeyDebounceSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Key debounce (anti-chatter)";

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Filter double-taps (rapid trigger / chatter)",
                    HintText = "Drops a gameplay key press that fires within the threshold of that key's last release - the spurious "
                               + "double-tap from rapid-trigger / hall-effect boards or worn switches. Gameplay keys only; never touches "
                               + "typing or replays. It can only remove too-fast repeats, so it can't help you score better.",
                    Current = config.GetBindable<bool>(OsuSetting.ToriiKeyDebounceEnabled),
                    NewFeatureId = NewFeatureRegistry.KeyDebounce,
                })
                {
                    Keywords = new[] { @"debounce", @"chatter", @"unchatter", @"double", @"tap", @"rapid", @"trigger", @"switch", @"bounce", @"torii" },
                },
                new SettingsItemV2(new FormSliderBar<double>
                {
                    Caption = "Threshold (ms)",
                    HintText = "How close together two presses of the same key must be to count as chatter. Keep it low - well below your "
                               + "fastest legit tap gap (a 200 BPM single-key stream is ~75ms apart) or it will eat real inputs. 15ms is a safe start.",
                    Current = config.GetBindable<double>(OsuSetting.ToriiKeyDebounceThresholdMs),
                    TransferValueOnCommit = true,
                })
                {
                    Keywords = new[] { @"debounce", @"chatter", @"threshold", @"ms", @"milliseconds" },
                },
            };
        }
    }
}
