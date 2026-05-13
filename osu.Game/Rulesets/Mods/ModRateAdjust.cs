// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;

namespace osu.Game.Rulesets.Mods
{
    public abstract class ModRateAdjust : Mod, IApplicableToRate, IApplicableToDifficulty
    {
        public sealed override bool ValidForFreestyleAsRequiredMod => true;
        public sealed override bool ValidForMultiplayerAsFreeMod => false;

        public abstract BindableNumber<double> SpeedChange { get; }

        [SettingSource("Adjust hit windows")]
        public virtual BindableBool AdjustWindows { get; } = new BindableBool();

        [SettingSource("Adjust approach rate")]
        public virtual BindableBool AdjustApproachRate { get; } = new BindableBool();

        public abstract void ApplyToTrack(IAdjustableAudioComponent track);

        public virtual void ApplyToSample(IAdjustableAudioComponent sample)
        {
            sample.AddAdjustment(AdjustableProperty.Frequency, SpeedChange);
        }

        public double ApplyToRate(double time, double rate) => rate * SpeedChange.Value;

        public override Type[] IncompatibleMods => new[] { typeof(ModTimeRamp), typeof(ModAdaptiveSpeed), typeof(ModRateAdjust) };

        public override IEnumerable<(LocalisableString setting, LocalisableString value)> SettingDescription
        {
            get
            {
                if (!SpeedChange.IsDefault)
                    yield return ("Speed change", FormattableString.Invariant($@"{SpeedChange.Value:N2}x"));
            }
        }

        public override string ExtendedIconInformation => SpeedChange.IsDefault ? string.Empty : FormattableString.Invariant($"{SpeedChange.Value:N2}x");
        public abstract void ApplyToDifficulty(BeatmapDifficulty difficulty);
    }
}
