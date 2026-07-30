// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
using osu.Framework.Threading;
using osu.Framework.Utils;
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
    /// torii: los themes propios. usamos el MISMO componente que vive en Settings
    /// (<see cref="UIThemeDropdownAndRestart"/>) en vez de armar un dropdown aparte, asi el dialogo de
    /// reinicio y el guard de un solo handler valen igual desde aca.
    ///
    /// Reiniciar a mitad del wizard no rompe nada: el flag ShowToriiWelcome sigue prendido hasta que se
    /// termina, asi que al reabrir el juego el wizard vuelve solo (ya con el theme nuevo puesto).
    /// </summary>
    [LocalisableDescription(typeof(ToriiWelcomeStrings), nameof(ToriiWelcomeStrings.ThemesTitle))]
    public partial class ScreenToriiThemes : WizardScreen
    {

        // torii: el slider no escribe directo en la config.
        //
        // Cada cambio de CustomUIHue redispara el re-tinte de TODOS los esquemas enganchados, y arrastrando
        // rapido eso son cientos de reconstrucciones por segundo: se sentia como lag. Aca el slider mueve un
        // bindable propio y el valor real se escribe a lo sumo cada TICK_MS, mas una escritura final cuando
        // soltas, para que el color termine exactamente donde lo dejaste.
        private const double tick_ms = 40;

        private readonly BindableFloat hueDisplay = new BindableFloat();

        private Bindable<float> hueConfig = null!;

        private ScheduledDelegate? hueFlush;
        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Content.Children = new Drawable[]
            {
                new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    Text = ToriiWelcomeStrings.ThemesDescription,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                },
                new UIThemeDropdownAndRestart(),
                new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE - 2))
                {
                    Text = ToriiWelcomeStrings.ThemesRestartNote,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Colour = OverlayColourProvider.Content2,
                },
                new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    Text = ToriiWelcomeStrings.ThemesHueDescription,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = @"Custom UI hue",
                    HintText = @"Tint the UI (menus, overlays, settings) to a custom hue instead of the theme default.",
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIHueEnabled),
                }),
                new SettingsItemV2(new FormHuePicker
                {
                    Caption = @"UI hue",
                    HintText = @"Base hue applied across the UI when custom hue is enabled.",
                    Current = hueDisplay,
                }),
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            hueConfig = config.GetBindable<float>(OsuSetting.CustomUIHue);
            hueDisplay.Value = hueConfig.Value;

            // la config puede cambiar desde otro lado (settings): el slider sigue.
            hueConfig.BindValueChanged(e =>
            {
                if (hueFlush == null)
                    hueDisplay.Value = e.NewValue;
            });

            hueDisplay.BindValueChanged(_ =>
            {
                if (hueFlush != null)
                    return;

                hueConfig.Value = hueDisplay.Value;
                hueFlush = Scheduler.AddDelayed(() =>
                {
                    hueFlush = null;

                    // el valor final, por si el ultimo movimiento cayo dentro de la ventana.
                    if (!Precision.AlmostEquals(hueConfig.Value, hueDisplay.Value))
                        hueConfig.Value = hueDisplay.Value;
                }, tick_ms);
            });
        }
    }
}
