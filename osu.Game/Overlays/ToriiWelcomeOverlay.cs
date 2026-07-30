// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API;
using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Localisation;
using osu.Game.Overlays.Notifications;
using osu.Game.Overlays.ToriiWelcome;
using osu.Game.Screens;
using osu.Game.Screens.Menu;

namespace osu.Game.Overlays
{
    /// <summary>
    /// torii: el wizard que corre DESPUES del first-run de ppy. el first-run resuelve de donde salen los
    /// datos (portable o la carpeta de lazer del jugador); esto le presenta lo que torii agrega arriba de
    /// osu!, y le deja prender/apagar ahi mismo lo que un jugador nuevo querria decidir.
    ///
    /// Rosa a proposito: el first-run es violeta, y compartiendo footer/animaciones se confunden si usan
    /// el mismo esquema. El rosa ya es el acento de los popups propios de torii (ver ToriiFeatureHintOverlay).
    /// </summary>
    [Cached]
    public partial class ToriiWelcomeOverlay : WizardOverlay
    {
        [Resolved]
        private IPerformFromScreenRunner performer { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notificationOverlay { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private readonly Bindable<bool> showFirstRunSetup = new Bindable<bool>();

        private readonly Bindable<bool> showToriiWelcome = new Bindable<bool>();

        public ToriiWelcomeOverlay()
            : base(OverlayColourScheme.Pink)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            AddStep<ScreenToriiIntro>();
            AddStep<ScreenToriiShortcuts>();
            AddStep<ScreenToriiThemes>();
            AddStep<ScreenToriiOptions>();

            Header.Title = ToriiWelcomeStrings.WelcomeTitle;
            Header.Description = ToriiWelcomeStrings.WelcomeDescription;
        }

        private IDisposable? hueBinding;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // torii: sin esto la ventana del wizard se queda con su color fijo mientras el resto de la UI
            // se tine. Es el mismo enganche que ya usan el chat, las notificaciones y el panel de settings.
            hueBinding = CustomUiHueHelper.BindFullScheme(config, ColourProvider, OverlayColourScheme.Pink.GetHue(), CustomUiHueScope.Overlays, api);

            config.BindWith(OsuSetting.ShowFirstRunSetup, showFirstRunSetup);
            config.BindWith(OsuSetting.ShowToriiWelcome, showToriiWelcome);

            // el camino normal es que nos abra el first-run al terminar. aca cubrimos el otro: el jugador
            // importo su carpeta de lazer, el juego se reinicio, y del otro lado el first-run ya esta hecho
            // (ver ToriiFirstRunFlow) asi que nadie nos llamaria.
            if (showToriiWelcome.Value && !showFirstRunSetup.Value)
                Show();
        }

        public override void Show()
        {
            // igual que el first-run: solo aparecemos con el menu principal ya en pantalla, nunca arriba del
            // intro o de una screen a medio cargar.
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

            // cerrado a mitad de camino: queda pendiente (el flag sigue prendido, vuelve solo al reabrir el
            // juego) y dejamos la notificacion para retomarlo sin esperar tanto.
            if (CurrentStepIndex != null)
            {
                notificationOverlay.Post(new SimpleNotification
                {
                    Text = ToriiWelcomeStrings.ClickToResumeWelcome,
                    Icon = FontAwesome.Solid.ToriiGate,
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
                showToriiWelcome.Value = false;
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            hueBinding?.Dispose();
        }
    }
}
