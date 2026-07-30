// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Online.API;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Localisation;
using osu.Game.Overlays.FirstRunSetup;
using osu.Game.Overlays.Notifications;
using osu.Game.Screens;
using osu.Game.Screens.Menu;

namespace osu.Game.Overlays
{
    [Cached]
    public partial class FirstRunSetupOverlay : WizardOverlay
    {
        [Resolved]
        private IPerformFromScreenRunner performer { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notificationOverlay { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [Resolved(canBeNull: true)]
        private ToriiWelcomeOverlay? toriiWelcomeOverlay { get; set; }

        private readonly Bindable<bool> showFirstRunSetup = new Bindable<bool>();

        private readonly Bindable<bool> showToriiWelcome = new Bindable<bool>();

        private LegacyImportManager? legacyImportManager;

        public FirstRunSetupOverlay()
            : base(OverlayColourScheme.Purple)
        {
        }

        [BackgroundDependencyLoader(permitNulls: true)]
        private void load(LegacyImportManager? legacyImportManager)
        {
            this.legacyImportManager = legacyImportManager;

            // torii: el unico paso que existe de entrada es la deteccion de la carpeta de lazer. la mayoria
            // importa ahi mismo y el juego se reinicia, asi que los pasos clasicos de ppy se agregan en
            // caliente (ver AppendClassicSteps) solo si eligen quedarse portable.
            AddStep<ScreenToriiStorage>();

            Header.Title = FirstRunSetupOverlayStrings.FirstRunSetupTitle;
            // el string de ppy dice "osu!", pero lo primero que se ve aca es la pantalla de datos de torii.
            Header.Description = @"Set up Torii to suit you";
        }

        /// <summary>
        /// torii: pone el paso de elegir carpeta a mano como continuacion. lo llama la pantalla de deteccion
        /// cuando el jugador pide importar otra carpeta, o sea justo antes de avanzar de paso.
        /// </summary>
        /// <summary>
        /// torii: avanzar de paso desde una pantalla que ramifica.
        ///
        /// No sirve disparar el boton del pie: en la pantalla de deteccion esta apagado a proposito, porque
        /// seria una salida mas y la mas prominente (ver WizardScreen.HideNextButton).
        /// </summary>
        public void AdvanceStep() => ShowNextStep();

        public void AppendFolderPickerStep()
        {
            SetStepsAfterCurrent(typeof(ScreenToriiFolderPicker));
        }

        /// <summary>
        /// torii: pone los pasos originales del first-run de ppy como continuacion. lo llama la pantalla de
        /// deteccion cuando el jugador decide quedarse fresco, o sea justo antes de avanzar de paso.
        ///
        /// Reemplaza la cola en vez de agregar al final: con el boton de volver se puede elegir un camino,
        /// volver, y elegir el otro, y ahi la rama vieja quedaba colgada mas abajo.
        /// </summary>
        public void AppendClassicSteps()
        {
            var pasos = new List<Type>
            {
                typeof(ScreenWelcome),
                typeof(ScreenUIScale),
                typeof(ScreenBeatmaps),
            };

            if (legacyImportManager?.SupportsImportFromStable == true)
                pasos.Add(typeof(ScreenImportFromStable));

            pasos.Add(typeof(ScreenBehaviour));

            SetStepsAfterCurrent(pasos.ToArray());
        }

        private IDisposable? hueBinding;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // torii: sin esto la ventana del wizard se queda con su color fijo mientras el resto de la UI
            // se tine. Es el mismo enganche que ya usan el chat, las notificaciones y el panel de settings.
            hueBinding = CustomUiHueHelper.BindFullScheme(config, ColourProvider, OverlayColourScheme.Purple.GetHue(), CustomUiHueScope.Overlays, api);

            config.BindWith(OsuSetting.ShowFirstRunSetup, showFirstRunSetup);
            config.BindWith(OsuSetting.ShowToriiWelcome, showToriiWelcome);

            if (showFirstRunSetup.Value) Show();
        }

        public override void Show()
        {
            // if we are valid for display, only do so after reaching the main menu.
            performer.PerformFromScreen(screen =>
            {
                // Hides the toolbar for us.
                if (screen is MainMenu menu)
                    menu.ReturnToOsuLogo();

                base.Show();
            }, new[] { typeof(MainMenu) });
        }

        protected override void PopOut()
        {
            base.PopOut();

            if (CurrentStepIndex != null)
            {
                notificationOverlay.Post(new SimpleNotification
                {
                    Text = FirstRunSetupOverlayStrings.ClickToResumeFirstRunSetupAtAnyPoint,
                    Icon = FontAwesome.Solid.Redo,
                    Activated = () =>
                    {
                        Show();
                        return true;
                    },
                });
            }
        }

        protected override void ShowNextStep()
        {
            base.ShowNextStep();

            if (CurrentStepIndex == null)
            {
                showFirstRunSetup.Value = false;

                // torii: terminar el setup SIEMPRE encadena con el wizard de torii, y a proposito NO se mira
                // ShowToriiWelcome aca. Ese flag es solo para el auto-abrir del arranque (el camino de
                // importar la carpeta, que reinicia el juego). Colgar tambien esta rama de el hacia que en la
                // maquina de cualquiera que ya tenia Torii no saliera nunca, porque el guard one-shot se lo
                // apaga al arrancar. Y si alguien vuelve a correr el setup a mano desde settings, al terminarlo
                // lo quiere ver igual.
                //
                // Agendado y no en linea: este overlay y el de torii comparten el grupo de "side overlays que
                // se cancelan entre si", asi que mostrarlo en el mismo frame en que este se esconde lo mataba.
                Schedule(() => toriiWelcomeOverlay?.Show());
            }
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            hueBinding?.Dispose();
        }
    }
}
