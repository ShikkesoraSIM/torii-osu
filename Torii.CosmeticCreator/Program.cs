// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using osu.Framework;
using osu.Framework.Platform;
using osu.Game.Cosmetics.Definitions;

namespace Torii.CosmeticCreator
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            // modo headless de verificacion: carga los .toriicosmetic de auras, los parsea y construye
            // el preset (sin GUI), e imprime un resumen. sirve para round-trip en net10 sin depender de
            // osu.Game.Tests (que apunta a net8). uso: --verify-auras [dir].
            if (args.Length >= 1 && args[0] == "--verify-auras")
            {
                verifyAuras(args.Length >= 2 ? args[1] : Path.Combine(AppContext.BaseDirectory, "SampleAuras"));
                return;
            }

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

        private static void verifyAuras(string dir)
        {
            if (!Directory.Exists(dir))
            {
                Console.WriteLine($"no existe el dir de auras: {dir}");
                return;
            }

            int ok = 0, fail = 0;
            foreach (string file in Directory.GetFiles(dir, "*.toriicosmetic").OrderBy(f => f))
            {
                try
                {
                    var def = CosmeticDefinition.Parse(File.ReadAllText(file));
                    var data = def.Settings?.ToObject<DataDrivenAura>();

                    if (!CosmeticAuraFactory.CanBuild(def))
                        throw new InvalidOperationException($"CanBuild=false (Type={def.Type})");

                    var preset = CosmeticAuraFactory.Create(def);
                    int particles = data?.Particles?.Count ?? 0;
                    int layers = data?.Particles?.Sum(p => p.Layers?.Count ?? 0) ?? 0;

                    Console.WriteLine(
                        $"OK   {def.Id,-16} kind={def.AuraKind} auraId={preset.AuraId} glow={preset.GlowColour} " +
                        $"glowSettings={(preset.GlowSettings != null ? "si" : "no")} maxAlive={preset.MaxAlive} " +
                        $"interval={preset.SpawnIntervalMs} particles={particles} layers={layers}");
                    ok++;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"FAIL {Path.GetFileName(file)}: {e.Message}");
                    fail++;
                }
            }

            Console.WriteLine($"\n=== {ok} ok, {fail} fail ===");
        }
    }
}
