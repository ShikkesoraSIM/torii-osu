// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using osu.Framework;
using osu.Framework.Platform;

namespace Torii.CosmeticCreator
{
    public static class Program
    {
        public static void Main()
        {
            // dev: capturamos cualquier crash de arranque a un archivo (la ventana no siempre deja ver
            // el stack). se puede sacar cuando este estable.
            string crashLog = Path.Combine(Path.GetTempPath(), "torii-creator-crash.log");

            try
            {
                // portable: la data (config, realm, exports) vive al lado del exe en vez de %APPDATA%.
                // para una tool de creator es lo correcto -> self-contained, zippeable, y los archivos
                // exportados quedan a mano; ademas evita depender de que el known-folder de roaming
                // resuelva segun como se lance el proceso.
                var hostOptions = new HostOptions { PortableInstallation = true };

                using (DesktopGameHost host = Host.GetSuitableDesktopHost(@"torii-cosmetic-creator", hostOptions))
                    host.Run(new CosmeticCreatorGame());
            }
            catch (Exception e)
            {
                try { File.WriteAllText(crashLog, DateTime.Now + "\n" + e); }
                catch { }

                Console.Error.WriteLine(e);
                throw;
            }
        }
    }
}
