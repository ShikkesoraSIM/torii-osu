// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.ComponentModel;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;

namespace osu.Game.Overlays.Settings.Sections.Maintenance
{
    /// <summary>
    /// Torii: elegir a mano el periodo del dispositivo en modo exclusivo.
    /// </summary>
    /// <remarks>
    /// El init de exclusivo prueba el minimo que declara el dispositivo, su default y
    /// el automatico, y se queda con el PRIMERO que entre: en la practica, siempre el
    /// minimo. Para casi todos eso esta bien y es la menor latencia posible.
    ///
    /// El problema es el firmware que no aguanta ese ritmo. Un headset inalambrico
    /// corriendo a 3ms puede resetearse cada tantos minutos, y en exclusivo eso no es
    /// un salto de audio: el juego POSEE el dispositivo, asi que cuando desaparece hay
    /// que reconstruir el stack entero y se congela varios segundos, cursor incluido.
    ///
    /// Es un dropdown y no un slider porque los valores utiles son pocos y discretos,
    /// y con un slider la gente termina eligiendo numeros que el dispositivo rechaza.
    ///
    /// Vive en Mantenimiento y no en Audio a proposito: no es una preferencia de
    /// sonido, es una salida para cuando el hardware se porta mal. Solo aparece con el
    /// modo exclusivo prendido, porque en compartido no hace absolutamente nada.
    /// </remarks>
    public partial class ExclusiveAudioSettings : SettingsSubsection
    {
        protected override LocalisableString Header => @"Exclusive audio";

        [Resolved]
        private AudioManager audio { get; set; } = null!;

        private Bindable<ExclusiveAudioPeriod> period = null!;
        private readonly BindableBool exclusiveActive = new BindableBool();

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            period = config.GetBindable<ExclusiveAudioPeriod>(OsuSetting.ToriiExclusiveAudioPeriod);

            Add(new SettingsItemV2(new FormEnumDropdown<ExclusiveAudioPeriod>
            {
                Caption = @"Device period",
                HintText =
                    "How often the game hands audio to your device while exclusive mode is on. "
                    + "Lower is less latency; higher is more stable.\n\n"
                    + "Leave this on Automatic unless exclusive mode freezes the game for a few "
                    + "seconds every so often. That usually means your device can't actually keep "
                    + "up with the period it claims to support, and raising it fixes the freezing "
                    + "at the cost of a couple of milliseconds.\n\n"
                    + "If your device rejects the value you pick, the game falls back to its usual "
                    + "choice, so you can't lose audio here.",
                Current = period,
                NewFeatureId = NewFeatureRegistry.ExclusiveAudioPeriod,
            }));

            // Se mira el bindable del framework y no el checkbox de ajustes: asi esto
            // tambien se esconde solo cuando el exclusivo se cae a compartido por su
            // cuenta (por ejemplo, si otra aplicacion tenia tomado el dispositivo).
            exclusiveActive.BindTo(audio.UseExclusiveWasapi);
            exclusiveActive.BindValueChanged(on =>
            {
                // Sobre la subseccion entera: si solo se escondiera el control, el
                // titulo quedaria colgado en medio de Mantenimiento sin nada debajo.
                Alpha = on.NewValue ? 1 : 0;
            }, true);

            period.BindValueChanged(v => audio.ExclusiveUpdatePeriodMs.Value = (int)v.NewValue, true);
        }
    }

    /// <summary>
    /// Periodos ofrecidos, en ms. El valor del enum ES el periodo, y 0 es automatico.
    /// </summary>
    public enum ExclusiveAudioPeriod
    {
        [Description("Automatic (lowest your device allows)")]
        Automatic = 0,

        [Description("3 ms")]
        ThreeMs = 3,

        [Description("5 ms")]
        FiveMs = 5,

        [Description("8 ms")]
        EightMs = 8,

        [Description("12 ms")]
        TwelveMs = 12,

        [Description("20 ms (most stable)")]
        TwentyMs = 20,
    }
}
