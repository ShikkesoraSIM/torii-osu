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
                // Torii cursor controls — DUPLICATED here on purpose. The
                // canonical home is Settings → User Interface → General
                // (alongside the other lazer cursor knobs), but the user
                // also wants them surfaced inside the Torii section since
                // the "Use Torii cursor" option is a Torii-branded feature
                // and people browsing Torii-specific settings expect to
                // find it here. Both copies bind to the same OsuSetting
                // bindables, so changes round-trip live regardless of which
                // panel the user touched.
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.CursorRotation,
                    Current = config.GetBindable<bool>(OsuSetting.CursorRotation),
                })
                {
                    Keywords = new[] { @"cursor", @"spin", @"rotate", @"drag", @"torii" },
                },
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = UserInterfaceStrings.MenuCursorSize,
                    Current = config.GetBindable<float>(OsuSetting.MenuCursorSize),
                    KeyboardStep = 0.01f,
                    LabelFormat = v => $"{v:0.##}x",
                })
                {
                    Keywords = new[] { @"cursor", @"size", @"menu", @"torii" },
                },
                new SettingsItemV2(new FormEnumDropdown<osu.Game.Graphics.Cursor.MenuCursorStyle>
                {
                    Caption = UserInterfaceStrings.MenuCursorStyle,
                    Current = config.GetBindable<osu.Game.Graphics.Cursor.MenuCursorStyle>(OsuSetting.MenuCursorStyle),
                })
                {
                    Keywords = new[] { @"cursor", @"gameplay", @"skin", @"torii", @"style" },
                },

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

                // Server pulse widget — toolbar pill that shows live
                // "currently playing / plays per minute / top map" stats.
                // Bindable shared with ToriiServerPulseButton + Provider;
                // toggling here stops polling immediately.
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Show server pulse on toolbar",
                    Current = config.GetBindable<bool>(OsuSetting.ToriiServerPulseEnabled),
                })
                {
                    Keywords = new[] { @"pulse", @"toolbar", @"server", @"activity", @"online", @"live", @"torii", @"playing", @"heartbeat" },
                },
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
        // click. The previous designs (lonely corner lock, then right-side
        // pill) both visually clashed with the row's value chip, which
        // also lives on the right edge. Final design unifies the locked
        // state into a single full-row treatment:
        //
        //   - A rounded scrim covers the entire row width with a faint
        //     pink tint so the lock state reads at a glance even before
        //     the eye finds the label.
        //   - A "🔒 SUPPORTER" pill sits at the absolute centre of the
        //     row, never overlapping the caption or the value chip.
        //   - The whole overlay is one click target, so anywhere the
        //     user touches the row triggers the toast.
        //
        // Hover brightens BOTH the scrim and the pill, plus a tiny pill
        // scale, so the affordance reads as "this whole row is clickable",
        // not "click this small badge".
        private partial class LockOverlay : ClickableContainer
        {
            public bool Locked { get; set; }

            private readonly Box scrim;
            private readonly Container pill;
            private readonly Box pillBackground;

            // Soft pink with low alpha — matches the "supporter" pink
            // accent the donor badge already uses elsewhere, so the cue
            // reads as "this belongs to the supporter feature family"
            // without screaming for attention.
            private static readonly osuTK.Graphics.Color4 pill_colour =
                Color4Extensions.FromHex("#FF66B3");

            // Scrim alpha values are intentionally lower than the pill's so
            // the row content underneath stays legible — the goal is "you
            // can see what's locked", not "obscure the row".
            private const float scrim_alpha_idle = 0.10f;
            private const float scrim_alpha_hover = 0.18f;
            private const float pill_alpha_idle = 0.22f;
            private const float pill_alpha_hover = 0.36f;

            public LockOverlay(System.Action onClick)
            {
                RelativeSizeAxes = Axes.Both;
                AlwaysPresent = true;
                Masking = true;
                CornerRadius = 10;
                Action = () =>
                {
                    if (Locked)
                        onClick();
                };

                Children = new Drawable[]
                {
                    scrim = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = pill_colour.Opacity(scrim_alpha_idle),
                    },
                    pill = new Container
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 12,
                        Children = new Drawable[]
                        {
                            pillBackground = new Box
                            {
                                RelativeSizeAxes = Axes.Both,
                                Colour = pill_colour.Opacity(pill_alpha_idle),
                            },
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new osuTK.Vector2(7, 0),
                                Padding = new MarginPadding { Horizontal = 12, Vertical = 6 },
                                Children = new Drawable[]
                                {
                                    new SpriteIcon
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Icon = FontAwesome.Solid.Lock,
                                        Size = new osuTK.Vector2(12),
                                        Colour = pill_colour,
                                    },
                                    new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Text = "TORII SUPPORTER",
                                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                                        Colour = pill_colour,
                                    },
                                },
                            },
                        },
                    },
                };
            }

            // CRITICAL: when not locked, refuse positional input entirely so
            // the inner picker / toggle the supporter is trying to use sees
            // the click. ClickableContainer otherwise consumes any click
            // that lands on it (Action != null is treated as "consumed"),
            // which made the unlocked accent picker silently swallow taps —
            // it looked unlocked (no visible pill) but every interaction
            // dropped on the floor.
            public override bool ReceivePositionalInputAt(osuTK.Vector2 screenSpacePos)
                => Locked && base.ReceivePositionalInputAt(screenSpacePos);

            protected override bool OnHover(HoverEvent e)
            {
                if (!Locked)
                    return false;

                scrim.FadeColour(pill_colour.Opacity(scrim_alpha_hover), 150, Easing.OutQuint);
                pillBackground.FadeColour(pill_colour.Opacity(pill_alpha_hover), 150, Easing.OutQuint);
                pill.ScaleTo(1.04f, 150, Easing.OutQuint);
                return true;
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                scrim.FadeColour(pill_colour.Opacity(scrim_alpha_idle), 200, Easing.OutQuint);
                pillBackground.FadeColour(pill_colour.Opacity(pill_alpha_idle), 200, Easing.OutQuint);
                pill.ScaleTo(1f, 200, Easing.OutQuint);
                base.OnHoverLost(e);
            }

            // Block stray events (hover-into-content, scroll, etc) so the
            // inner picker doesn't react to input that was meant for this
            // overlay.
            protected override bool Handle(UIEvent e) => Locked && base.Handle(e);
        }
    }
}
