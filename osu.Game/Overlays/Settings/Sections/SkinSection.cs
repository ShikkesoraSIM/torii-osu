// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays.SkinEditor;
using osu.Game.Screens.Select;
using osu.Game.Skinning;
using osuTK;
using Realms;
using WebCommonStrings = osu.Game.Resources.Localisation.Web.CommonStrings;

namespace osu.Game.Overlays.Settings.Sections
{
    public partial class SkinSection : SettingsSection
    {
        private SkinDropdown skinDropdown;

        public override LocalisableString Header => SkinSettingsStrings.SkinSectionHeader;

        public override Drawable CreateIcon() => new SpriteIcon
        {
            Icon = OsuIcon.SkinB
        };

        public override IEnumerable<LocalisableString> FilterTerms => base.FilterTerms.Concat(new LocalisableString[] { "skins" });

        private readonly List<Live<SkinInfo>> dropdownItems = new List<Live<SkinInfo>>();

        [Resolved]
        private SkinManager skins { get; set; }

        [Resolved]
        private RealmAccess realm { get; set; }

        private IDisposable realmSubscription;

        [BackgroundDependencyLoader(permitNulls: true)]
        private void load(OsuConfigManager config, [CanBeNull] SkinEditorOverlay skinEditor)
        {
            Children = new Drawable[]
            {
                new SettingsItemV2(skinDropdown = new SkinDropdown
                {
                    AlwaysShowSearchBar = true,
                    AllowNonContiguousMatching = true,
                    Caption = SkinSettingsStrings.CurrentSkin,
                    Current = skins.CurrentSkinInfo,
                }),
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Padding = SettingsPanel.CONTENT_PADDING,
                    Children = new Drawable[]
                    {
                        // This is all super-temporary until we move skin settings to their own panel / overlay.
                        new RenameSkinButton { Padding = new MarginPadding { Right = 2.5f }, RelativeSizeAxes = Axes.X, Width = 1 / 3f },
                        new ExportSkinButton { Padding = new MarginPadding { Horizontal = 2.5f }, RelativeSizeAxes = Axes.X, Width = 1 / 3f },
                        new DeleteSkinButton { Padding = new MarginPadding { Left = 2.5f }, RelativeSizeAxes = Axes.X, Width = 1 / 3f },
                    }
                },
                new SkinFavouriteButton(),
                new SettingsButtonV2
                {
                    Text = SkinSettingsStrings.SkinLayoutEditor,
                    Action = () => skinEditor?.ToggleVisibility(),
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = SkinSettingsStrings.CycleSkinsThroughFavoritesOnly,
                    HintText = SkinSettingsStrings.CycleSkinsThroughFavoritesOnlyDescription,
                    Current = config.GetBindable<bool>(OsuSetting.CycleSkinsThroughFavoritesOnly),
                })
                {
                    Keywords = new[] { "skin", "cycle", "favourite", "favorite", "pin", "hotkey", "keybind" },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            realmSubscription = realm.RegisterForNotifications(_ => realm.Realm.All<SkinInfo>()
                                                                         .Where(s => !s.DeletePending)
                                                                         .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase), skinsChanged);

            // Pin state lives in a sidecar JSON store (see PinnedSkinsStore
            // class docs) — outside Realm — so toggling it never fires the
            // Realm subscription above. We listen to the store's own
            // Changed event so the dropdown reorders + the ♥ prefix on
            // each item refreshes after a pin/unpin. Without this hook the
            // dropdown stays stale until the next time Realm itself fires
            // (a skin import, deletion, etc.) — the user-visible bug was
            // "I pin a skin and it stays at the bottom, no heart appears".
            skins.PinnedSkins.Changed += refreshSkinList;

            skinDropdown.Current.BindValueChanged(skin =>
            {
                if (skin.NewValue.ID == SkinInfo.RANDOM_SKIN)
                {
                    // before selecting random, set the skin back to the previous selection.
                    // this is done because at this point it will be random_skin_info, and would
                    // cause SelectRandomSkin to be unable to skip the previous selection.
                    skins.CurrentSkinInfo.Value = skin.OldValue;
                    skins.SelectRandomSkin();
                }
            });
        }

