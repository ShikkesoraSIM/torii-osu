// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.IO;
using osu.Game.Localisation;
using osu.Game.Screens.Edit.Setup;
using osuTK;

namespace osu.Game.Overlays.FirstRunSetup
{
    /// <summary>
    /// torii: segundo paso del first-run, fuera del camino feliz. sólo se llega desde la opción terciaria de
    /// <see cref="ScreenToriiStorage"/>, o sea cuando el jugador quiere una carpeta distinta a la detectada.
    /// </summary>
    [LocalisableDescription(typeof(ToriiSettingsStrings), nameof(ToriiSettingsStrings.PickLazerFolderHeader))]
    public partial class ScreenToriiFolderPicker : WizardScreen
    {
        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private OsuGameBase game { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved(canBeNull: true)]
        private IDialogOverlay? dialogOverlay { get; set; }

        [Resolved(canBeNull: true)]
        private FirstRunSetupOverlay? firstRunOverlay { get; set; }

        private LazerStorageLocatorTextBox storageTextBox = null!;

        private OsuTextFlowContainer helperText = null!;

        public override LocalisableString? NextStepText => ToriiSettingsStrings.SkipFirstRunSetup;

        [BackgroundDependencyLoader]
        private void load()
        {
            Content.Children = new Drawable[]
            {
                new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    Text = ToriiSettingsStrings.PickLazerFolderDescription,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                },
                // torii: no lo pre-cargamos con la carpeta detectada, el jugador llegó acá justamente
                // porque quiere otra.
                storageTextBox = new LazerStorageLocatorTextBox
                {
                    Label = ToriiSettingsStrings.FolderSelectorLabel,
                    PlaceholderText = ToriiSettingsStrings.FolderSelectorPlaceholder,
                },
                new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 55,
                    Text = ToriiSettingsStrings.UseSelectedLazerFolder,
                    Action = importSelectedFolder,
                },
                // torii: salida de emergencia. si vuelven atrás desde acá y eligen quedarse frescos,
                // el wizard los deja parados en este paso otra vez, y sin este link no habría forma
                // de seguir sin elegir carpeta.
                new OsuHoverContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    IdleColour = OverlayColourProvider.Content2,
                    HoverColour = OverlayColourProvider.Light1,
                    Action = stayFresh,
                    Child = new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: 13))
                    {
                        Text = ToriiSettingsStrings.StayFreshInstead,
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        TextAnchor = Anchor.TopCentre,
                    },
                },
                helperText = new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: 13))
                {
                    Text = ToriiSettingsStrings.ChangeLaterInSettings,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Colour = OverlayColourProvider.Content2,
                },
            };
        }

        private void importSelectedFolder()
        {
            if (ToriiFirstRunFlow.ApplyDataPath(storageTextBox.Current.Value, storage, game, config, dialogOverlay))
                return;

            helperText.Text = ToriiSettingsStrings.InvalidLazerFolder;
            helperText.Colour = colours.Orange1;
        }

        private void stayFresh()
        {
            firstRunOverlay?.AppendClassicSteps();

            // el wizard sólo avanza por el botón del footer, y los pasos se leen recién al avanzar.
            Schedule(() => firstRunOverlay?.AdvanceStep());
        }

        private partial class LazerStorageLocatorTextBox : LabelledTextBoxWithPopover
        {
            private readonly Bindable<DirectoryInfo?> currentDirectory = new Bindable<DirectoryInfo?>();

            private bool changingDirectory;

            protected override void LoadComplete()
            {
                base.LoadComplete();

                currentDirectory.BindValueChanged(onDirectorySelected);
            }

            private void onDirectorySelected(ValueChangedEvent<DirectoryInfo?> directory)
            {
                if (changingDirectory)
                    return;

                try
                {
                    changingDirectory = true;

                    if (directory.NewValue == null)
                    {
                        Current.Value = string.Empty;
                        return;
                    }

                    // DirectorySelectors can trigger a noop value changed, but `DirectoryInfo` equality doesn't catch this.
                    if (directory.OldValue?.FullName == directory.NewValue.FullName)
                        return;

                    // torii: sólo cerramos el popover cuando la carpeta navegada es de verdad una de lazer.
                    // commitear en cada cambio de directorio pateaba al jugador afuera del selector apenas
                    // entraba a una subcarpeta.
                    if (ToriiStoragePathHelper.LooksLikeLazerStoragePath(directory.NewValue.FullName))
                    {
                        Current.Value = directory.NewValue.FullName;
                        this.HidePopover();
                    }
                }
                finally
                {
                    changingDirectory = false;
                }
            }

            public override Popover GetPopover() => new DirectoryChooserPopover(currentDirectory);

            private partial class DirectoryChooserPopover : OsuPopover
            {
                public DirectoryChooserPopover(Bindable<DirectoryInfo?> currentDirectory)
                    : base(false)
                {
                    Child = new Container
                    {
                        Size = new Vector2(600, 400),
                        // torii: arranca desde el path navegado en vivo y queda bindeado, así reabrir el
                        // popover retoma donde estaba en vez de volver al principio.
                        Child = new OsuDirectorySelector(currentDirectory.Value?.FullName)
                        {
                            RelativeSizeAxes = Axes.Both,
                            CurrentPath = { BindTarget = currentDirectory }
                        },
                    };
                }

                [BackgroundDependencyLoader]
                private void load(OverlayColourProvider colourProvider)
                {
                    Body.BorderColour = colourProvider.Highlight1;
                    Body.BorderThickness = 2;
                }
            }
        }
    }
}
