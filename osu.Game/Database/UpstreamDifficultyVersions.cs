// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;

namespace osu.Game.Database
{
    /// <summary>
    /// torii: espejo de las Version de los difficulty calculators de UPSTREAM (ppy/osu), NO las
    /// nuestras. se usa para subir el stamp compartido de la realm
    /// (<see cref="osu.Game.Rulesets.RulesetInfo.LastAppliedDifficultyVersion"/>) a un valor que el
    /// cliente oficial considere al dia: asi abrir la misma database con el osu! oficial no dispara
    /// el wipe/recalc total de star ratings (el oficial solo wipea cuando su version es MAYOR al
    /// stamp; nunca mira los valores).
    ///
    /// ATENCION (checklist de sync): actualizar estos numeros en CADA sync/rebase con upstream —
    /// grep "override int Version" en los cuatro Difficulty*Calculator de upstream. si upstream
    /// bumpea antes de que actualicemos, el costo es acotado: UN wipe del lado vanilla por release
    /// (+ un re-own silencioso nuestro), no un ping-pong por switch.
    ///
    /// fuente actual: ppy/osu a66634b9cc7 (mergeado 2026-07-06) — osu/taiko/fruits 20260706;
    /// mania nunca fue bumpeado upstream (20241007).
    /// </summary>
    public static class UpstreamDifficultyVersions
    {
        private static readonly Dictionary<string, int> versions = new Dictionary<string, int>
        {
            [@"osu"] = 20260706,
            [@"taiko"] = 20260706,
            [@"fruits"] = 20260706,
            [@"mania"] = 20241007,
        };

        /// <summary>La version upstream espejada para el ruleset, o 0 si no lo trackeamos (rulesets custom).</summary>
        public static int For(string rulesetShortName) => versions.GetValueOrDefault(rulesetShortName, 0);
    }
}
