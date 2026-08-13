// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.IO;
using osu.Game.Overlays.Dialog;

namespace osu.Game.Overlays.FirstRunSetup
{
    /// <summary>
    /// torii: el cambio de carpeta de datos del first-run, en un solo lugar. lo usan tanto la pantalla
    /// de deteccion (carpeta autodetectada) como la de elegir carpeta a mano, y las dos necesitan
    /// exactamente la misma secuencia: confirmar, llevarse el torii.ini, redirigir y salir.
    /// </summary>
    public static class ToriiFirstRunFlow
    {
        // MaintenanceSettingsStrings de este base no tiene RestartAndReOpenRequiredForCompletion, asi que
        // el texto va inline para no depender de un string que puede no existir tras un sync de upstream.
        private static readonly LocalisableString restart_required_message =
            @"To complete this operation, Torii will close. Please open it again to use the new data location.";

        /// <summary>
        /// Pide confirmacion y, al confirmar, migra el torii.ini al destino, apunta el storage ahi y cierra el juego.
        /// </summary>
        /// <returns>false si el path no es una carpeta de datos de lazer valida. el llamador muestra el error.</returns>
        public static bool ApplyDataPath(string? path, Storage storage, OsuGameBase game, OsuConfigManager config, IDialogOverlay? dialogOverlay)
        {
            if (!ToriiStoragePathHelper.LooksLikeLazerStoragePath(path))
                return false;

            dialogOverlay?.Push(new ConfirmDialog(restart_required_message, () =>
            {
                migrateConfigFile(storage, config, path!);

                (storage as OsuStorage)?.ChangeDataPath(path!);
                game.Exit();
            }));

            return true;
        }

        /// <summary>
        /// torii guarda su config en torii.ini (no en el game.ini de ppy), asi que la carpeta de lazer del
        /// jugador no tiene ninguno: sin esta copia el proximo arranque leeria defaults, y el first-run
        /// volveria a aparecer justo despues de haberlo terminado.
        /// </summary>
        private static void migrateConfigFile(Storage storage, OsuConfigManager config, string destinationPath)
        {
            try
            {
                // el estado tiene que quedar escrito ANTES de copiar: lo que viaja es el archivo, no los
                // bindables. first-run hecho, wizard de torii pendiente para cuando vuelva a abrir.
                config.SetValue(OsuSetting.ShowFirstRunSetup, false);
                config.SetValue(OsuSetting.ShowToriiWelcome, true);
                config.Save();

                string destination = Path.Combine(destinationPath, OsuConfigManager.TORII_CONFIG_FILENAME);

                // ya hay un torii.ini alla: ese jugador ya corrio torii sobre esa carpeta y su config manda
                // (tiene su login y sus settings). pisarla seria perderle todo eso.
                if (File.Exists(destination))
                    return;

                string source = storage.GetFullPath(OsuConfigManager.TORII_CONFIG_FILENAME);

                if (!File.Exists(source))
                    return;

                File.Copy(source, destination);
            }
            catch (Exception ex)
            {
                // el peor caso de fallar aca es ver el wizard una vez mas, nada se corrompe: no cortamos el flujo.
                Logger.Log($"[torii.ini] copia a {destinationPath} fallo: {ex.Message}", LoggingTarget.Runtime, LogLevel.Important);
            }
        }
    }
}
