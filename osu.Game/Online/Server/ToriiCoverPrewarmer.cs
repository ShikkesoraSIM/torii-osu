// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;

namespace osu.Game.Online.Server
{
    /// <summary>
    /// baja las portadas que el pulse esta por mostrar, afuera del update
    /// thread, y se guarda una referencia de cada una para que el store no las
    /// purgue entre apertura y apertura del popover.
    ///
    /// por que existe esto
    /// -------------------
    /// las portadas del popover son Sprites metidos con AddInternal adentro de
    /// un padre ya cargado, asi que su BackgroundDependencyLoader corre
    /// sincronico en el hilo que hizo el add, que siempre es el update thread.
    /// y LargeTextureStore.Get es 100% bloqueante (http + decode del jpeg), o
    /// sea que cada cambio de portada clavaba el frame lo que tardara la
    /// descarga: eso era el tironcito al abrir el pulse y al refrescarse.
    ///
    /// en vez de que cada sprite se baje lo suyo, el provider calienta todas
    /// las urls por aca y recien despues publica el snapshot. cuando el sprite
    /// hace su Get bloqueante, la textura ya esta en el diccionario del store
    /// y vuelve en microsegundos.
    ///
    /// por que las descargas van POR URL y no por tanda
    /// ------------------------------------------------
    /// dos tandas seguidas comparten casi todas las urls (abrir el popover
    /// dispara una, y el poll que cae 300 ms despues dispara otra con el mismo
    /// top). si la segunda volviera a pedirle la misma url al store mientras la
    /// primera sigue bajando, el store la hace ESPERAR a la primera, y esperar
    /// adentro de un thread del pool revienta (WaitSafely). Por eso cada url
    /// tiene una sola descarga viva, y las tandas nuevas se cuelgan de las
    /// descargas que ya estan en vuelo en vez de pisarlas. lo unico que una
    /// tanda nueva cancela de la anterior es el AVISO: siempre avisa la ultima.
    ///
    /// por que ademas las pinea
    /// ------------------------
    /// LargeTextureStore es refcounted: cuando se dispone el ultimo sprite que
    /// usaba una textura, la purga. asi, un mapa que se caia del top 5 y volvia
    /// se re-bajaba entero. guardando una referencia por url el contador nunca
    /// llega a cero. las urls que ya no estan en el ultimo snapshot se sueltan
    /// en el siguiente calentado, asi que esto queda acotado al puñado de
    /// portadas que el popover puede llegar a dibujar.
    /// </summary>
    internal sealed class ToriiCoverPrewarmer : IDisposable
    {
        // valvula para que una descarga colgada no retenga el snapshot para
        // siempre. Tiene que ser MENOR que la cadencia activa del poll (20 s):
        // si fuera mayor, el proximo poll pisa el aviso antes de que el timeout
        // llegue a disparar y la valvula no vale nada.
        private const int warm_timeout_ms = 15000;

        private readonly LargeTextureStore textures;
        private readonly Action<Action> scheduleToUpdateThread;

        // protege TODO el estado de abajo. Warm entra desde el update thread
        // (apertura del popover) y desde el hilo de la API (respuesta del
        // poll), asi que aca no hay ningun "esto solo se toca de un lado".
        private readonly object stateLock = new object();

        private readonly Dictionary<string, Texture> pinned = new Dictionary<string, Texture>(StringComparer.Ordinal);

        // una descarga viva por url, compartida entre tandas.
        private readonly Dictionary<string, Task> inFlight = new Dictionary<string, Task>(StringComparer.Ordinal);

        // urls que el store ya cacheo como rotas (404, sin red). El store
        // cachea el fallo en negativo para siempre, asi que reintentarlas es
        // gratis pero inutil: se dan por "listas" para que la tanda pueda
        // avisar sin esperarlas y el camino rapido siga funcionando.
        private readonly HashSet<string> failed = new HashSet<string>(StringComparer.Ordinal);

        private HashSet<string> wanted = new HashSet<string>(StringComparer.Ordinal);

        // la tanda cuyo aviso vale. Las anteriores quedan mudas.
        private int generation;

        private bool disposed;

        public ToriiCoverPrewarmer(LargeTextureStore textures, Action<Action> scheduleToUpdateThread)
        {
            this.textures = textures;
            this.scheduleToUpdateThread = scheduleToUpdateThread;
        }

