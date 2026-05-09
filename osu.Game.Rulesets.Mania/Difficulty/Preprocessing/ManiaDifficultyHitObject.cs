// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.Mania.Difficulty.Preprocessing
{
    public class ManiaDifficultyHitObject : DifficultyHitObject
    {
        public new ManiaHitObject BaseObject => (ManiaHitObject)base.BaseObject;
        public int Column => BaseObject.Column;

        // Compatibility surface for legacy strain evaluators.
        public ManiaDifficultyHitObject?[] PreviousHitObjects
        {
            get
            {
                int maxColumn = Column;
                var previous = new List<ManiaDifficultyHitObject>();

                for (int i = 0; ; i++)
                {
                    if (Previous(i) is not ManiaDifficultyHitObject maniaPrevious)
                        break;

                    previous.Add(maniaPrevious);
                    if (maniaPrevious.Column > maxColumn)
                        maxColumn = maniaPrevious.Column;
                }

                var result = new ManiaDifficultyHitObject?[maxColumn + 1];

                // iterate nearest -> farthest so the first object in each column is the latest previous one.
                foreach (var maniaPrevious in previous)
                    result[maniaPrevious.Column] ??= maniaPrevious;

                return result;
            }
        }

        public double ColumnStrainTime => StartTime - (PrevInColumn(0)?.StartTime ?? 0);

        public ManiaDifficultyHitObject(HitObject hitObject, HitObject lastObject, double clockRate, List<DifficultyHitObject> objects, int index)
            : base(hitObject, lastObject, clockRate, objects, index)
        {
        }

        public ManiaDifficultyHitObject? PrevInColumn(int backwardsIndex)
        {
            int found = 0;

            for (int i = 0; ; i++)
            {
                if (Previous(i) is not ManiaDifficultyHitObject previous)
                    return null;

                if (previous.Column != Column)
                    continue;

                if (found == backwardsIndex)
                    return previous;

                found++;
            }
        }

        public ManiaDifficultyHitObject? NextInColumn(int forwardsIndex)
        {
            int found = 0;

            for (int i = 0; ; i++)
            {
                if (Next(i) is not ManiaDifficultyHitObject next)
                    return null;

                if (next.Column != Column)
                    continue;

                if (found == forwardsIndex)
                    return next;

                found++;
            }
        }
    }
}
