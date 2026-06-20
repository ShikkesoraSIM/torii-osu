// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays.Dialog;

namespace osu.Game.Overlays.Settings
{
    /// <summary>
    /// Cosmetic-chrome theme dropdown shared between Settings → Skin
    /// and Settings → Torii → Interface. Selecting a different value
    /// prompts the user to confirm a restart — the chrome palette is
    /// captured into every drawable at construction (see OsuColour.cs
    /// + OverlayColourProvider.cs), so a hot swap mid-run would leave
    /// the UI in a torn state where some surfaces use the old theme
    /// and others the new one. Same restart-dialog pattern the SDL3
    /// and Renderer toggles use upstream.
    ///
    /// Mounted at two call sites so users browsing skins (cosmetic
    /// surfaces) AND users browsing the Torii section (Torii-specific
    /// chrome) both find it where they'd expect. Both copies bind to
    /// the same <see cref="OsuSetting.UITheme"/> bindable, so a change
    /// in one place updates the other live.
    ///
    /// Single-handler invariant
    /// ------------------------
    /// The restart-confirm subscription is registered exactly once
    /// across all instances via <see cref="UIThemeRestartCoordinator"/>.
    /// First instance to load wires it up; the second silently no-ops
    /// the registration call. Without this guard, a dropdown change
    /// would fire BOTH instances' <see cref="Bindable{T}.BindValueChanged"/>
    /// callbacks back-to-back, and the second
    /// <c>game.AttemptExit()</c> would race with the first and
    /// Velopack's process-launch would throw "Cannot start process
    /// because a file name has not been provided" — visible as two
    /// stacked error toasts next to the confirm dialog.
    /// </summary>
    public partial class UIThemeDropdownAndRestart : CompositeDrawable
    {
        // Resolved optionally because the test-scene host doesn't
        // register OsuGame / IDialogOverlay. In that case we silently
        // bind the dropdown without the restart prompt — the test can
        // change the setting freely and observe the bindable directly.
        [Resolved(CanBeNull = true)]
        private OsuGame? game { get; set; }

        [Resolved(CanBeNull = true)]
        private IDialogOverlay? dialogOverlay { get; set; }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            var themeBindable = config.GetBindable<UIThemeOption>(OsuSetting.UITheme);

            // Torii: NewFeatureId is set on the dropdown ITSELF (not the
            // SettingsItemV2 wrapper) so the [NEW] badge renders inline
            // inside the dropdown header, immediately to the right of
            // the tooltip "?" icon. The previous wrapper-level approach
            // injected the badge as its own row above the card — visually
            // detached from the control it was meant to flag. The
            // interaction-based tracker dismisses the badge after the
            // user opens the dropdown twice (see NewFeatureTracker).
            InternalChild = new SettingsItemV2(new FormEnumDropdown<UIThemeOption>
            {
                Caption = "UI theme",
                HintText = "Cosmetic chrome palette. \"Grayscale by fsyori\" strips saturation from "
                           + "every UI accent for the monochrome look and mounts a stable-style "
                           + "user-stats panel in song select. The Midnight family keeps that "
                           + "structural reskin (sharp corners, mounted stats panel) but reuses the "
                           + "default slanted chrome, with three hue variants: Mauve (violet), "
                           + "Crimson (deep red), and Cerulean (deep cyan). For end-to-end skin "
                           + "matching (including gameplay), drop any stable-era .osk into your "
                           + "Skins folder and pick it from the skin dropdown above. Changing this "
                           + "option restarts the game.",
                Current = themeBindable,
            })
            {
                Keywords = new[] { @"theme", @"palette", @"grayscale", @"greyscale", @"monochrome", @"black", @"white", @"fsyori", @"chrome", @"ui", @"midnight", @"mauve", @"crimson", @"cerulean", @"purple", @"red", @"cyan", @"blue" },
            };

            // Register the restart-on-change handler centrally. Each
            // additional instance after the first is a no-op call into
            // the coordinator, so mounting this drawable in N places
            // produces exactly one restart prompt per change.
            UIThemeRestartCoordinator.EnsureRegistered(themeBindable, game, dialogOverlay);
        }
    }

    /// <summary>
    /// Process-wide one-shot subscription that owns the restart-confirm
    /// flow for <see cref="OsuSetting.UITheme"/>. Lives outside
    /// <see cref="UIThemeDropdownAndRestart"/> so that mounting the
    /// dropdown at multiple call sites doesn't duplicate the handler.
    ///
    /// Captures <see cref="OsuGame"/> and <see cref="IDialogOverlay"/>
    /// from the first caller — these are app singletons, so they're
    /// effectively immortal references once they've resolved. The
    /// bindable subscription is itself immortal: the
    /// <see cref="OsuConfigManager"/> outlives every drawable that
    /// could reference it, and the subscription doesn't capture any
    /// per-instance state that would leak.
    /// </summary>
    internal static class UIThemeRestartCoordinator
    {
        private static bool registered;
        private static readonly object register_lock = new object();

        // evita el loop: el revert de Cancel re-dispara BindValueChanged; con esto no reabre el dialog.
        private static bool suppress;

        public static void EnsureRegistered(Bindable<UIThemeOption> themeBindable, OsuGame? game, IDialogOverlay? dialogOverlay)
        {
            // Fast path outside the lock — once we've registered, every
            // future call is a single boolean read with no contention.
            if (registered)
                return;

            lock (register_lock)
            {
                if (registered)
                    return;

                themeBindable.BindValueChanged(change =>
                {
                    // No-op on the initial bind (BindValueChanged fires
                    // once with the current value). Without this guard
                    // opening the settings panel would always pop the
                    // confirm dialog because the bindable "changes from
                    // default to current" on first read.
                    if (suppress || change.NewValue == change.OldValue)
                        return;

                    // Always route through the confirm dialog rather
                    // than attempting Velopack auto-restart. The
                    // auto-restart path (game.RestartAppWhenExited()
                    // followed by AttemptExit) fails in unpackaged
                    // builds (dotnet run from source) because Velopack's
                    // UpdateManager can't resolve a valid executable to
                    // re-launch — the failure surfaces as an
                    // "Unobserved exception... Cannot start process
                    // because a file name has not been provided" toast
                    // immediately after the user picks a theme. The
                    // dialog path closes the game cleanly and tells the
                    // user to reopen, which works reliably for both
                    // packaged and unpackaged builds. In a packaged
                    // Velopack install, AttemptExit on confirm still
                    // triggers Velopack's pending-restart hook if one
                    // was scheduled by other code, so we don't lose
                    // auto-restart capability where it actually works —
                    // we just stop initiating it ourselves from a path
                    // that can't validate it'll succeed.
                    dialogOverlay?.Push(new ConfirmDialog(
                        "In order to change the UI theme, the game will close. Please open it again.",
                        () => game?.AttemptExit(),
                        () =>
                        {
                            suppress = true;
                            themeBindable.Value = change.OldValue;
                            suppress = false;
                        }));
                });

                registered = true;
            }
        }
    }
}
