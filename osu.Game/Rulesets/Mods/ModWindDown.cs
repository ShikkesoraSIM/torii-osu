// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;

namespace osu.Game.Rulesets.Mods
{
    public class ModWindDown : ModTimeRamp
    {
        public override string Name => "Wind Down";
        public override string Acronym => "WD";
        public override LocalisableString Description => "Sloooow doooown...";
        public override IconUsage? Icon => OsuIcon.ModWindDown;

        // Torii: same reasoning as ModAdaptiveSpeed.Ranked => false. WindDown ramps the
        // track speed DOWN from the initial rate to the final rate over the course of
        // a map. Star-rating / pp computation runs the diffcalc against the BASE
        // beatmap (single fixed rate) and has no "realtime, per-window rate" hook —
        // a player can wind a map down 2.0 → 0.5 and the result is scored as if they
        // played the full base-rate version, gaining pp from an effective 0.5x-ish
        // average without paying the difficulty cost. Until upstream gains rate-aware
        // diffcalc this just needs to be unranked. WindUp is fine because it makes
        // the map *harder* than baseline — it would only ever lose pp relative to its
        // notional rating, not gain it. WindDown also implicitly drops out of
        // leaderboards / pp via the standard Ranked-mod filtering everywhere else
        // in the codebase that reads this property.
        public override bool Ranked => false;

        public override BindableNumber<double> InitialRate { get; } = new BindableDouble(1)
        {
            MinValue = 0.51,
            MaxValue = 2,
            Precision = 0.01,
        };

        public override BindableNumber<double> FinalRate { get; } = new BindableDouble(0.75)
        {
            MinValue = 0.5,
            MaxValue = 1.99,
            Precision = 0.01,
        };

        public override BindableBool AdjustPitch { get; } = new BindableBool(true);

        public override Type[] IncompatibleMods => base.IncompatibleMods.Append(typeof(ModWindUp)).ToArray();

        public ModWindDown()
        {
            InitialRate.BindValueChanged(val =>
            {
                if (val.NewValue <= FinalRate.Value)
                    FinalRate.Value = val.NewValue - FinalRate.Precision;
            });

            FinalRate.BindValueChanged(val =>
            {
                if (val.NewValue >= InitialRate.Value)
                    InitialRate.Value = val.NewValue + InitialRate.Precision;
            });
        }
    }
}
