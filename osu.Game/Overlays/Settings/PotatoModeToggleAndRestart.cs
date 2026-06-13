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
    /// Potato Mode toggle for Settings → Torii → Interface. Potato Mode is
    /// an extreme-performance preset for weak PCs; it's read once at
    /// startup (see <see cref="osu.Game.Performance.PotatoMode"/>) and
    /// gates heavy-visual subsystems at construction, so flipping it
    /// mid-run would leave a torn mix of cheap and expensive surfaces.
    /// Changing it therefore prompts for a restart, mirroring the
    /// <see cref="UIThemeDropdownAndRestart"/> dropdown.
    /// </summary>
    public partial class PotatoModeToggleAndRestart : CompositeDrawable
    {
        // Resolved optionally because the test-scene host doesn't register
        // OsuGame / IDialogOverlay. In that case the checkbox binds without
        // the restart prompt — the test can flip the setting freely.
        [Resolved(CanBeNull = true)]
        private OsuGame? game { get; set; }

        [Resolved(CanBeNull = true)]
        private IDialogOverlay? dialogOverlay { get; set; }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            var potatoBindable = config.GetBindable<bool>(OsuSetting.ToriiPotatoMode);

            InternalChild = new SettingsItemV2(new FormCheckBox
            {
                Caption = "Potato mode (extreme performance)",
                HintText = "For weak PCs. Cuts as much as possible: no animated triangles, no logo "
                           + "visualiser, no storyboards, no background blur, no hit lighting, no kiai "
                           + "flashes, no star fountains, no menu parallax, no cursor trail and no "
                           + "seasonal backgrounds. Also fully dims gameplay backgrounds, hides the "
                           + "server pulse widget, and switches to the legacy audio engine to avoid "
                           + "stutters. Frees up GPU, CPU and memory for max FPS. Your own graphics "
                           + "settings are left untouched. Changing this option restarts the game.",
                Current = potatoBindable,
                NewFeatureId = NewFeatureRegistry.PotatoMode,
            })
            {
                Keywords = new[] { @"potato", @"performance", @"fps", @"low end", @"weak", @"lag", @"stutter", @"perf", @"quality", @"battery" },
            };

            PotatoModeRestartCoordinator.EnsureRegistered(potatoBindable, game, dialogOverlay);
        }
    }

    /// <summary>
    /// Process-wide one-shot subscription that owns the restart-confirm
    /// flow for <see cref="OsuSetting.ToriiPotatoMode"/>. Mirrors
    /// <c>UIThemeRestartCoordinator</c>: the toggle lives at a single call
    /// site today, but the central registration keeps the restart prompt
    /// from firing twice if it's ever mounted in two places (e.g. a future
    /// quick-toggle alongside the settings panel).
    /// </summary>
    internal static class PotatoModeRestartCoordinator
    {
        private static bool registered;
        private static readonly object register_lock = new object();

        public static void EnsureRegistered(Bindable<bool> potatoBindable, OsuGame? game, IDialogOverlay? dialogOverlay)
        {
            if (registered)
                return;

            lock (register_lock)
            {
                if (registered)
                    return;

                potatoBindable.BindValueChanged(change =>
                {
                    // No-op on the initial bind (fires once with the current
                    // value); only a real user flip should prompt a restart.
                    if (change.NewValue == change.OldValue)
                        return;

                    // Route through the confirm dialog rather than a
                    // Velopack auto-restart for the same reason the UI-theme
                    // toggle does: auto-restart can't resolve an executable
                    // in unpackaged (run-from-source) builds and surfaces a
                    // stray error toast. Closing cleanly and asking the user
                    // to reopen works for both packaged and unpackaged.
                    dialogOverlay?.Push(new ConfirmDialog(
                        "Potato mode changes a lot of visuals, so the game needs to restart. It will close now, please open it again.",
                        () => game?.AttemptExit(),
                        () => potatoBindable.Value = change.OldValue));
                });

                registered = true;
            }
        }
    }
}
