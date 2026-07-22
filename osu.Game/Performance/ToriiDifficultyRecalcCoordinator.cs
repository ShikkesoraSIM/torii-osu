// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading.Tasks;
using osu.Framework.Extensions;
using osu.Game.Configuration;

namespace osu.Game.Performance
{
    /// <summary>
    /// torii: puente entre el recalculo de star rating de fondo (BackgroundDataStoreProcessor, que corre
    /// en un thread aparte) y el popup de arranque que deja al usuario elegir cuanta CPU usar.
    /// estatico a proposito (mismo patron que <see cref="PotatoMode"/>): hay un solo recalculo y un solo
    /// popup por proceso, y asi evitamos cablear DI entre un componente background y la UI.
    ///
    /// flujo: el processor cuenta los mapas pendientes y llama <see cref="AnnouncePending"/>; despues
    /// bloquea en <see cref="WaitForChoice"/> hasta que el popup llame <see cref="Choose"/> (o timeout ->
    /// fallback gentil). la UI espera <see cref="PendingReady"/> para saber si/cuantos mapas hay y mostrar
    /// el popup.
    /// </summary>
    public static class ToriiDifficultyRecalcCoordinator
    {
        private static readonly TaskCompletionSource<(int count, bool interactive)> pending_ready =
            new TaskCompletionSource<(int, bool)>(TaskCreationOptions.RunContinuationsAsynchronously);

        private static readonly TaskCompletionSource<ToriiDifficultyRecalcMode> choice =
            new TaskCompletionSource<ToriiDifficultyRecalcMode>(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Se completa con la cantidad de mapas que necesitan recalculo (0 si ninguno) y si la corrida
        /// amerita popup. La UI espera esto para decidir si mostrar. Llamado una sola vez por el processor.
        /// </summary>
        public static Task<(int count, bool interactive)> PendingReady => pending_ready.Task;

        /// <summary>Cantidad de mapas pendientes una vez que <see cref="PendingReady"/> resolvio (0 si no).</summary>
        public static int PendingCount => pending_ready.Task.IsCompletedSuccessfully ? pending_ready.Task.GetResultSafely().count : 0;

        /// <summary>
        /// true solo cuando la causa del recalculo es un cambio de version de difficulty de TORII (ahi
        /// si corresponde preguntar cuanta CPU usar). false = corrida silenciosa (re-own tras un wipe
        /// del cliente oficial sobre la DB compartida, resume de una corrida cortada, o backfill de
        /// imports): se usa el modo ya guardado sin molestar.
        /// </summary>
        public static bool PendingInteractive => pending_ready.Task.IsCompletedSuccessfully && pending_ready.Task.GetResultSafely().interactive;

        /// <summary>processor: anuncia cuantos mapas hay que recalcular (0 = ninguno) y si va popup.</summary>
        public static void AnnouncePending(int count, bool interactive) => pending_ready.TrySetResult((count, interactive));

        /// <summary>UI: el usuario eligio un modo (o se cerro el popup -> el caller pasa el default).</summary>
        public static void Choose(ToriiDifficultyRecalcMode mode) => choice.TrySetResult(mode);

        /// <summary>
        /// processor: bloquea hasta que el usuario elija, o hasta <paramref name="timeout"/> (ahi devuelve
        /// <paramref name="fallback"/>). corre en el thread background del processor, no traba la UI.
        /// </summary>
        public static ToriiDifficultyRecalcMode WaitForChoice(TimeSpan timeout, ToriiDifficultyRecalcMode fallback)
        {
            try
            {
                return choice.Task.Wait(timeout) ? choice.Task.GetResultSafely() : fallback;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
