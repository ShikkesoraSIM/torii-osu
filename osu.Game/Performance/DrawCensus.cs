// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Testing;

namespace osu.Game.Performance
{
    /// <summary>
    /// Torii: censo de diagnostico del arbol de dibujo. Agrupa cada drawable vivo
    /// por tipo con cuantos hay y cuanta area de pantalla suman, y lo dumpea a un
    /// archivo. Se activa con la variable de entorno TORII_DRAW_CENSUS=1 y sirve
    /// para diffear que compone este cliente contra otro build en la misma escena
    /// (la caceria del "torii dibuja 3x los pixeles de lazer").
    /// </summary>
    public static class DrawCensus
    {
        public static bool Enabled => Environment.GetEnvironmentVariable("TORII_DRAW_CENSUS") == "1";

        public static void Dump(Drawable root, string label)
        {
            var groups = new Dictionary<string, (int count, double area)>();

            int total = 0;
            double totalArea = 0;

            foreach (var d in root.ChildrenOfType<Drawable>())
            {
                // presencia EFECTIVA: un hijo visible adentro de un overlay oculto no se
                // dibuja (el ancestro poda el subtree entero), asi que no cuenta.
                if (!d.IsPresent)
                    continue;

                bool ancestorHidden = false;

                for (var p = d.Parent; p != null; p = p.Parent)
                {
                    if (!p.IsPresent)
                    {
                        ancestorHidden = true;
                        break;
                    }
                }

                if (ancestorHidden)
                    continue;

                var quad = d.ScreenSpaceDrawQuad.AABBFloat;
                double area = (double)quad.Width * quad.Height;

                string key = d.GetType().Name;
                var g = groups.GetValueOrDefault(key);
                groups[key] = (g.count + 1, g.area + area);

                total++;
                totalArea += area;
            }

            string path = Path.Combine(Path.GetTempPath(), $"draw-census-{label}-{DateTime.Now:HHmmss}.txt");

            using (var w = new StreamWriter(path))
            {
                w.WriteLine($"# draw census '{label}' - {total} drawables presentes, area total {totalArea / 1_000_000:0.00} Mpx");
                w.WriteLine($"# {"TIPO",-46} {"COUNT",6} {"AREA Mpx",10}");

                foreach (var kv in groups.OrderByDescending(kv => kv.Value.area))
                    w.WriteLine($"{kv.Key,-48} {kv.Value.count,6} {kv.Value.area / 1_000_000,10:0.000}");
            }

            Logger.Log($"[DrawCensus] dump escrito en {path}", level: LogLevel.Important);
        }
    }
}
