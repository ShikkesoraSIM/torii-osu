// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    public partial class ToriiInterfaceSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Interface";

        [Resolved(CanBeNull = true)]
        private IAPIProvider? api { get; set; }

        [Resolved(CanBeNull = true)]
        private INotificationOverlay? notifications { get; set; }

        // The accent picker + its enable toggle are always rendered. When
        // the local user isn't a supporter we keep them visible (so non-
        // donators can SEE the feature exists) but lock them — clicking
        // the lock-skin posts a notification telling the user how to
        // unlock the feature instead of letting them mutate the setting.
        private SupporterLockedSlot? accentEnableSlot;
        private SupporterLockedSlot? accentPickerSlot;
        private IBindable<APIUser>? localUser;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.EnableCustomUIHue,
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIHueEnabled),
                })
                {
                    Keywords = new[] { @"colour", @"color", @"hue", @"theme", @"accent", @"torii" },
                },
                new SettingsItemV2(new FormHuePicker
                {
                    Caption = UserInterfaceStrings.CustomUIHue,
                    Current = config.GetBindable<float>(OsuSetting.CustomUIHue),
                    HintText = UserInterfaceStrings.CustomUIHue,
                })
                {
                    Keywords = new[] { @"colour", @"color", @"hue", @"theme", @"accent", @"torii" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.ApplyHueToMenu,
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToMenu),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.ApplyHueToOverlays,
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToOverlays),
                }),
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.ApplyHueToSettingsPanel,
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToSettingsPanel),
                }),
            };

            // Donator-only accent picker — built ALWAYS, optionally locked.
            // Wrapping in SupporterLockedSlot lets us greyscale + intercept
            // input without rebuilding/reflowing the panel when the user
            // logs in mid-session and the lock falls off.
            accentEnableSlot = new SupporterLockedSlot(new SettingsItemV2(new FormCheckBox
            {
                Caption = "Use a separate accent hue",
                Current = config.GetBindable<bool>(OsuSetting.CustomUIAccentEnabled),
            })
            {
                Keywords = new[] { @"colour", @"color", @"hue", @"accent", @"supporter", @"donator" },
            }, () => onSupporterFeatureLockedClick());

            accentPickerSlot = new SupporterLockedSlot(new SettingsItemV2(new FormHuePicker
            {
                Caption = "Accent hue",
                Current = config.GetBindable<float>(OsuSetting.CustomUIAccentHue),
                HintText = "Hue applied to highlights, hovers and accent colours",
            })
            {
                Keywords = new[] { @"colour", @"color", @"hue", @"accent", @"supporter", @"donator" },
            }, () => onSupporterFeatureLockedClick());

            Add(accentEnableSlot);
            Add(accentPickerSlot);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (api == null)
            {
                lockSlots();
                return;
            }

            localUser = api.LocalUser.GetBoundCopy();
            localUser.BindValueChanged(_ => updateSupporterLock(), true);
        }

        private void updateSupporterLock()
        {
            // "Donator" tier = current OR past supporter. The server exposes
            // both via APIUser.IsSupporter (current tag) and HasSupported
            // (true if they ever subscribed). Either grants access to the
            // cosmetic accent picker.
            var user = localUser?.Value;
            bool donator = user != null && (user.IsSupporter || user.HasSupported);

            if (donator)
                unlockSlots();
            else
                lockSlots();
        }

        private void unlockSlots()
        {
            accentEnableSlot?.SetLocked(false);
            accentPickerSlot?.SetLocked(false);
        }

        private void lockSlots()
        {
            accentEnableSlot?.SetLocked(true);
            accentPickerSlot?.SetLocked(true);
        }

        // Click handler shared by both locked slots. Posts a SimpleNotification
        // so the user gets a clear, non-blocking explanation of why the
        // control didn't respond. Title + body match the user's request:
        // a single sentence that names the feature and tells them how to
        // unlock it.
        private void onSupporterFeatureLockedClick()
        {
            notifications?.Post(new SimpleNotification
            {
                Icon = FontAwesome.Solid.Lock,
                Text = "Custom accent hue is a Torii Supporter perk — drop a tip on shikkesora.com to enable it!",
            });
        }

        public override IEnumerable<LocalisableString> FilterTerms => base.FilterTerms;

        // -----------------------------------------------------------------
        // SupporterLockedSlot
        //
        // Container that wraps a settings item and toggles between an
        // unlocked (full-opacity, fully interactive) and a locked (50%
        // opacity, click-intercepting + lock-icon corner badge) state.
        // We can't simply call .Disabled on every nested control because
        // FormHuePicker's underlying bindable would refuse to mutate but
        // the popover would still open — so we instead overlay a hit
        // surface that swallows clicks, drags, and hovers when locked.
        // -----------------------------------------------------------------
        private partial class SupporterLockedSlot : Container
        {
            private readonly Drawable inner;
            private readonly System.Action onLockedClick;
            private readonly LockOverlay lockOverlay;

            public SupporterLockedSlot(Drawable inner, System.Action onLockedClick)
            {
                this.inner = inner;
                this.onLockedClick = onLockedClick;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;

                Children = new[]
                {
                    inner,
                    lockOverlay = new LockOverlay(this.onLockedClick)
                    {
                        Alpha = 0,
                    },
                };
            }

            public void SetLocked(bool locked)
            {
                inner.FadeTo(locked ? 0.5f : 1f, 200, Easing.OutQuint);
                lockOverlay.FadeTo(locked ? 1f : 0f, 200, Easing.OutQuint);
                lockOverlay.Locked = locked;
            }
        }

        // Transparent overlay that fills the slot when the feature is locked.
        // Catches positional input and triggers the supplied callback on
        // click, plus paints a small lock badge in the top-right so the
        // lock state is unambiguous (the 50% fade alone could be confused
        // with the "disabled because the feature toggle is off" pattern
        // used elsewhere in the panel).
        private partial class LockOverlay : ClickableContainer
        {
            public bool Locked { get; set; }

            public LockOverlay(System.Action onClick)
            {
                RelativeSizeAxes = Axes.Both;
                AlwaysPresent = true;
                Action = () =>
                {
                    if (Locked)
                        onClick();
                };

                Child = new Container
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding { Top = 12, Right = 12 },
                    AutoSizeAxes = Axes.Both,
                    Child = new SpriteIcon
                    {
                        Icon = FontAwesome.Solid.Lock,
                        Size = new osuTK.Vector2(14),
                        Colour = OsuColour.Gray(0.85f),
                    },
                };
            }

            // Block hover events too, otherwise the inner picker's hover
            // animations would still play even though clicks were eaten,
            // which felt weirdly inconsistent in testing.
            protected override bool OnHover(HoverEvent e) => Locked;
            protected override bool Handle(UIEvent e) => Locked && base.Handle(e);
        }
    }
}
