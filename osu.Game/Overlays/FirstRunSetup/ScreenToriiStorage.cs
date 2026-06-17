// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.IO;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.IO;
using osu.Game.Localisation;
using osu.Game.Overlays.Dialog;
using osu.Game.Screens.Edit.Setup;
using osuTK;
using FontAwesome = osu.Framework.Graphics.Sprites.FontAwesome;

namespace osu.Game.Overlays.FirstRunSetup
{
    [LocalisableDescription(typeof(ToriiSettingsStrings), nameof(ToriiSettingsStrings.FirstRunHeader))]
    public partial class ScreenToriiStorage : WizardScreen
    {
        // toriirefresh's MaintenanceSettingsStrings has no RestartAndReOpenRequiredForCompletion
        // (drift from the release base), so the wording is kept inline here to stay self-contained.
        private static readonly LocalisableString restart_required_message =
            @"To complete this operation, osu! will close. Please open it again to use the new data location.";

        private OsuSpriteText helperText = null!;
        private LazerStorageLocatorTextBox storageTextBox = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private OsuGameBase game { get; set; } = null!;

        [Resolved(canBeNull: true)]
        private IDialogOverlay? dialogOverlay { get; set; }

        [Resolved(canBeNull: true)]
        private FirstRunSetupOverlay? firstRunSetupOverlay { get; set; }

        private string? detectedLazerPath;

        [BackgroundDependencyLoader]
        private void load()
        {
            detectedLazerPath = ToriiStoragePathHelper.GetLikelyLazerStoragePath();

            Content.Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(12),
                    Children = new Drawable[]
                    {
                        new CircularContainer
                        {
                            Size = new Vector2(72),
                            Masking = true,
                            CornerRadius = 18,
                            BorderThickness = 3,
                            BorderColour = OverlayColourProvider.Colour2,
                            Child = new SpriteIcon
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Icon = FontAwesome.Solid.Server,
                                Size = new Vector2(30),
                                Colour = OverlayColourProvider.Light1,
                            }
                        },
                        new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                        {
                            Text = ToriiSettingsStrings.FirstRunDescription,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                        },
                        new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                        {
                            Text = !string.IsNullOrEmpty(detectedLazerPath)
                                ? ToriiSettingsStrings.DetectedLazerFolder(detectedLazerPath)
                                : ToriiSettingsStrings.NoDetectedLazerFolder,
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Colour = OverlayColourProvider.Content1,
                        },
                    }
                },
                storageTextBox = new LazerStorageLocatorTextBox
                {
                    Label = ToriiSettingsStrings.FolderSelectorLabel,
                    PlaceholderText = ToriiSettingsStrings.FolderSelectorPlaceholder,
                },
                new ProgressRoundedButton
                {
                    Width = 420,
                    Height = 50,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Text = ToriiSettingsStrings.UseDetectedLazerFolder,
                    Enabled = { Value = !string.IsNullOrEmpty(detectedLazerPath) },
                    Action = () =>
                    {
                        if (!string.IsNullOrEmpty(detectedLazerPath))
                            applyDataPath(detectedLazerPath);
                    }
                },
                new ProgressRoundedButton
                {
                    Width = 420,
                    Height = 50,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Text = ToriiSettingsStrings.UseSelectedLazerFolder,
                    Action = () => applyDataPath(storageTextBox.Current.Value),
                },
                new ProgressRoundedButton
                {
                    Width = 420,
                    Height = 50,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                    Text = ToriiSettingsStrings.KeepPortable,
                    Action = continuePortable,
                },
                helperText = new OsuSpriteText
                {
                    RelativeSizeAxes = Axes.X,
                    Text = ToriiSettingsStrings.ChangeLaterInSettings,
                    Font = OsuFont.Default.With(size: 13),
                    Colour = OverlayColourProvider.Content2,
                },
            };

            if (!string.IsNullOrEmpty(detectedLazerPath))
                storageTextBox.Current.Value = detectedLazerPath;
        }

        public override LocalisableString? NextStepText => ToriiSettingsStrings.ContinueSetup;

        private void continuePortable()
        {
            helperText.Text = ToriiSettingsStrings.ChangeLaterInSettings;
            helperText.Colour = OverlayColourProvider.Content1;
            Schedule(() => firstRunSetupOverlay?.NextButton?.TriggerClick());
        }

        private void applyDataPath(string? path)
        {
            if (!ToriiStoragePathHelper.LooksLikeLazerStoragePath(path))
            {
                helperText.Text = ToriiSettingsStrings.InvalidLazerFolder;
                helperText.Colour = new osuTK.Graphics.Color4(255, 179, 92, 255);
                return;
            }

            dialogOverlay?.Push(new ConfirmDialog(restart_required_message, () =>
            {
                (storage as OsuStorage)?.ChangeDataPath(path!);
                game.Exit();
            }));
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

                    // NAVIGATION-BUG FIX:
                    // The release committed Current + closed the popover on EVERY directory change, so a single
                    // click into any subfolder slammed the picker shut ("kicked you out / reset to the initial
                    // path"). Mirror the vanilla osu! ScreenImportFromStable gate instead: only commit the path
                    // and hide the popover once the navigated folder is actually a valid lazer data folder.
                    // Plain navigation just updates `currentDirectory`, so the persistent selector keeps
                    // browsing deeper without resetting.
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
                        // Initialise from the LIVE navigated path (currentDirectory.Value), and keep the
                        // selector's CurrentPath two-way bound to it, so re-opening the popover resumes from
                        // wherever the user last browsed instead of snapping back to a stale snapshot.
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
