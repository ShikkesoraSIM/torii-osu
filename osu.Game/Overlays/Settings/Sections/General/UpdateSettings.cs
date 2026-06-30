// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Threading.Tasks;
using osu.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Online.Multiplayer;
using osu.Game.Overlays.Dialog;
using osu.Game.Overlays.Notifications;
using osu.Game.Updater;

namespace osu.Game.Overlays.Settings.Sections.General
{
    public partial class UpdateSettings : SettingsSubsection
    {
        protected override LocalisableString Header => GeneralSettingsStrings.UpdateHeader;

        private SettingsButtonV2 checkForUpdatesButton = null!;
        private FormEnumDropdown<ReleaseStream> releaseStreamDropdown = null!;

        private readonly Bindable<SettingsNote.Data?> releaseStreamDropdownNote = new Bindable<SettingsNote.Data?>();

        private readonly Bindable<ReleaseStream> configReleaseStream = new Bindable<ReleaseStream>();

        [Resolved]
        private UpdateManager? updateManager { get; set; }

        [Resolved]
        private INotificationOverlay? notifications { get; set; }

        [Resolved]
        private OsuGame? game { get; set; }

        [Resolved]
        private IDialogOverlay? dialogOverlay { get; set; }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            config.BindWith(OsuSetting.ReleaseStream, configReleaseStream);

            bool isDesktop = RuntimeInfo.IsDesktop;

            // For simplicity, hide the concept of release streams from mobile users.
            if (isDesktop)
            {
                Add(new SettingsItemV2(releaseStreamDropdown = new FormEnumDropdown<ReleaseStream>
                {
                    Caption = GeneralSettingsStrings.ReleaseStream,
                    Current = { Value = configReleaseStream.Value },
                })
                {
                    Keywords = new[] { @"version" },
                    ShowRevertToDefaultButton = updateManager!.FixedReleaseStream == null
                });

                if (updateManager!.FixedReleaseStream != null)
                {
                    configReleaseStream.Value = updateManager.FixedReleaseStream.Value;

                    releaseStreamDropdown.Items = [updateManager.FixedReleaseStream.Value];
                    releaseStreamDropdownNote.Value = new SettingsNote.Data(GeneralSettingsStrings.ChangeReleaseStreamPackageManagerWarning, SettingsNote.Type.Warning);
                }

                releaseStreamDropdown.Current.BindValueChanged(releaseStreamChanged);
            }

            Add(checkForUpdatesButton = new SettingsButtonV2
            {
                Text = GeneralSettingsStrings.CheckUpdate,
                Action = () => checkForUpdates().FireAndForget()
            });
        }

        private void releaseStreamChanged(ValueChangedEvent<ReleaseStream> stream)
        {
            if (stream.NewValue == ReleaseStream.Nova)
            {
                dialogOverlay?.Push(
                    new ConfirmDialog(GeneralSettingsStrings.ChangeReleaseStreamConfirmation,
                        () => configReleaseStream.Value = ReleaseStream.Nova,
                        () => releaseStreamDropdown.Current.Value = ReleaseStream.Torii)
                    {
                        BodyText = GeneralSettingsStrings.ChangeReleaseStreamConfirmationInfo
                    });

                return;
            }

            // Vanilla is upstream lazer wired to Torii: no Torii features, and its
            // client-side pp will drift from the server's. Spell that out before the
            // user commits to swapping to a different binary.
            if (stream.NewValue == ReleaseStream.Vanilla)
            {
                dialogOverlay?.Push(
                    new ConfirmDialog("Switch to the Vanilla stream?",
                        () =>
                        {
                            configReleaseStream.Value = ReleaseStream.Vanilla;
                            checkForUpdates().FireAndForget();
                        },
                        () => releaseStreamDropdown.Current.Value = ReleaseStream.Torii)
                    {
                        BodyText = "Vanilla is basically Lazer but wired to work on Torii. It won't have any Torii features and the PP gotten from maps will deviate from the one stored in the server, but this is good for people with compatibility problems, lag issues, poor pcs, or weird systems like Wayland, Linux, etc."
                    });

                return;
            }

            configReleaseStream.Value = stream.NewValue;
        }

        private async Task checkForUpdates()
        {
            if (updateManager == null || game == null)
                return;

            checkForUpdatesButton.Enabled.Value = false;

            var checkingNotification = new ProgressNotification
            {
                Text = GeneralSettingsStrings.CheckingForUpdates,
            };
            notifications?.Post(checkingNotification);

            try
            {
                bool foundUpdate = await updateManager.CheckForUpdateAsync(checkingNotification.CancellationToken).ConfigureAwait(true);

                if (!foundUpdate)
                {
                    notifications?.Post(new SimpleNotification
                    {
                        Text = GeneralSettingsStrings.RunningLatestRelease(game.Version),
                        Icon = FontAwesome.Solid.CheckCircle,
                    });
                }
            }
            catch
            {
            }
            finally
            {
                checkingNotification.CompleteSilently();
                checkForUpdatesButton.Enabled.Value = true;
            }
        }
    }
}
