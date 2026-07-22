// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Rulesets;

namespace osu.Game.Overlays.Profile
{
    /// <summary>
    /// torii: los "modos de perfil" exclusivos del server (osu!relax, osu!autopilot, taiko relax)
    /// como <see cref="RulesetInfo"/> FAKE sin realm: el ShortName es el string exacto de modo que
    /// la API de g0v0 entiende ("osurx"/"osuap"/"taikorx"), asi GetUserRequest y GetUserScoresRequest
    /// (que serializan Ruleset.ShortName tal cual) piden las stats/scores del variant sin tocar nada.
    /// el InstantiationInfo es el del ruleset base, asi el tab puede crear el icono; los OnlineID
    /// (4/5/6, los mismos alias enteros del server) son unicos para que MatchesOnlineID no explote.
    /// catch relax NO esta a proposito: el server lo mergea al perfil de catch base.
    /// NUNCA asignar estos rulesets al bindable global del juego — son solo para la UI de perfil.
    /// </summary>
    public static class ProfileRulesetVariants
    {
        // shortname api -> (base, nombre visible, online id del server, label corto del tab)
        private static readonly (string shortName, string baseShortName, string name, int onlineId, string label)[] variants =
        {
            (@"osurx", @"osu", @"osu! (relax)", 4, @"relax"),
            (@"osuap", @"osu", @"osu! (autopilot)", 5, @"autopilot"),
            (@"taikorx", @"taiko", @"taiko (relax)", 6, @"relax"),
        };

        public static bool IsVariant(IRulesetInfo? ruleset)
        {
            if (ruleset == null)
                return false;

            foreach (var v in variants)
            {
                if (v.shortName == ruleset.ShortName)
                    return true;
            }

            return false;
        }

        /// <summary>El label corto que se muestra al lado del icono en el tab ("relax" / "autopilot").</summary>
        public static string LabelFor(IRulesetInfo ruleset)
        {
            foreach (var v in variants)
            {
                if (v.shortName == ruleset.ShortName)
                    return v.label;
            }

            return string.Empty;
        }

        public static RulesetInfo? TryGet(string shortName, RulesetStore rulesets)
        {
            foreach (var v in variants)
            {
                if (v.shortName != shortName)
                    continue;

                var baseRuleset = rulesets.GetRuleset(v.baseShortName);

                if (baseRuleset == null)
                    return null;

                return new RulesetInfo(v.shortName, v.name, baseRuleset.InstantiationInfo, v.onlineId)
                {
                    Available = true,
                };
            }

            return null;
        }

        /// <summary>
        /// La lista de tabs del perfil, con los variants intercalados despues de su ruleset base:
        /// osu, osu!relax, osu!autopilot, taiko, taiko relax, fruits, mania.
        /// </summary>
        public static List<RulesetInfo> BuildProfileTabs(RulesetStore rulesets)
        {
            var result = new List<RulesetInfo>();

            foreach (string baseShort in new[] { @"osu", @"taiko", @"fruits", @"mania" })
            {
                var baseRuleset = rulesets.GetRuleset(baseShort);

                if (baseRuleset == null)
                    continue;

                result.Add(baseRuleset);

                foreach (var v in variants)
                {
                    if (v.baseShortName == baseShort && TryGet(v.shortName, rulesets) is RulesetInfo variant)
                        result.Add(variant);
                }
            }

            return result;
        }
    }
}
