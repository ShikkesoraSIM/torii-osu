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
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Overlays.Cosmetics;
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

        // Resolved so the lock can send the user to the cosmetic store, where
        // the accent hue is bought with points. Optional because this
        // subsection also runs in test scenes with no store registered.
        [Resolved(CanBeNull = true)]
        private CosmeticStoreOverlay? cosmeticStore { get; set; }

        // The accent picker + its enable toggle are always rendered. Until the
        // accent unlock is bought in the store we keep them visible (so everyone
        // can SEE the feature exists) but locked - clicking the lock opens the
        // store at the accent unlock instead of mutating the setting. Gated on a
        // points purchase now, NOT a supporter tier.
        private SupporterLockedSlot? accentEnableSlot;
        private SupporterLockedSlot? accentPickerSlot;
        private Bindable<bool>? accentUnlocked;

        // NSFW profile media toggle state. The value is authoritative
        // server-side (lives in UserPreference.profile_media_show_nsfw)
        // because the *server* substitutes avatar / cover URLs with
        // placeholders before they leave the API — a client-only toggle
        // would do nothing because we couldn't unsubstitute what we never
        // got. We fetch the current value at load and PATCH on toggle.
        //
        // suppressNsfwPatch gates the BindValueChanged handler so the
        // initial GET response setting the bindable doesn't echo back
        // as a PATCH for the same value the server already has.
        private readonly Bindable<bool> nsfwProfileMediaBindable = new BindableBool();
        private bool suppressNsfwPatch;

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            Children = new Drawable[]
            {
                // NSFW profile media toggle. Mirrors the web's User
                // Preferences page so users get the same control in-game
                // they have on the website. Lives at the very top of the
                // Torii Interface subsection because:
                //   - it's a content-display preference (matches the
                //     "what do I see" theme of this whole subsection),
                //   - it's the only ingame surface for this preference
                //     (no other entry point), so making users scroll
                //     past cursor + theme settings just to find it
                //     would be a UX miss,
                //   - on first visit, the GET below populates the
                //     checkbox a frame after the panel opens; the user
                //     sees the live state instead of stale defaults.
                // Initial state populates from the GET fired in
                // LoadComplete; toggling fires a PATCH back to the
                // server (see LoadComplete for the wiring).
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Show NSFW profile media",
                    HintText = "Show real avatars and covers for users who've flagged their profile as NSFW. "
                               + "When off (default), those users render with placeholders instead. "
                               + "This mirrors the same toggle on the website's user preferences page; "
                               + "changing it here saves to your account immediately.",
                    Current = nsfwProfileMediaBindable,
                    NewFeatureId = NewFeatureRegistry.NsfwProfileMedia,
                    ShowExplicitContentBadge = true,
                })
                {
                    Keywords = new[] { @"nsfw", @"18+", @"sensitive", @"avatar", @"cover", @"profile", @"media", @"explicit", @"offensive" },
                },
                // Torii: cosmetic UI-theme dropdown ("Torii" vs
                // "Grayscale by fsyori"). Mirrored from Settings → Skin
                // — same bindable, same restart-confirm — placed at
                // the top of the Torii Interface subsection because
                // users browsing Torii-specific chrome prefs are the
                // primary audience for it.
                new UIThemeDropdownAndRestart(),
                // Torii: menu cursor style is the only torii-specific
                // cursor knob (Lazer Default / Skin Cursor / Torii Cursor)
                // so it's the only one we surface inside Torii Interface.
                // The shape / scale / rotation cursor sliders used to be
                // duplicated here too, but the user found that wasteful —
                // those still live at Settings → User Interface → General
                // (the canonical lazer home), reachable via the settings
                // search.
                new SettingsItemV2(new FormEnumDropdown<osu.Game.Graphics.Cursor.MenuCursorStyle>
                {
                    Caption = UserInterfaceStrings.MenuCursorStyle,
                    Current = config.GetBindable<osu.Game.Graphics.Cursor.MenuCursorStyle>(OsuSetting.MenuCursorStyle),
                })
                {
                    Keywords = new[] { @"cursor", @"gameplay", @"skin", @"torii", @"style" },
                },

                // Custom UI hue — a single master toggle + the hue picker.
                // Previously this section also exposed three separate
                // "Apply hue to menu / overlays / settings panel" toggles,
                // but in practice everybody enabled the master and left
                // the three sub-toggles on; the granularity was clutter,
                // not flexibility. The sub-toggles still exist as
                // bindables (other code reads them) — they're now driven
                // entirely by the master in LoadComplete: master ON
                // forces all three ON, master OFF forces all three OFF.
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = UserInterfaceStrings.EnableCustomUIHue,
                    Current = config.GetBindable<bool>(OsuSetting.CustomUIHueEnabled),
                })
                {
                    Keywords = new[] { @"colour", @"color", @"hue", @"theme", @"accent", @"torii", @"menu", @"overlay", @"settings", @"apply" },
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
            }, () => onAccentLockedClick());

            accentPickerSlot = new SupporterLockedSlot(new SettingsItemV2(new FormHuePicker
            {
                Caption = "Accent hue",
                Current = config.GetBindable<float>(OsuSetting.CustomUIAccentHue),
                HintText = "Hue applied to highlights, hovers and accent colours",
            })
            {
                Keywords = new[] { @"colour", @"color", @"hue", @"accent", @"supporter", @"donator" },
            }, () => onAccentLockedClick());

            Add(accentEnableSlot);
            Add(accentPickerSlot);

            // Bindable that drives the lock: flips off the instant the accent
            // unlock is bought in the store (or synced from the server).
            accentUnlocked = config.GetBindable<bool>(OsuSetting.CustomUIAccentUnlocked);

            // Custom UI hue master → sub-toggle propagation. The three
            // ApplyHueTo* bindables (menu / overlays / settings panel)
            // are still consumed by the chrome that actually paints
            // those surfaces — they just don't have UI of their own any
            // more. Whenever the master toggle moves, push the same
            // value into all three so they stay in lock-step. Fires
            // immediately on bind to bring stale configs (where the
            // user had toggled the sub-flags independently in an
            // earlier release) back into a consistent "all three match
            // master" state on first launch of this version.
            //
            // Direction is intentionally master → subs only. There's no
            // remaining UI that toggles a single sub-flag, so we don't
            // need to listen for sub-flag changes — and if other code
            // ever did, the master would still be honoured as the
            // user-visible source of truth on the next toggle.
            var customUiHueEnabled = config.GetBindable<bool>(OsuSetting.CustomUIHueEnabled);
            var applyToMenu = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToMenu);
            var applyToOverlays = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToOverlays);
            var applyToSettings = config.GetBindable<bool>(OsuSetting.CustomUIHueApplyToSettingsPanel);
            // Defer to the update thread via Schedule(): the immediate
            // (runOnceImmediately) firing happens on the BDL thread, and
            // assignment cascades into downstream listeners that animate
            // hue-tinted drawables (the menu chrome, overlay backgrounds,
            // settings-panel accents). Those listeners mutate Transforms
            // on Ready drawables, which the framework only permits from
            // the load or update threads -- doing it from BDL throws
            // InvalidThreadForMutationException and the entire Torii
            // settings section fails to load, which presents to the user
            // as "Settings won't open" (Android especially, where the
            // unhandled-exception ceiling is hit faster on the smaller
            // dispatch budget). Wrapping in Schedule queues the same
            // propagation onto the update thread where it's legal.
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

            // Accent lock: gated on the points-store unlock now, not supporter.
            // Fires immediately and whenever the unlock flips (e.g. right after
            // buying it in the store) so the lock falls off live.
            accentUnlocked?.BindValueChanged(_ => updateAccentLock(), true);

            if (api == null)
                return;

            // NSFW profile media wiring. Two halves:
            //
            // 1) GET the current preference from the server to populate
            //    the checkbox so users opening the panel see their real
            //    saved state. We gate the change handler with
            //    suppressNsfwPatch so the value assignment from this
            //    callback doesn't echo back as a redundant PATCH.
            //
            // 2) On user toggle, PATCH the new value. Failure shows a
            //    notification and reverts the visual state so the
            //    checkbox doesn't lie about server-side reality.
            //
            // Both branches are best-effort — a logged-out client or a
            // server outage leaves the checkbox at its default-false
            // state and toggling does nothing dramatic (the PATCH just
            // fails, which we surface to the user).
            var prefRequest = new GetToriiUserPreferencesRequest();
            prefRequest.Success += response =>
            {
                Schedule(() =>
                {
                    suppressNsfwPatch = true;
                    nsfwProfileMediaBindable.Value = response.ProfileMediaShowNsfw ?? false;
                    suppressNsfwPatch = false;
                });
            };
            // No Failure handler: leaving the toggle at default-false
            // when the GET fails is a reasonable fallback — if the user
            // then toggles, the PATCH will surface its own error.
            api.Queue(prefRequest);

            nsfwProfileMediaBindable.BindValueChanged(e =>
            {
                if (suppressNsfwPatch) return;
                if (api == null) return;

                var patch = PatchToriiUserPreferencesRequest.ProfileMediaShowNsfw(e.NewValue);
                patch.Failure += _ =>
                {
                    // Revert the visual toggle so the checkbox stays
                    // honest about server-side state. Schedule because
                    // Failure can fire off the update thread.
                    Schedule(() =>
                    {
                        suppressNsfwPatch = true;
                        nsfwProfileMediaBindable.Value = e.OldValue;
                        suppressNsfwPatch = false;

                        notifications?.Post(new SimpleErrorNotification
                        {
                            Text = "Couldn't save your NSFW profile media preference. Try again in a moment.",
                        });
                    });
                };
                api.Queue(patch);
            });
        }

        private void updateAccentLock()
        {
            if (accentUnlocked?.Value == true)
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

        // Click handler shared by both locked slots. The accent hue is a
        // points-store unlock now, so clicking the lock takes the user straight
        // to it in the cosmetic store (opening the store and scrolling to the
        // unlock) instead of mutating the still-locked setting.
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

            // Neutral grey, low alpha. The lock is no longer a "supporter" cue
            // (the accent is bought with points in the store now), so it doesn't
            // carry the supporter-pink branding any more - just a plain "locked"
            // affordance that stays legible over the row.
            private static readonly osuTK.Graphics.Color4 pill_colour =
                Color4Extensions.FromHex("#9AA0AA");

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
