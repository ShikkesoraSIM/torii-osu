// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
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
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays.Cosmetics;
using osu.Game.Overlays.Notifications;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Settings.Sections.Torii
{
    public partial class ToriiInterfaceSettings : SettingsSubsection
    {
        protected override LocalisableString Header => "Interface";

        [Resolved(CanBeNull = true)]
        private IAPIProvider? api { get; set; }

        [Resolved(CanBeNull = true)]
        private INotificationOverlay? notifications { get; set; }

        // NSFW profile-media preference is server-authoritative (the server swaps
        // avatar/cover URLs for placeholders before they leave the API), so we GET
        // the current value on load and PATCH on toggle. suppressNsfwPatch stops the
        // initial GET assignment from echoing back as a redundant PATCH.
        private readonly Bindable<bool> nsfwProfileMediaBindable = new BindableBool();
        private bool suppressNsfwPatch;

        // Resolved so the lock can send the user to the cosmetic store, where the
        // accent hue is bought with points. Optional because this subsection also
        // runs in test scenes with no store registered.
        [Resolved(CanBeNull = true)]
        private CosmeticStoreOverlay? cosmeticStore { get; set; }

        // The accent picker + its enable toggle are always rendered. Until the
        // accent unlock is bought in the store they stay visible (so everyone can
        // SEE the feature exists) but LOCKED — clicking the lock opens the store at
        // the accent unlock instead of mutating the setting. The unlock itself is
        // synced from server ownership (ToolbarPointsButton -> SyncOwned).
        private SupporterLockedSlot? accentEnableSlot;
        private SupporterLockedSlot? accentPickerSlot;
        private Bindable<bool>? accentUnlocked;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                // NSFW profile media — mirrors the website's User Preferences toggle so
                // users get the same control in-game. Server-authoritative (see LoadComplete).
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Show NSFW profile media",
                    HintText = "Show avatars and cover images from profiles flagged as NSFW. Saved to your account.",
                    Current = nsfwProfileMediaBindable,
                    NewFeatureId = NewFeatureRegistry.NsfwProfileMedia,
                    ShowExplicitContentBadge = true,
                })
                {
                    Keywords = new[] { @"nsfw", @"explicit", @"adult", @"profile", @"media", @"avatar", @"cover", @"18" },
                },
                new UIThemeDropdownAndRestart(),
                // menu cursor style: mirroreado aca (tambien vive en UI > General, mismo key) asi el
                // que toca cosmeticos lo encuentra cerca.
                new SettingsItemV2(new FormEnumDropdown<osu.Game.Graphics.Cursor.MenuCursorStyle>
                {
                    Caption = @"Menu cursor style",
                    Current = config.GetBindable<osu.Game.Graphics.Cursor.MenuCursorStyle>(OsuSetting.MenuCursorStyle),
                })
                {
                    Keywords = new[] { @"cursor", @"gameplay", @"skin", @"torii", @"style" },
                },
                // toggle del server pulse (la pill del toolbar con currently-playing / plays-per-minute).
                // mismo bindable que ToriiServerPulseButton, apagarlo corta el polling al toque.
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Show server pulse on toolbar",
                    Current = config.GetBindable<bool>(OsuSetting.ToriiServerPulseEnabled),
                })
                {
                    Keywords = new[] { @"pulse", @"toolbar", @"server", @"activity", @"online", @"live", @"torii", @"playing", @"heartbeat" },
                },
                new PotatoModeToggleAndRestart(),
                new SettingsItemV2(new FormEnumDropdown<ToriiInputAudioHzMode>
                {
                    Caption = "Input/audio thread rate",
                    HintText = "How fast the input, audio and update threads run. Higher rates suit high-polling-rate mice (e.g. 8000 Hz) but cost more CPU. 2000 Hz is a safe default. Applies instantly.",
                    Current = config.GetBindable<ToriiInputAudioHzMode>(OsuSetting.ToriiInputAudioHz),
                    NewFeatureId = NewFeatureRegistry.InputAudioHz,
                })
                {
                    Keywords = new[] { @"hz", @"polling", @"rate", @"input", @"audio", @"thread", @"latency", @"8000", @"performance" },
                },
                // stable song select: es una feature de UI (cambia todo el chrome del song select al look
                // stable), por eso vive aca en Interface y no en Gameplay. va pegada al auto-hide toolbar
                // porque se complementan (el hint del auto-hide menciona el stable song select).
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Legacy (stable-style) song select",
                    Current = config.GetBindable<bool>(OsuSetting.ToriiLegacyFooterUseSkin),
                    HintText = "Makes song select look like osu!stable: a skinnable legacy footer (back / mode / mods / random / options "
                               + "+ your rank panel) and the modern filter/sort bar and info wedges hidden. Turn off for the standard lazer UI.",
                    NewFeatureId = NewFeatureRegistry.LegacyFooterSkin,
                })
                {
                    Keywords = new[] { @"footer", @"skin", @"song", @"select", @"legacy", @"bottom", @"buttons", @"torii", @"stable" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Auto-hide toolbar",
                    HintText = "Keep the top toolbar hidden and only reveal it when you move the cursor to the very top of the screen, then it tucks away again on its own. Great with the stable song select, where a hidden toolbar gives the full classic layout.",
                    Current = config.GetBindable<bool>(OsuSetting.ToriiAutoHideToolbar),
                    NewFeatureId = NewFeatureRegistry.AutoHideToolbar,
                })
                {
                    Keywords = new[] { @"toolbar", @"taskbar", @"navbar", @"auto", @"hide", @"reveal", @"hover", @"immersive", @"torii" },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Custom UI hue",
                    HintText = "Tint the UI (menus, overlays, settings) to a custom hue instead of the theme default.",
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIHueEnabled),
                })
                {
                    Keywords = new[] { @"hue", @"colour", @"color", @"accent", @"tint", @"theme" },
                },
                new SettingsItemV2(new FormHuePicker
                {
                    Caption = "UI hue",
                    HintText = "Base hue applied across the UI when custom hue is enabled.",
                    Current = config.GetBindable<float>(OsuSetting.CustomUIHue),
                })
                {
                    Keywords = new[] { @"hue", @"colour", @"color", @"tint" },
                },
            };

            // Accent controls: always visible, locked until the store unlock is owned.
            accentEnableSlot = new SupporterLockedSlot(new SettingsItemV2(new FormCheckBox
            {
                Caption = "Separate accent hue",
                HintText = "Use a different hue for highlights, hovers and accents. Unlocked in the cosmetic store.",
                Current = config.GetBindable<bool>(OsuSetting.CustomUIAccentEnabled),
            })
            {
                Keywords = new[] { @"accent", @"hue", @"highlight", @"hover", @"unlock", @"store" },
            }, onAccentLockedClick);

            accentPickerSlot = new SupporterLockedSlot(new SettingsItemV2(new FormHuePicker
            {
                Caption = "Accent hue",
                HintText = "Hue applied to highlights, hovers and accent colours.",
                Current = config.GetBindable<float>(OsuSetting.CustomUIAccentHue),
            })
            {
                Keywords = new[] { @"accent", @"hue", @"highlight", @"unlock", @"store" },
            }, onAccentLockedClick);

            Add(accentEnableSlot);
            Add(accentPickerSlot);

            accentUnlocked = config.GetBindable<bool>(OsuSetting.CustomUIAccentUnlocked);

            // Master "Custom UI hue" toggle drives all three per-scope apply
            // flags (menu / overlays / settings panel) in lock-step: master ON
            // forces all three ON, master OFF forces all three OFF. Without this
            // the menu/toolbar never tints (its flag defaults off) and stale
            // configs can end up inconsistent. Schedule() defers off the BDL
            // thread because the cascade animates hue-tinted drawables, which
            // the framework only allows to mutate from the load/update threads.
            var customUiHueEnabled = config.GetBindable<bool>(OsuSetting.CustomUIHueEnabled);
            var applyToMenu = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToMenu);
            var applyToOverlays = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToOverlays);
            var applyToSettings = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToSettingsPanel);

            customUiHueEnabled.BindValueChanged(e => Schedule(() =>
            {
                applyToMenu.Value = e.NewValue;
                applyToOverlays.Value = e.NewValue;
                applyToSettings.Value = e.NewValue;
            }), true);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Accent lock falls off the instant the unlock is bought in the store
            // (or synced from the server on login). Fires immediately too so the
            // initial locked/unlocked state is correct.
            accentUnlocked?.BindValueChanged(_ => updateAccentLock(), true);

            if (api == null)
                return;

            // NSFW profile media: (1) GET the saved value so the checkbox reflects the
            // user's real state (suppress the echo PATCH on that assignment); (2) PATCH
            // on toggle, reverting + notifying on failure so the checkbox never lies.
            var prefRequest = new GetToriiUserPreferencesRequest();
            prefRequest.Success += response => Schedule(() =>
            {
                suppressNsfwPatch = true;
                nsfwProfileMediaBindable.Value = response.ProfileMediaShowNsfw ?? false;
                suppressNsfwPatch = false;
            });
            api.Queue(prefRequest);

            nsfwProfileMediaBindable.BindValueChanged(e =>
            {
                if (suppressNsfwPatch || api == null)
                    return;

                var patch = PatchToriiUserPreferencesRequest.ProfileMediaShowNsfw(e.NewValue);
                patch.Failure += _ => Schedule(() =>
                {
                    suppressNsfwPatch = true;
                    nsfwProfileMediaBindable.Value = e.OldValue;
                    suppressNsfwPatch = false;

                    notifications?.Post(new SimpleErrorNotification
                    {
                        Text = "Couldn't save your NSFW profile media preference. Try again in a moment.",
                    });
                });
                api.Queue(patch);
            });
        }

        private void updateAccentLock()
        {
            bool unlocked = accentUnlocked?.Value == true;
            accentEnableSlot?.SetLocked(!unlocked);
            accentPickerSlot?.SetLocked(!unlocked);
        }

        // The accent hue is a points-store unlock, so clicking the lock takes the
        // user straight to it in the cosmetic store instead of mutating the
        // still-locked setting.
        private void onAccentLockedClick()
        {
            if (cosmeticStore != null)
                cosmeticStore.ShowAndScrollToAccentUnlock();
            else
                notifications?.Post(new SimpleNotification
                {
                    Icon = FontAwesome.Solid.Lock,
                    Text = "Unlock the custom accent hue in the cosmetic store.",
                });
        }

        // -----------------------------------------------------------------
        // SupporterLockedSlot — wraps a settings item and toggles between an
        // unlocked (full-opacity, interactive) and a locked (dimmed, click-
        // intercepting + "LOCKED" pill) state. We can't just .Disabled the inner
        // controls because FormHuePicker's popover would still open, so we overlay
        // a hit surface that swallows input when locked.
        // -----------------------------------------------------------------
        private partial class SupporterLockedSlot : Container
        {
            private readonly Drawable inner;
            private readonly LockOverlay lockOverlay;

            public SupporterLockedSlot(Drawable inner, Action onLockedClick)
            {
                this.inner = inner;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;

                Children = new[]
                {
                    inner,
                    lockOverlay = new LockOverlay(onLockedClick)
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

        // Transparent overlay that fills the slot when locked. Catches positional
        // input and triggers the callback on click; a centred "LOCKED" pill reads
        // the state at a glance.
        private partial class LockOverlay : ClickableContainer
        {
            public bool Locked { get; set; }

            private readonly Box scrim;
            private readonly Container pill;
            private readonly Box pillBackground;

            private static readonly Color4 pill_colour = Color4Extensions.FromHex("#9AA0AA");

            private const float scrim_alpha_idle = 0.10f;
            private const float scrim_alpha_hover = 0.18f;
            private const float pill_alpha_idle = 0.22f;
            private const float pill_alpha_hover = 0.36f;

            public LockOverlay(Action onClick)
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
                                Spacing = new Vector2(7, 0),
                                Padding = new MarginPadding { Horizontal = 12, Vertical = 6 },
                                Children = new Drawable[]
                                {
                                    new SpriteIcon
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Icon = FontAwesome.Solid.Lock,
                                        Size = new Vector2(12),
                                        Colour = pill_colour,
                                    },
                                    new OsuSpriteText
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Text = "LOCKED",
                                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                                        Colour = pill_colour,
                                    },
                                },
                            },
                        },
                    },
                };
            }

            // When not locked, refuse positional input entirely so the inner
            // picker/toggle sees the click (ClickableContainer with Action != null
            // would otherwise swallow it).
            public override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
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

            protected override bool Handle(UIEvent e) => Locked && base.Handle(e);
        }
    }
}
