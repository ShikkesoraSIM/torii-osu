// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Rulesets.Mods
{
    /// <summary>
    /// Marker interface for mods which change star rating through the ruleset's difficulty calculator,
    /// but don't implement any interface that <see cref="Beatmaps.StarRatingApproximator.ModAffectsDifficulty"/> already checks for.
    /// </summary>
    public interface IAffectsDifficulty
    {
    }
}
