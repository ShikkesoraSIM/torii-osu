// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Extensions;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Performance;
using osu.Game.Rulesets;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;
using osu.Game.Screens.Play;
using Realms;

namespace osu.Game.Database
{
    /// <summary>
    /// Performs background updating of data stores at startup.
    /// </summary>
    public partial class BackgroundDataStoreProcessor : Component
    {
        protected Task ProcessingTask { get; private set; } = null!;

        [Resolved]
        private RulesetStore rulesetStore { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private ScoreManager scoreManager { get; set; } = null!;

        [Resolved]
        private RealmAccess realmAccess { get; set; } = null!;

        [Resolved]
        private IBeatmapUpdater beatmapUpdater { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> gameBeatmap { get; set; } = null!;

        [Resolved]
        private ILocalUserPlayInfo? localUserPlayInfo { get; set; }

        [Resolved]
        private IHighPerformanceSessionManager? highPerformanceSessionManager { get; set; }

        [Resolved]
        private INotificationOverlay? notificationOverlay { get; set; }

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private LocalCachedBeatmapMetadataSource localMetadataSource = null!;

        protected virtual int TimeToSleepDuringGameplay => 30000;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            localMetadataSource = new LocalCachedBeatmapMetadataSource(storage);

            ProcessingTask = Task.Factory.StartNew(() =>
            {
                Logger.Log("Beginning background data store processing..");

                reconcileDifficultyVersions();
                populateMissingStarRatings();
                processOnlineBeatmapSetsWithNoUpdate();
                // Note that the previous method will also update these on a fresh run.
                processBeatmapsWithMissingObjectCounts();
                processScoresWithMissingStatistics();
                // ordering significant, `upgradeModMultipliers()` should run first as it will handle all scores
                // (rather than only lazer scores, if it was called after `convertLegacyTotalScoreToStandardised()`)
                upgradeModMultipliers();
                convertLegacyTotalScoreToStandardised();
                upgradeScoreRanks();
                // torii: desactivamos estos dos pasos de backpopulation porque tiran notificaciones molestas al arranque.
                // las fechas de submit/rank y los user tags igual se llenan al importar (BeatmapUpdaterMetadataLookup), asi que no perdemos nada para mapas nuevos.
                // backpopulateMissingSubmissionAndRankDates();
                // backpopulateUserTags();
            }, TaskCreationOptions.LongRunning).ContinueWith(t =>
            {
                if (t.Exception?.InnerException is ObjectDisposedException)
                {
                    Logger.Log("Finished background aborted during shutdown");
                    return;
                }

                Logger.Log("Finished background data store processing!");
            });
        }

        private const string recalc_journal_filename = @"torii-difficulty-recalc.journal";

        // rulesets que necesitan recalculo COMPLETO in-place, separados por causa: el bump de NUESTRA
        // version muestra el popup; el re-own tras un wipe ajeno y los resumes van en silencio.
        private readonly HashSet<string> versionBumpRulesets = new HashSet<string>();
        private readonly HashSet<string> foreignReownRulesets = new HashSet<string>();

        // resume de una corrida interrumpida (journal privado): targets por ruleset + GUIDs pendientes.
        private readonly Dictionary<string, int> journalTargets = new Dictionary<string, int>();
        private readonly HashSet<Guid> journalRemaining = new HashSet<Guid>();

        private readonly Dictionary<string, int> currentDifficultyVersions = new Dictionary<string, int>();

        private readonly object journalLock = new object();

        /// <summary>
        /// torii: reconciliacion de versiones de difficulty pensada para COMPARTIR la realm con el
        /// cliente oficial. la clave: vanilla solo reacciona a dos cosas — el stamp compartido
        /// (RulesetInfo.LastAppliedDifficultyVersion menor a su version => wipe total a -1) y las filas
        /// con StarRating -1 (las repuebla con SU calculadora). nunca mira los valores. entonces:
        ///  1. subimos el stamp compartido (solo hacia ARRIBA) al espejo de la version actual de
        ///     upstream (<see cref="UpstreamDifficultyVersions"/>): vanilla nunca ve staleness.
        ///  2. nuestra staleness vive en torii.ini (ToriiAppliedDifficultyVersions), nunca en la realm,
        ///     y nuestros recalcs son IN-PLACE (jamas escribimos -1): no le dejamos trabajo colgado a
        ///     vanilla si nos cortan a mitad de camino.
        ///  3. si vanilla bumpeo el stamp desde la ultima vez que lo vimos (wipe ajeno, completo o
        ///     cancelado), re-adueniamos el ruleset entero en silencio con el modo de CPU guardado.
        /// resultado: torii->vanilla no dispara nada, vanilla->torii solo recalcula si NUESTRA version
        /// cambio de verdad (popup) o si vanilla piso valores (silencioso). el ping-pong muere.
        /// </summary>
        private void reconcileDifficultyVersions()
        {
            loadRecalcJournal();

            var applied = parseVersionMap(config.Get<string>(OsuSetting.ToriiAppliedDifficultyVersions));
            var seen = parseVersionMap(config.Get<string>(OsuSetting.ToriiSeenRealmDifficultyVersions));

            // primer arranque con stamps privados (build nueva sobre DB existente o DB vanilla ajeno).
            bool firstBoot = applied.Count == 0 && journalTargets.Count == 0;

            foreach (var ruleset in rulesetStore.AvailableRulesets)
            {
                string shortName = ruleset.ShortName;

                // beatmap being passed in is arbitrary here. just needs to be non-null.
                int currentVersion = ruleset.CreateInstance().CreateDifficultyCalculator(gameBeatmap.Value).Version;
                currentDifficultyVersions[shortName] = currentVersion;

                // stamp compartido: solo lo SUBIMOS al espejo de upstream (nunca escribimos nuestra
                // version ni lo bajamos). con stamp >= a la version de vanilla, vanilla no wipea nada.
                int mirror = UpstreamDifficultyVersions.For(shortName);

                if (ruleset.LastAppliedDifficultyVersion < mirror)
                {
                    realmAccess.Write(r => r.Find<RulesetInfo>(shortName)!.LastAppliedDifficultyVersion = mirror);
                    Logger.Log($"[difficulty] stamp compartido de {shortName} subido a {mirror} (espejo upstream, protege al cliente oficial)");
                }

                if (firstBoot)
                    continue;

                // resume: si quedo journal para este ruleset hay una corrida a medias — se retoma en
                // silencio (la eleccion de CPU ya se hizo aquella vez), sin re-flaggear el bump.
                if (journalTargets.ContainsKey(shortName))
                    continue;

                if (applied.GetValueOrDefault(shortName) < currentVersion)
                {
                    Logger.Log($"[difficulty] version torii de {shortName} cambio ({applied.GetValueOrDefault(shortName)} -> {currentVersion}): recalc completo con popup");
                    versionBumpRulesets.Add(shortName);
                    continue;
                }

                // wipe ajeno: el stamp de la realm cambio desde la ultima vez que lo vimos => vanilla
                // recalculo (completo o cancelado, dejando -1s y/o valores suyos). re-own silencioso.
                int realmStamp = realmAccess.Run(r => r.Find<RulesetInfo>(shortName)?.LastAppliedDifficultyVersion ?? 0);
                int lastSeen = seen.GetValueOrDefault(shortName);

                if (lastSeen != 0 && realmStamp != lastSeen)
                {
                    Logger.Log($"[difficulty] stamp compartido de {shortName} cambio afuera ({lastSeen} -> {realmStamp}): re-own silencioso de los star ratings");
                    foreignReownRulesets.Add(shortName);
                }
            }

            if (firstBoot)
                runFirstBootDifficultyMigration(applied, seen);
        }

        /// <summary>
        /// migracion one-shot: sampleamos ~30 mapas por ruleset y comparamos el SR guardado contra
        /// nuestra calculadora. si todo matchea, el DB ya es torii y sellamos el stamp privado SIN
        /// recalcular (el caso comun al actualizar); si no, es un DB con valores ajenos y se flaggea
        /// el recalc completo con popup (onboarding normal sobre un DB de vanilla).
        /// </summary>
        private void runFirstBootDifficultyMigration(Dictionary<string, int> applied, Dictionary<string, int> seen)
        {
            Logger.Log("[difficulty] primera corrida con stamps privados: sampleando SR guardados para ver de que calculadora son");

            foreach (var rulesetInfo in rulesetStore.AvailableRulesets)
            {
                string shortName = rulesetInfo.ShortName;

                var candidates = new List<Guid>();

                realmAccess.Run(r =>
                {
                    foreach (var b in r.All<BeatmapInfo>().Where(b => b.StarRating >= 0 && b.BeatmapSet != null))
                    {
                        if (b.Ruleset.ShortName == shortName)
                            candidates.Add(b.ID);
                    }
                });

                bool matches = true;

                if (candidates.Count > 0)
                {
                    var ruleset = rulesetInfo.CreateInstance();
                    int step = Math.Max(1, candidates.Count / 30);

                    for (int i = 0; i < candidates.Count; i += step)
                    {
                        var beatmap = realmAccess.Run(r => r.Find<BeatmapInfo>(candidates[i])?.Detach());

                        if (beatmap == null)
                            continue;

                        try
                        {
                            double stored = beatmap.StarRating;
                            double computed = ruleset.CreateDifficultyCalculator(beatmapManager.GetWorkingBeatmap(beatmap)).Calculate().StarRating;

                            if (Math.Abs(stored - computed) > 0.005)
                            {
                                matches = false;
                                break;
                            }
                        }
                        catch
                        {
                            // mapa corrupto: no cuenta para la decision.
                        }
                    }
                }

                if (matches)
                {
                    applied[shortName] = currentDifficultyVersions.GetValueOrDefault(shortName);
                    Logger.Log($"[difficulty] {shortName}: los SR guardados ya son nuestros, sello sin recalcular");
                }
                else
                {
                    Logger.Log($"[difficulty] {shortName}: los SR guardados son de otra calculadora, recalc completo con popup");
                    versionBumpRulesets.Add(shortName);
                }

                seen[shortName] = realmAccess.Run(r => r.Find<RulesetInfo>(shortName)?.LastAppliedDifficultyVersion ?? 0);
            }

            config.SetValue(OsuSetting.ToriiAppliedDifficultyVersions, formatVersionMap(applied));
            config.SetValue(OsuSetting.ToriiSeenRealmDifficultyVersions, formatVersionMap(seen));
        }

        private static Dictionary<string, int> parseVersionMap(string packed)
        {
            var result = new Dictionary<string, int>();

            foreach (string part in (packed ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                int i = part.IndexOf(':');

                if (i > 0 && int.TryParse(part[(i + 1)..], out int v))
                    result[part[..i]] = v;
            }

            return result;
        }

        private static string formatVersionMap(Dictionary<string, int> map)
            => string.Join(',', map.Select(kv => $"{kv.Key}:{kv.Value}"));

        private void loadRecalcJournal()
        {
            try
            {
                if (!storage.Exists(recalc_journal_filename))
                    return;

                using (var stream = storage.GetStream(recalc_journal_filename))
                using (var reader = new System.IO.StreamReader(stream))
                {
                    string? header = reader.ReadLine();

                    if (string.IsNullOrEmpty(header))
                        return;

                    foreach (var kv in parseVersionMap(header))
                        journalTargets[kv.Key] = kv.Value;

                    string? line;

                    while ((line = reader.ReadLine()) != null)
                    {
                        if (Guid.TryParse(line, out var id))
                            journalRemaining.Add(id);
                    }
                }

                Logger.Log($"[difficulty] journal encontrado: {journalRemaining.Count} mapas pendientes de una corrida anterior ({string.Join(", ", journalTargets.Keys)})");
            }
            catch (Exception e)
            {
                // journal corrupto: lo ignoramos; los stamps privados viejos re-flaggean el recalc
                // completo del ruleset que corresponda (redo total, seguro aunque lento).
                Logger.Log($"[difficulty] journal ilegible, se descarta: {e.Message}");
                journalTargets.Clear();
                journalRemaining.Clear();
            }
        }

        private void writeRecalcJournal(Dictionary<string, int> targets, HashSet<Guid> remaining)
        {
            try
            {
                lock (journalLock)
                {
                    Guid[] snapshot;

                    lock (remaining)
                        snapshot = remaining.ToArray();

                    using (var stream = storage.CreateFileSafely(recalc_journal_filename))
                    using (var writer = new System.IO.StreamWriter(stream))
                    {
                        writer.WriteLine(formatVersionMap(targets));

                        foreach (var id in snapshot)
                            writer.WriteLine(id.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Log($"[difficulty] no pude escribir el journal: {e.Message}");
            }
        }

        private void deleteRecalcJournal()
        {
            try
            {
                if (storage.Exists(recalc_journal_filename))
                    storage.Delete(recalc_journal_filename);
            }
            catch
            {
            }
        }

        /// <summary>
        /// sella los stamps privados tras una corrida completa: Applied para los rulesets recalculados
        /// y SeenRealm con los stamps actuales de la realm (para detectar el proximo wipe ajeno).
        /// </summary>
        private void persistDifficultyStamps(Dictionary<string, int> completedTargets)
        {
            var applied = parseVersionMap(config.Get<string>(OsuSetting.ToriiAppliedDifficultyVersions));

            foreach (var kv in completedTargets)
                applied[kv.Key] = kv.Value;

            var seen = new Dictionary<string, int>();

            realmAccess.Run(r =>
            {
                foreach (var ruleset in rulesetStore.AvailableRulesets)
                    seen[ruleset.ShortName] = r.Find<RulesetInfo>(ruleset.ShortName)?.LastAppliedDifficultyVersion ?? 0;
            });

            config.SetValue(OsuSetting.ToriiAppliedDifficultyVersions, formatVersionMap(applied));
            config.SetValue(OsuSetting.ToriiSeenRealmDifficultyVersions, formatVersionMap(seen));
        }

        /// <remarks>
        /// This is split out from <see cref="processOnlineBeatmapSetsWithNoUpdate"/> as a separate process to prevent high server-side load
        /// from the <see cref="beatmapUpdater"/> firing online requests as part of the update.
        /// Star rating recalculations can be ran strictly locally.
        /// </remarks>
        private void populateMissingStarRatings()
        {
            HashSet<Guid> beatmapIds = new HashSet<Guid>();

            Logger.Log("Querying for beatmaps with missing star ratings...");

            // sentinels -1: imports frescos o los restos de un wipe ajeno cancelado. NOSOTROS nunca
            // escribimos -1 (los recalcs torii son in-place, ver reconcileDifficultyVersions).
            realmAccess.Run(r =>
            {
                foreach (var b in r.All<BeatmapInfo>().Where(b => b.StarRating < 0 && b.BeatmapSet != null))
                    beatmapIds.Add(b.ID);
            });

            // rulesets enteros flaggeados (bump de nuestra version / re-own tras wipe ajeno).
            var fullRulesets = new HashSet<string>(versionBumpRulesets);
            fullRulesets.UnionWith(foreignReownRulesets);

            if (fullRulesets.Count > 0)
            {
                realmAccess.Run(r =>
                {
                    foreach (var b in r.All<BeatmapInfo>().Where(b => b.BeatmapSet != null))
                    {
                        if (fullRulesets.Contains(b.Ruleset.ShortName))
                            beatmapIds.Add(b.ID);
                    }
                });
            }

            // resume de una corrida interrumpida.
            beatmapIds.UnionWith(journalRemaining);

            // torii: avisamos al popup de arranque cuantos mapas hay (0 = ninguno). el popup SOLO se
            // muestra cuando nuestra propia version cambio (bump); wipes ajenos, resumes y backfills de
            // imports corren en silencio con el modo de CPU ya guardado.
            bool interactive = versionBumpRulesets.Count > 0;
            ToriiDifficultyRecalcCoordinator.AnnouncePending(beatmapIds.Count, interactive);

            if (beatmapIds.Count == 0)
            {
                // nada que hacer: igual actualizamos el "ultimo stamp visto" para la deteccion de wipes.
                persistDifficultyStamps(new Dictionary<string, int>());
                return;
            }

            Logger.Log($"Found {beatmapIds.Count} beatmaps which require star rating reprocessing.");

            // journal: si hay rulesets enteros en juego, anotamos el progreso en storage privado para
            // retomar sin re-preguntar si esta corrida se corta (crash / cierre / cancel).
            var journalTargetsToWrite = new Dictionary<string, int>(journalTargets);

            foreach (string s in fullRulesets)
                journalTargetsToWrite[s] = currentDifficultyVersions.GetValueOrDefault(s);

            bool useJournal = journalTargetsToWrite.Count > 0;
            var remaining = useJournal ? new HashSet<Guid>(beatmapIds) : null;

            if (useJournal)
                writeRecalcJournal(journalTargetsToWrite, remaining!);

            ToriiDifficultyRecalcMode recalcMode = resolveDifficultyRecalcMode();
            int parallelism = ToriiDifficultyRecalc.ParallelismFor(recalcMode);
            Logger.Log($"Star rating recompute using {recalcMode} ({parallelism} thread(s)).");

            var notification = showProgressNotification(beatmapIds.Count, "Reprocessing star rating for beatmaps", "beatmaps' star ratings have been updated");

            int processedCount = 0;
            int failedCount = 0;

            // el SR de cada mapa es CPU-bound e independiente, asi que paralelizamos. GetWorkingBeatmap
            // lockea solo el lookup del cache; el decode pesado + el calculo van en paralelo de verdad.
            var rulesetCache = new ConcurrentDictionary<string, Ruleset>();

            Ruleset getRuleset(RulesetInfo rulesetInfo) => rulesetCache.GetOrAdd(rulesetInfo.ShortName, _ => rulesetInfo.CreateInstance());

            Parallel.ForEach(beatmapIds, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, (id, loopState) =>
            {
                if (notification?.State == ProgressNotificationState.Cancelled)
                {
                    loopState.Stop();
                    return;
                }

                // se duerme solo si hay gameplay / sesion high-perf activa (no traba el juego).
                sleepIfRequired();

                var beatmap = realmAccess.Run(r => r.Find<BeatmapInfo>(id)?.Detach());

                if (beatmap == null)
                    return;

                try
                {
                    var working = beatmapManager.GetWorkingBeatmap(beatmap);
                    var ruleset = getRuleset(working.BeatmapInfo.Ruleset);

                    Debug.Assert(ruleset != null);

                    var calculator = ruleset.CreateDifficultyCalculator(working);

                    double starRating = calculator.Calculate().StarRating;
                    realmAccess.Write(r =>
                    {
                        if (r.Find<BeatmapInfo>(id) is BeatmapInfo liveBeatmapInfo)
                            liveBeatmapInfo.StarRating = starRating;
                    });
                    ((IWorkingBeatmapCache)beatmapManager).Invalidate(beatmap);

                    int done = Interlocked.Increment(ref processedCount);
                    if (done % 25 == 0 || done == beatmapIds.Count)
                        updateNotificationProgress(notification, done, beatmapIds.Count);

                    if (remaining != null)
                    {
                        int left;

                        lock (remaining)
                        {
                            remaining.Remove(id);
                            left = remaining.Count;
                        }

                        // snapshot periodico del journal, asi un corte pierde como mucho ~500 mapas.
                        if (done % 500 == 0 && left > 0)
                            writeRecalcJournal(journalTargetsToWrite, remaining);
                    }
                }
                catch (Exception e)
                {
                    Logger.Log($"Background processing failed on {beatmap}: {e}");
                    Interlocked.Increment(ref failedCount);

                    // un mapa corrupto no debe trabar el resume para siempre: lo damos por visto.
                    if (remaining != null)
                    {
                        lock (remaining)
                            remaining.Remove(id);
                    }
                }
            });

            completeNotification(notification, processedCount, beatmapIds.Count, failedCount);

            bool cancelled = notification?.State == ProgressNotificationState.Cancelled;

            if (!cancelled)
            {
                // corrida completa: sellamos stamps privados y limpiamos el journal.
                deleteRecalcJournal();
                persistDifficultyStamps(journalTargetsToWrite);
            }
            else if (useJournal)
            {
                // cancelada: snapshot final para retomar (en silencio) el proximo arranque.
                writeRecalcJournal(journalTargetsToWrite, remaining!);
            }
        }

        private ToriiDifficultyRecalcMode resolveDifficultyRecalcMode()
        {
            // esperamos que el popup de arranque resuelva (timeout largo por si el usuario tarda). si nunca
            // aparece / se cierra, cae al modo guardado (default gentil de lazer). el popup en si decide si
            // mostrarse (cantidad de mapas + una vez por sesion); aca solo esperamos su eleccion.
            var fallback = config.Get<ToriiDifficultyRecalcMode>(OsuSetting.ToriiDifficultyRecalcMode);
            return ToriiDifficultyRecalcCoordinator.WaitForChoice(TimeSpan.FromMinutes(3), fallback);
        }

        private void processOnlineBeatmapSetsWithNoUpdate()
        {
            HashSet<Guid> beatmapSetIds = new HashSet<Guid>();

            Logger.Log("Querying for beatmap sets to reprocess...");

            realmAccess.Run(r =>
            {
                // BeatmapProcessor is responsible for both online and local processing.
                // In the case a user isn't logged in, it won't update LastOnlineUpdate and therefore re-queue,
                // causing overhead from the non-online processing to redundantly run every startup.
                //
                // We may eventually consider making the Process call more specific (or avoid this in any number
                // of other possible ways), but for now avoid queueing if the user isn't logged in at startup.
                if (api.IsLoggedIn)
                {
                    foreach (var b in r.All<BeatmapInfo>().Where(b => b.OnlineID > 0 && b.LastOnlineUpdate == null && b.BeatmapSet != null))
                        beatmapSetIds.Add(b.BeatmapSet!.ID);
                }
            });

            if (beatmapSetIds.Count == 0)
                return;

            Logger.Log($"Found {beatmapSetIds.Count} beatmap sets which require online updates.");

            var notification = showProgressNotification(beatmapSetIds.Count, "Updating online data for beatmaps", "beatmaps' online data have been updated");

            int processedCount = 0;
            int failedCount = 0;

            foreach (var id in beatmapSetIds)
            {
                if (notification?.State == ProgressNotificationState.Cancelled)
                    break;

                updateNotificationProgress(notification, processedCount, beatmapSetIds.Count);

                sleepIfRequired();

                realmAccess.Run(r =>
                {
                    var set = r.Find<BeatmapSetInfo>(id);

                    if (set != null)
                    {
                        try
                        {
                            beatmapUpdater.Process(set);
                            ++processedCount;
                        }
                        catch (Exception e)
                        {
                            Logger.Log($"Background processing failed on {set}: {e}");
                            ++failedCount;
                        }
                    }
                });
            }

            completeNotification(notification, processedCount, beatmapSetIds.Count, failedCount);
        }

        private void processBeatmapsWithMissingObjectCounts()
        {
            Logger.Log("Querying for beatmaps with missing hitobject counts to reprocess...");

            HashSet<Guid> beatmapIds = new HashSet<Guid>();

            realmAccess.Run(r =>
            {
                foreach (var b in r.All<BeatmapInfo>().Where(b => b.TotalObjectCount < 0 || b.EndTimeObjectCount < 0))
                    beatmapIds.Add(b.ID);
            });

            if (beatmapIds.Count == 0)
                return;

            Logger.Log($"Found {beatmapIds.Count} beatmaps which require statistics population.");

            var notification = showProgressNotification(beatmapIds.Count, "Populating missing statistics for beatmaps", "beatmaps have been populated with missing statistics");

            int processedCount = 0;
            int failedCount = 0;

            foreach (var id in beatmapIds)
            {
                if (notification?.State == ProgressNotificationState.Cancelled)
                    break;

                updateNotificationProgress(notification, processedCount, beatmapIds.Count);

                sleepIfRequired();

                realmAccess.Run(r =>
                {
                    var beatmap = r.Find<BeatmapInfo>(id);

                    if (beatmap != null)
                    {
                        try
                        {
                            beatmapUpdater.ProcessObjectCounts(beatmap);
                            ++processedCount;
                        }
                        catch (Exception e)
                        {
                            Logger.Log($"Background processing failed on {beatmap}: {e}");
                            ++failedCount;
                        }
                    }
                });
            }

            completeNotification(notification, processedCount, beatmapIds.Count, failedCount);
        }

        private void processScoresWithMissingStatistics()
        {
            HashSet<Guid> scoreIds = new HashSet<Guid>();

            Logger.Log("Querying for scores to reprocess...");

            realmAccess.Run(r =>
            {
                foreach (var score in r.All<ScoreInfo>().Where(s => !s.BackgroundReprocessingFailed))
                {
                    if (score.BeatmapInfo != null
                        && score.Statistics.Sum(kvp => kvp.Value) > 0
                        && score.MaximumStatistics.Sum(kvp => kvp.Value) == 0)
                    {
                        scoreIds.Add(score.ID);
                    }
                }
            });

            if (scoreIds.Count == 0)
                return;

            Logger.Log($"Found {scoreIds.Count} scores which require statistics population.");

            var notification = showProgressNotification(scoreIds.Count, "Populating missing statistics for scores", "scores have been populated with missing statistics");

            int processedCount = 0;
            int failedCount = 0;

            foreach (var id in scoreIds)
            {
                if (notification?.State == ProgressNotificationState.Cancelled)
                    break;

                updateNotificationProgress(notification, processedCount, scoreIds.Count);

                sleepIfRequired();

                try
                {
                    var score = scoreManager.Query(s => s.ID == id);

                    if (score != null)
                    {
                        scoreManager.PopulateMaximumStatistics(score);

                        // Can't use async overload because we're not on the update thread.
                        // ReSharper disable once MethodHasAsyncOverload
                        realmAccess.Write(r =>
                        {
                            r.Find<ScoreInfo>(id)!.MaximumStatisticsJson = JsonConvert.SerializeObject(score.MaximumStatistics);
                        });
                    }

                    ++processedCount;
                }
                catch (ObjectDisposedException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Logger.Log(@$"Failed to populate maximum statistics for {id}: {e}");
                    realmAccess.Write(r => r.Find<ScoreInfo>(id)!.BackgroundReprocessingFailed = true);
                    ++failedCount;
                }
            }

            completeNotification(notification, processedCount, scoreIds.Count, failedCount);
        }

        private void upgradeModMultipliers()
        {
            Logger.Log("Querying for scores that need mod multiplier upgrade...");

            HashSet<Guid> scoreIds = realmAccess.Run(r => new HashSet<Guid>(
                r.All<ScoreInfo>()
                 .Where(s => !s.BackgroundReprocessingFailed
                             && s.BeatmapInfo != null
                             && s.TotalScoreVersion < 30000017 // version number represents version with latest mod multiplier change
                             && s.TotalScoreWithoutMods > 0)
                 .AsEnumerable()
                 // must be done after materialisation, as realm doesn't want to support
                 // nested property predicates
                 .Where(s => s.Ruleset.IsLegacyRuleset())
                 .Select(s => s.ID)));

            Logger.Log($"Found {scoreIds.Count} scores which require mod multiplier upgrade.");

            if (scoreIds.Count == 0)
                return;

            var notification = showProgressNotification(scoreIds.Count, "Upgrading scores to new mod multipliers", "scores have been upgraded to the new mod multipliers");

            int processedCount = 0;
            int failedCount = 0;

            foreach (var id in scoreIds)
            {
                if (notification?.State == ProgressNotificationState.Cancelled)
                    break;

                updateNotificationProgress(notification, processedCount, scoreIds.Count);

                sleepIfRequired();

                try
                {
                    // Can't use async overload because we're not on the update thread.
                    // ReSharper disable once MethodHasAsyncOverload
                    realmAccess.Write(r =>
                    {
                        ScoreInfo s = r.Find<ScoreInfo>(id)!;
                        if (s.BeatmapInfo == null)
                            return;

                        StandardisedScoreMigrationTools.UpdateToLatestScoreMultipliers(s, s.BeatmapInfo.Difficulty);
                        s.TotalScoreVersion = LegacyScoreEncoder.LATEST_VERSION;
                    });

                    ++processedCount;
                }
                catch (ObjectDisposedException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Logger.Log($"Failed to upgrade mod multipliers for {id}: {e}");
                    realmAccess.Write(r => r.Find<ScoreInfo>(id)!.BackgroundReprocessingFailed = true);
                    ++failedCount;
                }
            }

            completeNotification(notification, processedCount, scoreIds.Count, failedCount);
        }

        private void convertLegacyTotalScoreToStandardised()
        {
            Logger.Log("Querying for scores that need total score conversion...");

            HashSet<Guid> scoreIds = realmAccess.Run(r => new HashSet<Guid>(
                r.All<ScoreInfo>()
                 .Where(s => !s.BackgroundReprocessingFailed
                             && s.BeatmapInfo != null
                             && s.IsLegacyScore
                             && s.TotalScoreVersion < LegacyScoreEncoder.LATEST_VERSION)
                 .AsEnumerable()
                 // must be done after materialisation, as realm doesn't want to support
                 // nested property predicates
                 .Where(s => s.Ruleset.IsLegacyRuleset())
                 .Select(s => s.ID)));

            Logger.Log($"Found {scoreIds.Count} scores which require total score conversion.");

            if (scoreIds.Count == 0)
                return;

            var notification = showProgressNotification(scoreIds.Count, "Upgrading scores to new scoring algorithm", "scores have been upgraded to the new scoring algorithm");

            int processedCount = 0;
            int failedCount = 0;

            foreach (var id in scoreIds)
            {
                if (notification?.State == ProgressNotificationState.Cancelled)
                    break;

                updateNotificationProgress(notification, processedCount, scoreIds.Count);

                sleepIfRequired();

                try
                {
                    // Can't use async overload because we're not on the update thread.
                    // ReSharper disable once MethodHasAsyncOverload
                    realmAccess.Write(r =>
                    {
                        ScoreInfo s = r.Find<ScoreInfo>(id)!;
                        StandardisedScoreMigrationTools.UpdateFromLegacy(s, beatmapManager.GetWorkingBeatmap(s.BeatmapInfo));
                        s.TotalScoreVersion = LegacyScoreEncoder.LATEST_VERSION;
                    });

                    ++processedCount;
                }
                catch (ObjectDisposedException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Logger.Log($"Failed to convert total score for {id}: {e}");
                    realmAccess.Write(r => r.Find<ScoreInfo>(id)!.BackgroundReprocessingFailed = true);
                    ++failedCount;
                }
            }

            completeNotification(notification, processedCount, scoreIds.Count, failedCount);
        }

        private void upgradeScoreRanks()
        {
            Logger.Log("Querying for scores that need rank upgrades...");

            HashSet<Guid> scoreIds = realmAccess.Run(r => new HashSet<Guid>(
                r.All<ScoreInfo>()
                 .Where(s => s.TotalScoreVersion < 30000013 && !s.BackgroundReprocessingFailed) // last total score version with a significant change to ranks
                 .AsEnumerable()
                 // must be done after materialisation, as realm doesn't support
                 // filtering on nested property predicates or projection via `.Select()`
                 .Where(s => s.Ruleset.IsLegacyRuleset())
                 .Select(s => s.ID)));

            Logger.Log($"Found {scoreIds.Count} scores which require rank upgrades.");

            if (scoreIds.Count == 0)
                return;

            var notification = showProgressNotification(scoreIds.Count, "Adjusting ranks of scores", "scores now have more correct ranks.");

            int processedCount = 0;
            int failedCount = 0;

            foreach (var id in scoreIds)
            {
                if (notification?.State == ProgressNotificationState.Cancelled)
                    break;

                updateNotificationProgress(notification, processedCount, scoreIds.Count);

                sleepIfRequired();

                try
                {
                    // Can't use async overload because we're not on the update thread.
                    // ReSharper disable once MethodHasAsyncOverload
                    realmAccess.Write(r =>
                    {
                        ScoreInfo s = r.Find<ScoreInfo>(id)!;
                        s.Rank = StandardisedScoreMigrationTools.ComputeRank(s);
                        s.TotalScoreVersion = LegacyScoreEncoder.LATEST_VERSION;
                    });

                    ++processedCount;
                }
                catch (ObjectDisposedException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Logger.Log($"Failed to update rank score {id}: {e}");
                    realmAccess.Write(r => r.Find<ScoreInfo>(id)!.BackgroundReprocessingFailed = true);
                    ++failedCount;
                }
            }

            completeNotification(notification, processedCount, scoreIds.Count, failedCount);
        }

        private void backpopulateMissingSubmissionAndRankDates()
        {
            if (!localMetadataSource.Available)
            {
                Logger.Log("Cannot backpopulate missing submission/rank dates because the local metadata cache is missing.");
                return;
            }

            try
            {
                if (!localMetadataSource.IsAtLeastVersion(2))
                {
                    Logger.Log("Cannot backpopulate missing submission/rank dates because the local metadata cache is too old.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"Error when trying to query version of local metadata cache: {ex}");
                return;
            }

            Logger.Log("Querying for beatmap sets that contain missing submission/rank date...");

            // find all ranked beatmap sets with missing date ranked or date submitted that have at least one difficulty ranked as well.
            // the reason for checking ranked status of the difficulties is that they can be locally modified or unknown too, and for those the lookup is likely to fail.
            // this is because metadata lookups are primarily based on file hash, so they will fail to match if the beatmap does not match the online version
            // (which is likely to be the case if the beatmap is locally modified or unknown).
            // that said, one difficulty in ranked state is enough for the backpopulation to work.
            HashSet<Guid> beatmapSetIds = realmAccess.Run(r => new HashSet<Guid>(
                r.All<BeatmapSetInfo>()
                 .Filter($@"{nameof(BeatmapSetInfo.StatusInt)} > 0 && ({nameof(BeatmapSetInfo.DateRanked)} == null || {nameof(BeatmapSetInfo.DateSubmitted)} == null) "
                         + $@"&& ANY {nameof(BeatmapSetInfo.Beatmaps)}.{nameof(BeatmapInfo.StatusInt)} > 0")
                 .AsEnumerable()
                 .Select(b => b.ID)));

            if (beatmapSetIds.Count == 0)
                return;

            Logger.Log($"Found {beatmapSetIds.Count} beatmap sets with missing submission/rank date.");

            var notification = showProgressNotification(beatmapSetIds.Count, "Populating missing submission and rank dates", "beatmap sets now have correct submission and rank dates.");

            int processedCount = 0;
            int failedCount = 0;

            foreach (var id in beatmapSetIds)
            {
                if (notification?.State == ProgressNotificationState.Cancelled)
                    break;

                updateNotificationProgress(notification, processedCount, beatmapSetIds.Count);

                sleepIfRequired();

                try
                {
                    // Can't use async overload because we're not on the update thread.
                    // ReSharper disable once MethodHasAsyncOverload
                    bool succeeded = realmAccess.Write(r =>
                    {
                        BeatmapSetInfo beatmapSet = r.Find<BeatmapSetInfo>(id)!;

                        var beatmap = beatmapSet.Beatmaps.First(b => b.Status >= BeatmapOnlineStatus.Ranked);

                        bool lookupSucceeded = localMetadataSource.TryLookup(beatmap, out var result);

                        if (lookupSucceeded)
                        {
                            Debug.Assert(result != null);
                            beatmapSet.DateRanked = result.DateRanked;
                            beatmapSet.DateSubmitted = result.DateSubmitted;
                            return true;
                        }

                        Logger.Log($"Could not find {beatmapSet.GetDisplayString()} in local cache while backpopulating missing submission/rank date");
                        return false;
                    });

                    if (succeeded)
                        ++processedCount;
                    else
                        ++failedCount;
                }
                catch (ObjectDisposedException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Logger.Log($"Failed to update ranked/submitted dates for beatmap set {id}: {e}");
                    ++failedCount;
                }
            }

            completeNotification(notification, processedCount, beatmapSetIds.Count, failedCount);
        }

        private void backpopulateUserTags()
        {
            if (!localMetadataSource.Available || !localMetadataSource.IsAtLeastVersion(3))
            {
                Logger.Log(@"Local metadata cache has too low version to backpopulate user tags, attempting refetch...");
                localMetadataSource.FetchCache().WaitSafely();

                if (!localMetadataSource.Available || !localMetadataSource.IsAtLeastVersion(3))
                {
                    Logger.Log(@"Local metadata cache refetch failed. Aborting user tags backpopulation.");
                    return;
                }
            }

            var lastPopulation = config.Get<DateTime?>(OsuSetting.LastOnlineTagsPopulation);
            // dropping time data here completely is intentional, because storing the date to config is a lossy operation
            // (truncates some ticks off of the date when it's being converted to string and back).
            // therefore, if precision isn't explicitly constrained, the condition below would always fail just because the date stored to config
            // is less accurate than the cache file's fetch date which is stored with higher precision in the filesystem metadata.
            var metadataSourceFetchDate = localMetadataSource.GetCacheFetchDate()?.Date;

            if (metadataSourceFetchDate <= lastPopulation)
            {
                Logger.Log(
                    $@"Skipping user tag population because the local metadata source hasn't been updated since the last time user tags were checked ({lastPopulation.Value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)})");
                return;
            }

            Logger.Log(@"Updating user tags");

            // while this is constrained to run every month or so (every time a new online.db cache is retrieved), there's some chance that this will still run much too often and be annoying to users.
            // if that turns out to be the case we may need a better way to debounce this (or just delete the backpopulation logic after some time has passed?)
            HashSet<Guid> beatmapIds = realmAccess.Run(r => new HashSet<Guid>(
                r.All<BeatmapInfo>()
                 .Filter($"{nameof(BeatmapInfo.StatusInt)} IN {{ 1,2,4 }}")
                 .AsEnumerable()
                 .Select(b => b.ID)));

            if (beatmapIds.Count == 0)
                return;

            Logger.Log($@"Checking for tag updates for {beatmapIds.Count} beatmaps.");

            var notification = showProgressNotification(beatmapIds.Count, @"Updating user tags",
                @"beatmaps have had their tags updated. This runs once a month to allow searching user tags.");

            int processedCount = 0;
            int updatedCount = 0;
            int failedCount = 0;

            foreach (var id in beatmapIds)
            {
                if (notification?.State == ProgressNotificationState.Cancelled)
                    break;

                updateNotificationProgress(notification, processedCount, beatmapIds.Count);

                sleepIfRequired();

                try
                {
                    var beatmap = realmAccess.Run(r => r.Find<BeatmapInfo>(id)?.Detach());

                    if (beatmap == null) continue;

                    bool lookupSucceeded = localMetadataSource.TryLookup(beatmap, out var result);

                    if (lookupSucceeded)
                    {
                        Debug.Assert(result != null);

                        HashSet<string> userTags = result.UserTags.ToHashSet();

                        if (!userTags.SetEquals(beatmap.Metadata.UserTags))
                        {
                            ++updatedCount;
                            realmAccess.Write(r =>
                            {
                                beatmap = r.Find<BeatmapInfo>(id);

                                if (beatmap == null)
                                    return;

                                beatmap.Metadata.UserTags.Clear();
                                beatmap.Metadata.UserTags.AddRange(userTags);
                            });
                        }
                    }
                    else
                    {
                        Logger.Log(@$"Could not find {beatmap.GetDisplayString()} in local cache while backpopulating missing user tags");
                    }

                    ++processedCount;
                }
                catch (ObjectDisposedException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Logger.Log(@$"Failed to update user tags for beatmap {id}: {e}");
                    ++failedCount;
                }
            }

            // Report the updated item count rather than the total processed. Users don't really care about noops here.
            completeNotification(notification, updatedCount, updatedCount, failedCount);

            config.SetValue(OsuSetting.LastOnlineTagsPopulation, metadataSourceFetchDate);
        }

        private void updateNotificationProgress(ProgressNotification? notification, int processedCount, int totalCount)
        {
            if (notification == null)
                return;

            notification.Text = notification.Text.ToString().Split('(').First().TrimEnd() + $" ({processedCount} of {totalCount})";
            notification.Progress = (float)processedCount / totalCount;

            if (processedCount % 100 == 0)
                Logger.Log(notification.Text.ToString());
        }

        private void completeNotification(ProgressNotification? notification, int processedCount, int totalCount, int? failedCount = null)
        {
            if (notification == null)
                return;

            if (totalCount == 0)
            {
                notification.CompleteSilently();
            }
            else if (processedCount == totalCount)
            {
                notification.CompletionText = $"{processedCount} {notification.CompletionText}";
                notification.Progress = 1;
                notification.State = ProgressNotificationState.Completed;
            }
            else
            {
                notification.Text = $"{processedCount} of {totalCount} {notification.CompletionText}";

                // We may have arrived here due to user cancellation or completion with failures.
                if (failedCount > 0)
                    notification.Text += $" Check logs for issues with {failedCount} failed items.";

                notification.State = ProgressNotificationState.Cancelled;
            }
        }

        private ProgressNotification? showProgressNotification(int totalCount, string running, string completed)
        {
            if (notificationOverlay == null)
                return null;

            if (totalCount < 10)
                return null;

            ProgressNotification notification = new ProgressNotification
            {
                Text = running,
                CompletionText = completed,
                State = ProgressNotificationState.Active
            };

            notificationOverlay?.Post(notification);

            return notification;
        }

        private void sleepIfRequired()
        {
            // Importantly, also sleep if high performance session is active.
            // If we don't do this, memory usage can become runaway due to GC running in a more lenient mode.
            while (localUserPlayInfo?.PlayingState.Value != LocalUserPlayingState.NotPlaying || highPerformanceSessionManager?.IsSessionActive == true)
            {
                Logger.Log("Background processing sleeping due to active gameplay...");
                Thread.Sleep(TimeToSleepDuringGameplay);
            }
        }
    }
}
