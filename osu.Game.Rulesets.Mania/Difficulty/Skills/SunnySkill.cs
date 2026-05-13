// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Difficulty.Preprocessing;
using osu.Game.Rulesets.Difficulty.Skills;
using osu.Game.Rulesets.Mania.Difficulty.Preprocessing;
using osu.Game.Rulesets.Mania.Difficulty.Calculators;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Mania.Difficulty.Skills
{
    public class SunnySkill : Skill
    {
        private List<Calculators.Note> noteSeq;
        private List<List<Calculators.Note>> noteSeqByColumn;
        private double od;
        private int totalColumns;
        private double spikiness;
        private double switches;
        private double greatHitWindow;
        private Mod[] mods;

        public SunnySkill(Mod[] mods, int totalColumns, double od, int objectCount, double greatHitWindow)
            : base(mods)
        {
            this.od = od;

            this.totalColumns = totalColumns;

            noteSeq = new List<Calculators.Note>(objectCount);

            this.greatHitWindow = greatHitWindow;

            noteSeqByColumn = new List<List<Calculators.Note>>();

            for (int k = 0; k < totalColumns; k++)
            {
                noteSeqByColumn.Add(new List<Calculators.Note>());
            }

            this.mods = mods;
        }

        // Mania difficulty hit objects are already sorted in the difficulty calculator, we just need to populate the lists.
        // Note: pp-dev refactored Skill.Process to be a concrete method that calls ProcessInternal and tracks per-object difficulty.
        // SunnySkill computes difficulty in aggregate (in DifficultyValue), so ProcessInternal returns 0 here — only the side
        // effect of populating noteSeq / noteSeqByColumn matters for our calc.
        protected override double ProcessInternal(DifficultyHitObject current)
        {
            ManiaDifficultyHitObject currObj = (ManiaDifficultyHitObject)current;

            int endTime = currObj.EndTime == currObj.StartTime ? -1 : (int)currObj.EndTime;

            Calculators.Note note = new Calculators.Note(currObj.BaseObject.Column, (int)currObj.StartTime, endTime);

            noteSeq.Add(note);

            noteSeqByColumn[note.Column].Add(note);

            return 0;
        }

        public override double DifficultyValue()
        {
            if (noteSeq.Count <= 0)
                return 0;

            double x = 0.3 * Math.Pow(greatHitWindow / 500.0, 0.5);
            x = Math.Min(x, 0.6 * (x - 0.09) + 0.09);

            SRParams srParams = MACalculator.Calculate(noteSeq, noteSeqByColumn, totalColumns, x, mods.Any(m => m is ModClassic));
            spikiness = srParams.Spikiness;
            switches = srParams.Switches;

            return srParams.SR;
        }

        public double VarietyValue()
        {
            return MACalculator.Variety(noteSeq, noteSeqByColumn);
        }

        public double AccScalarValue()
        {
            return 0.5*spikiness+0.5*switches;
        }

    }
}