        /// <summary>
        /// se asegura de que todas las urls esten en la cache de texturas y
        /// recien ahi llama a <paramref name="onReady"/> en el update thread.
        /// una segunda llamada pisa a la anterior: el callback viejo no se
        /// invoca nunca, pero sus descargas siguen y se reusan.
        /// </summary>
        public void Warm(IEnumerable<string> urls, Action onReady)
        {
            int myGeneration;
            var waitFor = new List<Task>();

            lock (stateLock)
            {
                if (disposed)
                    return;

                myGeneration = ++generation;

                wanted = new HashSet<string>(StringComparer.Ordinal);

                foreach (string url in urls)
                {
                    if (!string.IsNullOrEmpty(url))
                        wanted.Add(url);
                }

                releaseUnwanted();

                foreach (string url in wanted)
                {
                    if (pinned.ContainsKey(url) || failed.Contains(url))
                        continue;

                    if (!inFlight.TryGetValue(url, out var task))
                    {
                        task = fetchOne(url);
                        inFlight[url] = task;
                    }

                    waitFor.Add(task);
                }
            }

            // ya esta todo cacheado, seguimos derecho en el mismo hilo. es el
            // caso normal cuando el usuario abre y cierra el popover seguido.
            if (waitFor.Count == 0)
            {
                onReady();
                return;
            }

            _ = notifyWhenReady(waitFor, myGeneration, onReady);
        }

        private async Task notifyWhenReady(List<Task> waitFor, int myGeneration, Action onReady)
        {
            // WhenAll no tira nunca (fetchOne se traga sus errores). Si gana el
            // timeout se avisa igual con lo que haya: mejor un par de
            // placeholders que un popover que no aparece.
            var all = Task.WhenAll(waitFor);
            await Task.WhenAny(all, Task.Delay(warm_timeout_ms)).ConfigureAwait(false);

            if (!all.IsCompleted)
                Logger.Log($"[ToriiCoverPrewarmer] publishing with {waitFor.Count} cover(s) still in flight after {warm_timeout_ms} ms.", LoggingTarget.Runtime, LogLevel.Verbose);

            lock (stateLock)
            {
                // solo la ultima tanda avisa; y despues de Dispose no avisa nadie.
                if (disposed || generation != myGeneration)
                    return;
            }

            scheduleToUpdateThread(() =>
            {
                lock (stateLock)
                {
                    if (disposed || generation != myGeneration)
                        return;
                }

                onReady();
            });
        }

        private async Task fetchOne(string url)
        {
            try
            {
                Texture texture = await textures.GetAsync(url, CancellationToken.None).ConfigureAwait(false);

                lock (stateLock)
                {
                    inFlight.Remove(url);

                    if (texture == null)
                    {
                        // el store ya cacheo el fallo en negativo: de aca en mas
                        // Get(url) devuelve null al toque, asi que la url cuenta
                        // como resuelta para no arrastrarla en cada tanda.
                        failed.Add(url);
                        return;
                    }

                    if (disposed || !wanted.Contains(url) || pinned.ContainsKey(url))
                    {
                        texture.Dispose();
                        return;
                    }

                    pinned[url] = texture;
                }
            }
            catch (Exception e)
            {
                lock (stateLock)
                {
                    inFlight.Remove(url);
                    failed.Add(url);
                }

                // que una foto no se pueda bajar no puede parar el ciclo, pero
                // tampoco puede desaparecer sin dejar rastro: la ultima vez que
                // un catch vacio se comio esta excepcion, el bug de al lado
                // tardo una sesion entera de debug en aparecer.
                Logger.Log($"[ToriiCoverPrewarmer] cover fetch failed for {url}: {e.GetType().Name}: {e.Message}", LoggingTarget.Runtime, LogLevel.Verbose);
            }
        }

        // se llama con stateLock tomado.
        private void releaseUnwanted()
        {
            if (pinned.Count == 0) return;

            List<string> stale = null;

            foreach (var pin in pinned)
            {
                if (!wanted.Contains(pin.Key))
                    (stale ??= new List<string>()).Add(pin.Key);
            }

            if (stale == null) return;

            foreach (string url in stale)
            {
                pinned[url].Dispose();
                pinned.Remove(url);
            }
        }

        public void Dispose()
        {
            lock (stateLock)
            {
                if (disposed) return;

                disposed = true;

                foreach (var texture in pinned.Values)
                    texture.Dispose();

                pinned.Clear();
                wanted.Clear();
            }
        }
    }
}