        /// <summary>
        /// Rebuilds the dropdown from the current sorted skin list.
        /// Marshalled onto the update thread because PinnedSkinsStore.Changed
        /// can fire from any thread (the sidecar persist runs synchronously
        /// from whoever called <c>SetPinned</c>, typically the update
        /// thread, but the contract doesn't guarantee that).
        /// </summary>
        private void refreshSkinList() => Schedule(() =>
        {
            dropdownItems.Clear();
            dropdownItems.AddRange(skins.GetAllUsableSkins());
            skinDropdown.Items = dropdownItems;
        });

        private void skinsChanged(IRealmCollection<SkinInfo> sender, ChangeSet changes)
        {
            // This can only mean that realm is recycling, else we would see the protected skins.
            // Because we are using `Live<>` in this class, we don't need to worry about this scenario too much.
            if (!sender.Any())
                return;
            // For simplicity repopulate the full list.
            dropdownItems.Clear();
            dropdownItems.AddRange(skins.GetAllUsableSkins());

            Schedule(() => skinDropdown.Items = dropdownItems);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            realmSubscription?.Dispose();

            // Unhook the pinned-store event so we don't get callbacks
            // landing on a disposed section if something else mutates the
            // sidecar after the settings panel closes.
            if (skins != null)
                skins.PinnedSkins.Changed -= refreshSkinList;
        }

        private partial class SkinDropdown : FormDropdown<Live<SkinInfo>>
        {
            [Resolved]
            private SkinManager skinManager { get; set; }

            protected override LocalisableString GenerateItemText(Live<SkinInfo> item)
            {
                bool pinned = skinManager.PinnedSkins.IsPinned(item.ID);
                return item.PerformRead(s => pinned ? $"♥ {s}" : s.ToString());
            }
        }

        public partial class SkinFavouriteButton : SettingsButtonV2
        {
            [Resolved]
            private SkinManager skins { get; set; }

            private Bindable<Skin> currentSkin;
            private HeartIcon heart;

            [BackgroundDependencyLoader]
            private void load()
            {
                Action = togglePin;

                Content.Add(heart = new HeartIcon
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    X = 16,
                    Size = new Vector2(16),
                });
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                currentSkin = skins.CurrentSkin.GetBoundCopy();
                currentSkin.BindValueChanged(_ => updateState());
                currentSkin.BindDisabledChanged(_ => updateState(), true);

                // Mirror pin-state changes that come from anywhere — not just
                // our own button. Any external mutation (a future hotkey, a
                // different settings surface, the realm-downgrade restore
                // path that calls ReplaceAll) refreshes the heart without
                // us having to thread an explicit refresh through it.
                skins.PinnedSkins.Changed += onPinnedChanged;
            }

            private void onPinnedChanged() => Schedule(() => updateState(withAnimation: false));

            protected override void Dispose(bool isDisposing)
            {
                base.Dispose(isDisposing);
                if (skins != null)
                    skins.PinnedSkins.Changed -= onPinnedChanged;
            }

            private void updateState(bool withAnimation = false)
            {
                bool currentlyPinned = skins.PinnedSkins.IsPinned(currentSkin.Value.SkinInfo.ID);
                heart.SetActive(currentlyPinned, withAnimation);
                Text = currentlyPinned ? SkinSettingsStrings.UnpinSkin : SkinSettingsStrings.PinSkin;
                Enabled.Value = !currentSkin.Disabled;
            }

