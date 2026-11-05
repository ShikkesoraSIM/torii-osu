// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Graphics;

namespace osu.Game.Rulesets.Mods
{
    public class ModScoreV2 : Mod
    {
        public override string Name => "Score V2";
        public override string Acronym => @"SV2";
        public override IconUsage? Icon => OsuIcon.ModScoreV2;
        public override ModType Type => ModType.Conversion;
        public override LocalisableString Description => "Play with the Score V2 scoring algorithm.";
        public override double ScoreMultiplier => 1;
        public override bool UserPlayable => true;
        public override bool ValidForMultiplayer => true;
        public override bool ValidForMultiplayerAsFreeMod => true;
        public override bool HasImplementation => true;
        public sealed override bool Ranked => true;
        public sealed override bool ValidForFreestyleAsRequiredMod => true;
        public override Type[] IncompatibleMods => new[] { typeof(ModClassic) };
    }
}
