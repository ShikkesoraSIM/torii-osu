// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Configuration;

namespace osu.Game.Performance
{
    /// <summary>
    /// Applies "Potato Mode": when <see cref="OsuSetting.ToriiPotatoMode"/> is enabled this
    /// forces the most expensive graphics settings to their cheapest values, for low-end PCs.
    ///
    /// It only flips settings that already have a config toggle (so consuming code needs no
    /// changes). To avoid permanently clobbering the user's own choices it uses a snapshot:
    /// enabling Potato in a session stashes the current values and restores them on disable.
    /// If Potato was already on at startup (no enable transition this session) a later disable
    /// restores osu! defaults instead — the only case where a custom value isn't recovered.
    /// </summary>
    public partial class PotatoModeApplier : Component
    {
        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private Bindable<bool> potatoMode = null!;

        private readonly List<Action> capture = new List<Action>();
        private readonly List<Action> apply = new List<Action>();
        private readonly List<Action> restore = new List<Action>();

        [BackgroundDependencyLoader]
        private void load()
        {
            // (setting, value while Potato is ON, value to fall back to if we can't recover a custom one)
            register(config.GetBindable<bool>(OsuSetting.ShowStoryboard), potatoOff: false, fallback: true);
            register(config.GetBindable<double>(OsuSetting.DimLevel), potatoValue: 1.0, fallback: 0.7);
            register(config.GetBindable<double>(OsuSetting.BlurLevel), potatoValue: 0, fallback: 0);
            register(config.GetBindable<bool>(OsuSetting.HitLighting), potatoOff: false, fallback: true);
            register(config.GetBindable<bool>(OsuSetting.StarFountains), potatoOff: false, fallback: true);
            register(config.GetBindable<bool>(OsuSetting.MenuParallax), potatoOff: false, fallback: true);
            register(config.GetBindable<bool>(OsuSetting.GameplayLeaderboard), potatoOff: false, fallback: true);
            register(config.GetBindable<SeasonalBackgroundMode>(OsuSetting.SeasonalBackgroundMode), potatoValue: SeasonalBackgroundMode.Never, fallback: SeasonalBackgroundMode.Sometimes);

            potatoMode = config.GetBindable<bool>(OsuSetting.ToriiPotatoMode);

            // Already on at startup: apply the cheap values but do NOT capture (the persisted
            // values are themselves cheap); the stash keeps the safe fallbacks.
            if (potatoMode.Value)
                apply.ForEach(a => a());

            potatoMode.BindValueChanged(e =>
            {
                if (e.NewValue)
                {
                    capture.ForEach(a => a());
                    apply.ForEach(a => a());
                }
                else
                    restore.ForEach(a => a());
            });
        }

        // bool overload (named potatoOff for readability: the cheap value is always false here)
        private void register(Bindable<bool> target, bool potatoOff, bool fallback)
            => register<bool>(target, potatoOff, fallback);

        private void register(Bindable<double> target, double potatoValue, double fallback)
            => register<double>(target, potatoValue, fallback);

        private void register(Bindable<SeasonalBackgroundMode> target, SeasonalBackgroundMode potatoValue, SeasonalBackgroundMode fallback)
            => register<SeasonalBackgroundMode>(target, potatoValue, fallback);

        private void register<T>(Bindable<T> target, T potatoValue, T fallback)
        {
            T saved = fallback;
            capture.Add(() => saved = target.Value);
            apply.Add(() => target.Value = potatoValue);
            restore.Add(() => target.Value = saved);
        }
    }
}
