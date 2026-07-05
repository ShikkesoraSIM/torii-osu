// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Threading;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.ScoreNotes
{
    /// <summary>
    /// cache + batcher de notas de score, a nivel juego. los iconitos del leaderboard
    /// hacen Lookup(scoreId, cb) y el store junta los pedidos ~150ms y dispara UNA
    /// request batch por tanda (en vez de 50 requests, una por tarjeta). los resultados
    /// quedan cacheados, asi re-entrar al mismo leaderboard no consulta de nuevo.
    /// todo corre en el update thread (Lookup se llama desde LoadComplete de drawables).
    /// </summary>
    public partial class ScoreNoteStore : Component
    {
        [Resolved]
        private IAPIProvider api { get; set; }

        // scoreId -> nota (solo los que TIENEN nota).
        private readonly Dictionary<long, APIScoreNote> notes = new Dictionary<long, APIScoreNote>();

        // ids ya consultados (con o sin nota): no se vuelven a pedir.
        private readonly HashSet<long> known = new HashSet<long>();

        // callbacks esperando respuesta, por score id.
        private readonly Dictionary<long, List<Action<APIScoreNote>>> waiting = new Dictionary<long, List<Action<APIScoreNote>>>();
        private readonly HashSet<long> pending = new HashSet<long>();
        private ScheduledDelegate flushDelegate;

        /// <summary>pide la nota de un score. si el score tiene nota, el callback se
        /// invoca (inmediato si esta cacheada, o cuando llegue el batch). si no tiene,
        /// no se invoca nada. el caller debe guardarse contra su propio dispose.</summary>
        public void Lookup(long scoreId, Action<APIScoreNote> onFound)
        {
            if (scoreId <= 0 || onFound == null)
                return;

            if (notes.TryGetValue(scoreId, out var cached))
            {
                onFound(cached);
                return;
            }

            if (known.Contains(scoreId))
                return; // consultado y sin nota

            if (!waiting.TryGetValue(scoreId, out var list))
                waiting[scoreId] = list = new List<Action<APIScoreNote>>();
            list.Add(onFound);
            pending.Add(scoreId);

            flushDelegate?.Cancel();
            flushDelegate = Scheduler.AddDelayed(flushPending, 150);
        }

        /// <summary>registra una nota recien creada/editada localmente (sin refetch).</summary>
        public void SetLocal(APIScoreNote note)
        {
            if (note == null || note.ScoreId <= 0) return;

            notes[note.ScoreId] = note;
            known.Add(note.ScoreId);
        }

        /// <summary>marca un score como sin-nota (tras borrarla).</summary>
        public void RemoveLocal(long scoreId)
        {
            notes.Remove(scoreId);
            known.Add(scoreId);
        }

        private void flushPending()
        {
            if (pending.Count == 0)
                return;

            var ids = pending.Take(100).ToList();
            pending.ExceptWith(ids);

            var req = new GetScoreNotesBatchRequest(ids);

            req.Success += resp => Schedule(() =>
            {
                if (IsDisposed) return;

                foreach (long id in ids)
                    known.Add(id);

                foreach (var note in resp.Notes)
                {
                    notes[note.ScoreId] = note;

                    if (waiting.TryGetValue(note.ScoreId, out var callbacks))
                    {
                        foreach (var cb in callbacks)
                            cb(note);
                    }
                }

                foreach (long id in ids)
                    waiting.Remove(id);

                // quedaron mas pedidos encolados mientras viajaba el batch: otra tanda.
                if (pending.Count > 0)
                    flushPending();
            });

            req.Failure += _ => Schedule(() =>
            {
                if (IsDisposed) return;
                // no marcamos known: un proximo Lookup puede reintentar.
                foreach (long id in ids)
                    waiting.Remove(id);
            });

            api?.Queue(req);
        }
    }
}
