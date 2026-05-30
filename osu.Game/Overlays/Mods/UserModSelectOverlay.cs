// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Input.Events;
using osu.Game.Configuration;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osu.Game.Utils;

namespace osu.Game.Overlays.Mods
{
    public partial class UserModSelectOverlay : ModSelectOverlay
    {
        private ModSpeedHotkeyHandler modSpeedHotkeyHandler = null!;
        private ModSettingChangeTracker? modSettingChangeTracker;
        private readonly BindableInt ratePitchSettingVersion = new BindableInt();

        public UserModSelectOverlay(OverlayColourScheme colourScheme = OverlayColourScheme.Green)
            : base(colourScheme)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(modSpeedHotkeyHandler = new ModSpeedHotkeyHandler());
        }

        protected override ModColumn CreateModColumn(ModType modType) => new UserModColumn(modType, false, ratePitchSettingVersion);

        protected override void LoadComplete()
        {
            base.LoadComplete();

            SelectedMods.BindValueChanged(onSelectedModsChanged, true);
        }

        private void onSelectedModsChanged(ValueChangedEvent<IReadOnlyList<Mod>> mods)
        {
            modSettingChangeTracker?.Dispose();
            modSettingChangeTracker = new ModSettingChangeTracker(mods.NewValue);
            modSettingChangeTracker.SettingChanged += _ =>
            {
                ratePitchSettingVersion.Value++;
                removePitchAdjustIfRatePitchIsOn();
            };

            removePitchAdjustIfRatePitchIsOn();
        }

        protected override IReadOnlyList<Mod> ComputeNewModsFromSelection(IReadOnlyList<Mod> oldSelection, IReadOnlyList<Mod> newSelection)
        {
            var addedMods = newSelection.Except(oldSelection);
            var removedMods = oldSelection.Except(newSelection);

            IEnumerable<Mod> modsAfterRemoval = newSelection.Except(removedMods).ToList();

            // the preference is that all new mods should override potential incompatible old mods.
            // in general that's a bit difficult to compute if more than one mod is added at a time,
            // so be conservative and just remove all mods that aren't compatible with any one added mod.
            foreach (var addedMod in addedMods)
            {
                if (!ModUtils.CheckCompatibleSet(modsAfterRemoval.Append(addedMod), out var invalidMods))
                    modsAfterRemoval = modsAfterRemoval.Except(invalidMods);

                modsAfterRemoval = modsAfterRemoval.Append(addedMod).ToList();
            }

            var finalMods = modsAfterRemoval.ToList();

            if (finalMods.Any(isRatePitchAdjusting))
                finalMods.RemoveAll(m => m is ModPitchAdjust);

            return finalMods;
        }

        private void removePitchAdjustIfRatePitchIsOn()
        {
            var ratePitchMod = SelectedMods.Value.FirstOrDefault(isRatePitchAdjusting);

            if (!SelectedMods.Value.Any(m => m is ModPitchAdjust) || ratePitchMod == null)
                return;

            foreach (var modState in AllAvailableMods.Where(state => state.Mod.GetType() == ratePitchMod.GetType()))
                modState.PendingConfiguration = true;

            SelectedMods.Value = SelectedMods.Value.Where(m => m is not ModPitchAdjust).ToArray();
        }

        private static bool isDynamicallyIncompatible(Mod mod, IReadOnlyList<Mod> selectedMods) =>
            mod is ModPitchAdjust && selectedMods.Any(isRatePitchAdjusting);

        private static bool isRatePitchAdjusting(Mod mod) => mod switch
        {
            ModDoubleTime doubleTime => doubleTime.AdjustPitch.Value,
            ModHalfTime halfTime => halfTime.AdjustPitch.Value,
            _ => false,
        };

        public override bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            switch (e.Action)
            {
                case GlobalAction.IncreaseModSpeed:
                    return modSpeedHotkeyHandler.ChangeSpeed(0.05, AllAvailableMods.Where(state => state.ValidForSelection.Value).Select(state => state.Mod));

                case GlobalAction.DecreaseModSpeed:
                    return modSpeedHotkeyHandler.ChangeSpeed(-0.05, AllAvailableMods.Where(state => state.ValidForSelection.Value).Select(state => state.Mod));
            }

            return base.OnPressed(e);
        }

        private partial class UserModColumn : ModColumn
        {
            private readonly BindableInt ratePitchSettingVersion;

            public UserModColumn(ModType modType, bool allowIncompatibleSelection, BindableInt ratePitchSettingVersion)
                : base(modType, allowIncompatibleSelection)
            {
                this.ratePitchSettingVersion = ratePitchSettingVersion;
            }

            protected override ModPanel CreateModPanel(ModState modState) => new UserIncompatibilityDisplayingModPanel(modState, ratePitchSettingVersion);
        }

        private partial class UserIncompatibilityDisplayingModPanel : IncompatibilityDisplayingModPanel
        {
            private readonly BindableInt ratePitchSettingVersion;

            public UserIncompatibilityDisplayingModPanel(ModState modState, BindableInt ratePitchSettingVersion)
                : base(modState)
            {
                this.ratePitchSettingVersion = ratePitchSettingVersion;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();

                ratePitchSettingVersion.BindValueChanged(_ => UpdateIncompatibility());
            }

            protected override bool IsIncompatibleWithSelected(IReadOnlyList<Mod> selectedMods) =>
                base.IsIncompatibleWithSelected(selectedMods) || isDynamicallyIncompatible(Mod, selectedMods);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            modSettingChangeTracker?.Dispose();
        }
    }
}
