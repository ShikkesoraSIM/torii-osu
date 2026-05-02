// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
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

        // Resolved so the locked-perk notification can route the user to
        // Ko-fi when activated. Optional because this subsection also runs
        // in test scenes where no OsuGame host is registered.
        [Resolved(CanBeNull = true)]
        private OsuGame? game { get; set; }

        // Single point of truth for the Ko-fi page. Touched in two places:
        // the locked-perk toast and (potentially) future supporter CTAs.
        private const string supporter_kofi_url = @"https://ko-fi.com/toriiserver";

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
        // explaining why the control didn't respond AND making the toast
        // itself the call-to-action: clicking the notification opens the
        // Ko-fi page in the user's browser. Returning true from Activated
        // dismisses the toast on click, which feels right because the user
        // has now seen the message and acted on it.
        private void onSupporterFeatureLockedClick()
        {
            notifications?.Post(new SimpleNotification
            {
                Icon = FontAwesome.Solid.Heart,
                Text = "Custom accent hue is a Torii Supporter perk. Click here to support and unlock!",
                Activated = () =>
                {
                    // LinkWarnMode.NeverWarn skips the "you're about to leave
                    // osu!" interstitial — appropriate here because the URL
                    // is a constant we control, not user-supplied.
                    game?.OpenUrlExternally(supporter_kofi_url, LinkWarnMode.NeverWarn);
                    return true;
                },
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
        // click. The visible affordance is a small pill anchored to the
        // CentreRight of the row: a lock glyph and a "SUPPORTER" label
        // with a soft pink-tinted background. The previous design used a
        // lonely 14px lock in the top-right corner, which read as a stray
        // decoration rather than a clear "this is gated" signal — and made
        // it ambiguous whether the row was disabled or actually locked.
        //
        // Pill is bumped on hover (alpha + glow) so the user can see that
        // it's interactive even before they click.
        private partial class LockOverlay : ClickableContainer
        {
            public bool Locked { get; set; }

            private readonly Container pill;
            private readonly Box pillBackground;

            // Soft pink with low alpha — matches the "supporter" pink
            // accent the donor badge already uses elsewhere, so the cue
            // reads as "this belongs to the supporter feature family"
            // without screaming for attention.
            private static readonly osuTK.Graphics.Color4 pill_colour =
                Color4Extensions.FromHex("#FF66B3");

            public LockOverlay(System.Action onClick)
            {
                RelativeSizeAxes = Axes.Both;
                AlwaysPresent = true;
                Action = () =>
                {
                    if (Locked)
                        onClick();
                };

                Child = pill = new Container
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    Margin = new MarginPadding { Right = 14 },
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 10,
                    Children = new Drawable[]
                    {
                        pillBackground = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = pill_colour.Opacity(0.18f),
                        },
                        new FillFlowContainer
                        {
                            AutoSizeAxes = Axes.Both,
                            Direction = FillDirection.Horizontal,
                            Spacing = new osuTK.Vector2(6, 0),
                            Padding = new MarginPadding { Horizontal = 10, Vertical = 5 },
                            Children = new Drawable[]
                            {
                                new SpriteIcon
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Icon = FontAwesome.Solid.Lock,
                                    Size = new osuTK.Vector2(11),
                                    Colour = pill_colour,
                                },
                                new OsuSpriteText
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Text = "SUPPORTER",
                                    Font = OsuFont.GetFont(size: 10, weight: FontWeight.Bold),
                                    Colour = pill_colour,
                                },
                            },
                        },
                    },
                };
            }

            // Block hover events too, otherwise the inner picker's hover
            // animations would still play even though clicks were eaten,
            // which felt weirdly inconsistent in testing.
            protected override bool OnHover(HoverEvent e)
            {
                if (!Locked)
                    return false;

                // Brighten the pill slightly so the user sees "I CAN click this"
                // before they actually do.
                pillBackground.FadeColour(pill_colour.Opacity(0.32f), 150, Easing.OutQuint);
                pill.ScaleTo(1.04f, 150, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                pillBackground.FadeColour(pill_colour.Opacity(0.18f), 200, Easing.OutQuint);
                pill.ScaleTo(1f, 200, Easing.OutQuint);
                base.OnHoverLost(e);
            }

            protected override bool Handle(UIEvent e) => Locked && base.Handle(e);
        }
    }
}
