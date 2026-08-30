// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
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

        private static readonly Dictionary<Type, bool> dibuja_cache = new Dictionary<Type, bool>();

        /// <summary>
        /// Si este drawable produce geometria de verdad.
        /// </summary>
        /// <remarks>
        /// Un tipo dibuja si override CreateDrawNode. Con el filtro anterior ("no ser
        /// CompositeDrawable") se colaban cosas que son pura logica y no pintan nada,
        /// como GameIdleTracker o MouseInputDetector, y salian arriba de todo con 3,5 Mpx
        /// cada una inventando culpables. Una lista de sospechosos con falsos positivos
        /// es peor que no tenerla: manda a arreglar lo que no estaba roto.
        /// </remarks>
        private static bool dibujaDeVerdad(Drawable d)
        {
            var t = d.GetType();

            if (dibuja_cache.TryGetValue(t, out bool cached))
                return cached;

            var m = t.GetMethod("CreateDrawNode", System.Reflection.BindingFlags.Instance
                                                  | System.Reflection.BindingFlags.NonPublic
                                                  | System.Reflection.BindingFlags.Public);

            bool result = m != null
                          && m.DeclaringType != typeof(Drawable)
                          && m.DeclaringType != typeof(CompositeDrawable);

            dibuja_cache[t] = result;
            return result;
        }

        public static void Dump(Drawable root, string label)
        {
            var groups = new Dictionary<string, (int count, double area)>();
            var individuals = new List<(double area, string path)>();
            var invisibles = new List<(double area, string path)>();

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

                // "Presente" y "visible" NO son lo mismo, y confundirlos es el error que
                // nos costo ~15 Mpx por cuadro en el menu principal: AlwaysPresent
                // mantiene vivo un overlay escondido para que su Scheduler corra, pero de
                // paso lo deja DIBUJANDO, y un drawable en alfa efectivo 0 igual manda su
                // geometria a la GPU. Se paga entero y no se ve nada.
                //
                // Esta lista es la que hay que mirar primero: cualquier cosa grande que
                // aparezca aca se esta pintando para nadie.
                float alfaEfectivo = d.Alpha;

                for (var p = d.Parent; p != null; p = p.Parent)
                    alfaEfectivo *= p.Alpha;

                // Mismo filtro que la lista de arriba: un Container no pinta nada por si
                // mismo, contarlo aca inventa culpables. Solo las hojas que dibujan.
                bool dibuja = dibujaDeVerdad(d);

                // Sin umbral de area: lo chico no cuesta pixeles pero SI cuesta draw
                // calls. 140 textos invisibles no se notan en fill rate y son 140 cambios
                // de textura, que es justo lo que separa 28 draw calls de 222.
                if (dibuja && alfaEfectivo < 0.01f)
                {
                    var cadena = new List<string>();
                    for (var p = d.Parent; p != null && cadena.Count < 4; p = p.Parent)
                        cadena.Add(p.GetType().Name);
                    cadena.Reverse();
                    invisibles.Add((area, $"{string.Join(" > ", cadena)} > {d.GetType().Name}"));
                }

                string key = d.GetType().Name;
                var g = groups.GetValueOrDefault(key);
                groups[key] = (g.count + 1, g.area + area);

                // Solo las hojas que de verdad mandan pixeles. Un Container no dibuja
                // nada por si mismo: contarlo hincha el total con area que nadie pinta.
                if (dibuja)
                {
                    var chain = new List<string>();
                    for (var p = d.Parent; p != null && chain.Count < 4; p = p.Parent)
                        chain.Add(p.GetType().Name);
                    chain.Reverse();
                    individuals.Add((area, $"{string.Join(" > ", chain)} > {key}"));
                }

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

                // Agrupado por tipo alcanza para ver que "los Box suman 12 Mpx", pero no
                // dice CUALES. Y ahi esta la respuesta util: un Box gigante de pantalla
                // completa y uno chiquito son el mismo tipo. Esta segunda lista nombra a
                // cada uno con su cadena de padres, que es lo que se puede ir a buscar
                // al codigo.
                w.WriteLine();
                w.WriteLine("# los 30 que mas area ocupan, uno por uno (solo lo que DIBUJA)");
                w.WriteLine($"# {"AREA Mpx",10}  RUTA");

                foreach (var d in individuals.OrderByDescending(x => x.area).Take(30))
                    w.WriteLine($"{d.area / 1_000_000,12:0.000}  {d.path}");

                w.WriteLine();

                if (invisibles.Count == 0)
                    w.WriteLine("# nada grande se esta dibujando invisible. bien ahi.");
                else
                {
                    w.WriteLine($"# !! {invisibles.Count} cosas se DIBUJAN sin verse (alfa efectivo 0), {invisibles.Sum(x => x.area) / 1_000_000:0.00} Mpx por cuadro tirados");
                    w.WriteLine("# el conteo importa tanto como los Mpx: cada una puede costar un cambio de textura, o sea un draw call.");

                    foreach (var g in invisibles.GroupBy(x => x.path.Split(" > ")[0]).OrderByDescending(g => g.Count()).Take(12))
                        w.WriteLine($"#   {g.Count(),5} bajo {g.Key}");
                    
                    w.WriteLine("# suele ser AlwaysPresent en un overlay cerrado: mantiene el Scheduler vivo, pero tambien el dibujo.");

                    foreach (var d in invisibles.OrderByDescending(x => x.area).Take(20))
                        w.WriteLine($"{d.area / 1_000_000,12:0.000}  {d.path}");
                }
            }

            // Debug y NO Important: Important genera una NOTIFICACION en pantalla, y cada
            // toast son ~80 drawables que quedan en el historial del overlay. Con el censo
            // corriendo cada 8s, el propio instrumento hacia crecer lo que estaba midiendo
            // (676 -> 1065 drawables en un minuto, parado en el menu sin tocar nada).
            // Y las estadisticas globales registradas. El censo mostro que los
            // StatisticsItem del propio overlay de Global Statistics crecen ~10 por
            // segundo: si el que crece es el registro, alguien esta pidiendo una
            // estadistica con un nombre distinto cada vez. Esta lista lo nombra.
            try
            {
                var stats = osu.Framework.Statistics.GlobalStatistics.GetStatistics().ToList();
                string statsPath = Path.Combine(Path.GetTempPath(), $"draw-census-stats-{label}-{DateTime.Now:HHmmss}.txt");

                using (var w = new StreamWriter(statsPath))
                {
                    w.WriteLine($"# {stats.Count} estadisticas globales registradas");

                    foreach (var g in stats.GroupBy(x => x.Group).OrderByDescending(g => g.Count()))
                        w.WriteLine($"{g.Count(),6}  {g.Key}");
                }
            }
            catch (Exception e)
            {
                Logger.Log($"[DrawCensus] no pude leer las estadisticas globales: {e.Message}", level: LogLevel.Debug);
            }

            Logger.Log($"[DrawCensus] dump escrito en {path}", level: LogLevel.Debug);
        }
    }
}