            private void togglePin()
            {
                skins.TogglePinned(skins.CurrentSkinInfo.Value);
                // The Changed-event handler above will also refresh us
                // (without animation) but we still call updateState here
                // explicitly so the user's click animates the heart pop.
                // The HeartIcon's `if (this.active == active) return;`
                // guard makes the second invocation a no-op.
                updateState(withAnimation: skins.PinnedSkins.IsPinned(currentSkin.Value.SkinInfo.ID));
            }
        }

        public partial class RenameSkinButton : SettingsButtonV2, IHasPopover
        {
            [Resolved]
            private SkinManager skins { get; set; }

            private Bindable<Skin> currentSkin;

            [BackgroundDependencyLoader]
            private void load()
            {
                Text = CommonStrings.Rename;
                Action = this.ShowPopover;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                currentSkin = skins.CurrentSkin.GetBoundCopy();
                currentSkin.BindValueChanged(_ => updateState());
                currentSkin.BindDisabledChanged(_ => updateState(), true);
            }

            private void updateState() => Enabled.Value = !currentSkin.Disabled && currentSkin.Value.SkinInfo.PerformRead(s => !s.Protected);

            public Popover GetPopover()
            {
                return new RenameSkinPopover();
            }
        }

        public partial class ExportSkinButton : SettingsButtonV2
        {
            [Resolved]
            private SkinManager skins { get; set; }

            private Bindable<Skin> currentSkin;

            [BackgroundDependencyLoader]
            private void load()
            {
                Text = CommonStrings.Export;
                Action = export;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                currentSkin = skins.CurrentSkin.GetBoundCopy();
                currentSkin.BindValueChanged(_ => updateState());
                currentSkin.BindDisabledChanged(_ => updateState(), true);
            }

            private void updateState() => Enabled.Value = !currentSkin.Disabled && currentSkin.Value.SkinInfo.PerformRead(s => !s.Protected);

            private void export()
            {
                try
                {
                    skins.ExportCurrentSkin();
                }
                catch (Exception e)
                {
                    Logger.Log($"Could not export current skin: {e.Message}", level: LogLevel.Error);
                }
            }
        }

        public partial class DeleteSkinButton : DangerousSettingsButtonV2
        {
            [Resolved]
            private SkinManager skins { get; set; }

            [Resolved(CanBeNull = true)]
            private IDialogOverlay dialogOverlay { get; set; }

            private Bindable<Skin> currentSkin;

            [BackgroundDependencyLoader]
            private void load()
            {
                Text = WebCommonStrings.ButtonsDelete;
                Action = delete;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                currentSkin = skins.CurrentSkin.GetBoundCopy();
                currentSkin.BindValueChanged(_ => updateState());
                currentSkin.BindDisabledChanged(_ => updateState(), true);
            }

            private void updateState() => Enabled.Value = !currentSkin.Disabled && currentSkin.Value.SkinInfo.PerformRead(s => !s.Protected);

            private void delete()
            {
                dialogOverlay?.Push(new SkinDeleteDialog(currentSkin.Value));
            }
        }

        public partial class RenameSkinPopover : OsuPopover
        {
            [Resolved]
            private SkinManager skins { get; set; }

            private readonly FocusedTextBox textBox;

            public RenameSkinPopover()
            {
                AutoSizeAxes = Axes.Both;
                Origin = Anchor.TopCentre;

                RoundedButton renameButton;

                Child = new FillFlowContainer
                {
                    Direction = FillDirection.Vertical,
                    AutoSizeAxes = Axes.Y,
                    Width = 250,
                    Spacing = new Vector2(10f),
                    Children = new Drawable[]
                    {
                        textBox = new FocusedTextBox
                        {
                            PlaceholderText = SkinSettingsStrings.SkinName,
                            FontSize = OsuFont.DEFAULT_FONT_SIZE,
                            RelativeSizeAxes = Axes.X,
                            SelectAllOnFocus = true,
                        },
                        renameButton = new RoundedButton
                        {
                            Height = 40,
                            RelativeSizeAxes = Axes.X,
                            MatchingFilter = true,
                            Text = WebCommonStrings.ButtonsSave,
                        }
                    }
                };

                renameButton.Action += rename;
                textBox.OnCommit += (_, _) => rename();
            }

            protected override void PopIn()
            {
                textBox.Text = skins.CurrentSkinInfo.Value.Value.Name;
                textBox.TakeFocus();

                base.PopIn();
            }

            private void rename()
            {
                skins.Rename(skins.CurrentSkinInfo.Value, textBox.Text);
                PopOut();
            }
        }
    }
}
