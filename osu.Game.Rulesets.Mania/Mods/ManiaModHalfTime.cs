// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Bindables;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Mania.Mods
{
    public class ManiaModHalfTime : ModHalfTime, IManiaRateAdjustmentMod
    {
        public override BindableBool AdjustWindows { get; } = new BindableBool();
        public override BindableBool AdjustApproachRate { get; } = new BindableBool();
    }
}
