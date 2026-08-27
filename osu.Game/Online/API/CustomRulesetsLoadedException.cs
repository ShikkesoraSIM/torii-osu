// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Immutable;

namespace osu.Game.Online.API
{
    /// <summary>
    /// Se tiro cuando hay rulesets ajenos cargados y por eso no se intenta conectar.
    /// </summary>
    public class CustomRulesetsLoadedException : Exception
    {
        public readonly ImmutableArray<string> Rulesets;

        public CustomRulesetsLoadedException(ImmutableArray<string> rulesets)
            : base("Custom rulesets are not allowed on Torii.")
        {
            Rulesets = rulesets;
        }
    }
}
