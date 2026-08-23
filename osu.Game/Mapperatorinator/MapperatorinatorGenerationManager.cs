// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Mapperatorinator
{
    /// <summary>
    /// Runs Mapperatorinator generations in the background, game-wide. The screen only
    /// collects options and hands over a job; from then on a pinned progress notification
    /// keeps the user informed, the finished map imports itself, and a pinned completion
    /// notification jumps to it on click. Living at game level means closing the screen
    /// (or going off to play something) never kills a run.
    /// </summary>
    public partial class MapperatorinatorGenerationManager : Component
    {
        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved(canBeNull: true)]
        private INotificationOverlay? notifications { get; set; }

        [Resolved(canBeNull: true)]
        private OsuGame? game { get; set; }

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        /// <summary>
        /// Guarda las opciones con las que se genero un mapa como preset con nombre.
        /// El camino natural es este: generaste, te gusto como quedo, y recien ahi
        /// queres guardartelo. No hace falta abrir el generador para eso.
        /// </summary>
        public void SavePresetFromBeatmap(BeatmapInfo beatmap, string name)
        {
            var sidecar = ReadSidecar(beatmap);

            if (sidecar == null)
            {
                notifications?.Post(new SimpleErrorNotification { Text = @"This map doesn't carry the settings it was generated with." });
                return;
            }

            var request = new SaveMapperatorinatorPresetRequest(name, sidecar.Serialize());

            request.Success += preset => Schedule(() => notifications?.Post(new SimpleNotification
            {
                Text = $"Saved \"{preset.Name}\". Pick it from the preset list next time you generate.",
            }));

            request.Failure += e => Schedule(() => notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't save the preset: {e.Message}" }));

            api.Queue(request);
        }

        /// <summary>
        /// The single shared runner (config, install path, speed factors). The screen
        /// uses this same instance so install-path changes are seen everywhere at once.
        /// </summary>
        public MapperatorinatorRunner Runner { get; private set; } = null!;

        private Storage filesStorage = null!;

        // una sola generacion a la vez (una corrida ya satura la gpu); el resto espera
        // en la cola con su notificacion en estado "queued".
        private readonly Queue<MapperatorinatorJob> queue = new Queue<MapperatorinatorJob>();
        private MapperatorinatorJob? currentJob;
        private string device = @"cpu";

        [BackgroundDependencyLoader]
        private void load(Storage storage)
        {
            Runner = new MapperatorinatorRunner(storage.GetFullPath(string.Empty));
            filesStorage = storage.GetStorageForDirectory(@"files");
            device = Runner.EffectiveDevice();
        }

        public static string NewWorkDirectory() => Path.Combine(Path.GetTempPath(), @"torii-mapperatorinator", Guid.NewGuid().ToString(@"N"));

        /// <summary>
        /// Whether this map came out of Mapperatorinator (carries the marker tag).
        /// </summary>
        public static bool IsGeneratedMap(IBeatmapInfo beatmap) =>
            beatmap.Metadata.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                   .Contains(OszPostProcessor.MARKER_TAG, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Builds a job from a beatmap's audio. Must run on the update thread (touches
        /// realm-backed data); everything the background task needs gets snapshotted here.
        /// With <paramref name="addToOwnSet"/> the generated difficulty is merged into the
        /// source beatmap's own set (regenerations / extra difficulties) instead of
        /// becoming a new set, inheriting the set's identity and audio.
        /// </summary>
        public MapperatorinatorJob CreateJobFromBeatmap(BeatmapInfo beatmap, MapperatorinatorRequest request, OszPostProcessor.MetadataOverrides overrides, bool addToOwnSet)
        {
            // el BeatmapInfo del carousel viene desasociado y sin lista de archivos;
            // refetch lo trae entero de realm.
            var working = beatmaps.GetWorkingBeatmap(beatmap, refetch: true);

            string audioFilename = working.Metadata.AudioFile;
            if (string.IsNullOrEmpty(audioFilename))
                throw new InvalidOperationException(@"This beatmap has no audio file.");

            string? storagePath = working.BeatmapSetInfo.GetPathForFile(audioFilename);
            if (storagePath == null)
                throw new InvalidOperationException($"Couldn't locate \"{audioFilename}\" inside the beatmap's files.");

            var job = new MapperatorinatorJob
            {
                Request = request,
                Overrides = overrides,
                OpenAudio = () => working.GetStream(storagePath),
                AudioExtension = Path.GetExtension(audioFilename),
                AudioLengthSeconds = (working.BeatmapInfo.Length > 0 ? working.BeatmapInfo.Length : 180_000) / 1000.0,
            };

            if (addToOwnSet)
            {
                var set = working.BeatmapSetInfo;

                job.TargetSetSnapshot = set;
                job.TargetSetFiles = set.Files.Select(f => (f.Filename, filesStorage.GetFullPath(f.File.GetStoragePath()))).ToList();
                job.TargetSetVersions = set.Beatmaps.Select(b => b.DifficultyName).ToList();
                job.TargetAudioFilename = audioFilename;
                job.TargetBackgroundFilename = emptyToNull(working.Metadata.BackgroundFile);

                // la diff nueva hereda la identidad del set; la metadata del usuario
                // no aplica aca (es por-set, no por-diff).
                overrides.Title = emptyToNull(working.Metadata.Title);
                overrides.Artist = emptyToNull(working.Metadata.Artist);
                overrides.Creator = emptyToNull(working.Metadata.Author.Username);
                overrides.Tags = emptyToNull(working.Metadata.Tags);
                overrides.BackgroundImagePath = null;
            }

            job.DisplayName = overrides.Title ?? emptyToNull(working.Metadata.Title) ?? @"new map";
            return job;
        }

        public MapperatorinatorJob CreateJobFromExternalAudio(string audioPath, MapperatorinatorRequest request, OszPostProcessor.MetadataOverrides overrides)
        {
            if (!File.Exists(audioPath))
                throw new InvalidOperationException($"Audio file no longer exists: {audioPath}");

            return new MapperatorinatorJob
            {
                Request = request,
                Overrides = overrides,
                OpenAudio = () => File.OpenRead(audioPath),
                AudioExtension = Path.GetExtension(audioPath),
                DisplayName = overrides.Title ?? Path.GetFileNameWithoutExtension(audioPath),
            };
        }

        /// <summary>
        /// The generation settings stored inside a generated set, if any. Update thread.
        /// </summary>
        public MapperatorinatorSidecar? ReadSidecar(BeatmapInfo beatmap)
        {
            try
            {
                var working = beatmaps.GetWorkingBeatmap(beatmap, refetch: true);

                string? path = working.BeatmapSetInfo.GetPathForFile(MapperatorinatorSidecar.FILENAME);
                if (path == null)
                    return null;

                using (var stream = working.GetStream(path))
                {
                    if (stream == null)
                        return null;

                    using (var reader = new StreamReader(stream))
                        return MapperatorinatorSidecar.Deserialize(reader.ReadToEnd());
                }
            }
            catch (Exception e)
            {
                Logger.Log($"[mapperatorinator] couldn't read sidecar: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// One-click regenerate: same stored settings, fresh random seed, straight into
        /// the same set as a new difficulty (the old one stays; deleting it is the
        /// user's call). Falls back to sane defaults if the settings sidecar is gone.
        /// </summary>
        public void QuickRegenerate(BeatmapInfo beatmap)
        {
            try
            {
                var sidecar = ReadSidecar(beatmap);

                var request = sidecar?.ToRequest() ?? new MapperatorinatorRequest
                {
                    Difficulty = Math.Clamp(Math.Round(beatmap.StarRating, 1), 1, 10),
                };

                request.Seed = null; // seed nueva = mapa distinto con el mismo estilo
                request.WorkDirectory = NewWorkDirectory();

                Enqueue(CreateJobFromBeatmap(beatmap, request, new OszPostProcessor.MetadataOverrides(), addToOwnSet: true));
            }
            catch (Exception e)
            {
                notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't start the regeneration: {e.Message}" });
            }
        }

        /// <summary>Update thread. Posts the progress notification and starts (or queues) the run.</summary>
        public void Enqueue(MapperatorinatorJob job)
        {
            if (!Runner.InstallLooksValid)
            {
                notifications?.Post(new SimpleErrorNotification { Text = @"Mapperatorinator isn't installed yet. Open it from a map's right-click menu to set it up." });
                return;
            }

            job.Cancellation = new CancellationTokenSource();
            job.Notification = new GenerationProgressNotification
            {
                Text = $"Queued: {job.DisplayName}",
                State = ProgressNotificationState.Queued,
                CancelRequested = () =>
                {
                    job.Cancellation.Cancel();
                    return true;
                },
            };

            notifications?.Post(job.Notification);
            queue.Enqueue(job);
            processNext();
        }

        private void processNext()
        {
            if (currentJob != null)
                return;

            while (queue.TryDequeue(out var job))
            {
                // cancelado mientras esperaba en la cola
                if (job.Cancellation!.IsCancellationRequested)
                    continue;

                currentJob = job;
                run(job);
                return;
            }
        }

        private void run(MapperatorinatorJob job)
        {
            var notification = job.Notification!;
            notification.State = ProgressNotificationState.Active;

            var stopwatch = Stopwatch.StartNew();

            // por job, no al cargar: si reinstalaron pytorch para la gpu, cuenta ya.
            device = Runner.EffectiveDevice();

            // el progreso sale de lo que la tool va diciendo: en que device corre, la
            // etapa en la que esta y su propia barra con tiempo restante. el ETA adivinado
            // por largo de audio erraba por 30% o mas (la pasada de mapeo depende de lo
            // denso que salga el mapa) y dejaba la barra clavada en 95% con "taking longer
            // than estimated".
            var tracker = new MapperatorinatorProgressTracker(device);
            bool warnedCpu = false;

            var ticker = Scheduler.AddDelayed(() =>
            {
                var (progress, detail) = tracker.Render();
                notification.Progress = progress;
                notification.Text = $"Generating {job.DisplayName} · {detail}";
            }, 1000, true);

            var token = job.Cancellation!.Token;

            Task.Run(async () =>
            {
                string? mergeDirectory = null;

                try
                {
                    // antes de gastar minutos de gpu: si falta algo (python, ffmpeg, la
                    // tool) se corta aca con un mensaje que dice QUE falta, no un
                    // "exit code 1" despues.
                    var missing = MapperatorinatorReadiness.Check(Runner).Where(r => !r.Satisfied).ToList();

                    if (missing.Count > 0)
                    {
                        var first = missing[0];
                        throw new MapperatorinatorRunException(
                            $"{first.Title}: {first.Detail}",
                            first.State == RequirementState.Unsupported
                                ? first.Detail
                                : $"{first.Title} is missing ({first.Detail}). Open Mapperatorinator from a map's right-click menu and follow the requirements list.",
                            Runner.LastRunLogPath, Array.Empty<string>());
                    }

                    Directory.CreateDirectory(job.Request.WorkDirectory);
                    job.Request.AudioPath = copyAudioToWorkDir(job.OpenAudio, job.AudioExtension, job.Request.WorkDirectory);

                    string osz = await Runner.GenerateAsync(job.Request, line =>
                    {
                        tracker.Feed(line);
                        Logger.Log($"[mapperatorinator] {line}", level: LogLevel.Verbose);

                        // la tool se cayo a cpu con una gpu en la maquina: que se sepa ya,
                        // no despues de 20 minutos.
                        if (!warnedCpu && tracker.UsesCpuDespiteGpu)
                        {
                            warnedCpu = true;
                            Schedule(() => notifications?.Post(new SimpleNotification
                            {
                                Text = @"Mapperatorinator is generating on the CPU even though this machine has a GPU: the installed pytorch can't use it. Right-click any map, Use this song for Mapperatorinator, and press ""Use the GPU"" in the requirements list. It only replaces pytorch.",
                            }));
                        }
                    }, token).ConfigureAwait(false);

                    token.ThrowIfCancellationRequested();
                    // la velocidad se anota contra el device que de verdad corrio, o un
                    // run en cpu te arruina el estimado de la gpu.
                    Runner.RecordObservedSpeed(job.AudioLengthSeconds, stopwatch.Elapsed, tracker.ActualDevice ?? device);

                    notification.Progress = 0.97f;
                    notification.Text = $"Generating {job.DisplayName} · importing...";

                    Live<BeatmapSetInfo>? imported;
                    string? newDifficultyName;

                    if (job.TargetSetSnapshot == null)
                    {
                        // set nuevo: metadata del usuario + sidecar con los settings usados.
                        job.Overrides.SidecarJson = MapperatorinatorSidecar.FromRequest(job.Request, job.AnnounceToFeed).Serialize();
                        OszPostProcessor.Apply(osz, job.Overrides);
                        newDifficultyName = OszPostProcessor.Peek(osz)?.version;
                        imported = await beatmaps.Import(new ImportTask(osz)).ConfigureAwait(false);
                    }
                    else
                    {
                        (mergeDirectory, newDifficultyName) = buildMergedSet(job, osz);
                        // la notificacion interna no se postea: el progreso visible ya es el nuestro.
                        imported = await beatmaps.ImportAsUpdate(new ProgressNotification(), new ImportTask(mergeDirectory), job.TargetSetSnapshot).ConfigureAwait(false);
                    }

                    if (imported == null)
                        throw new InvalidOperationException(@"the generated map failed to import.");

                    Schedule(() => onCompleted(job, imported, newDifficultyName));
                }
                catch (OperationCanceledException)
                {
                    notification.State = ProgressNotificationState.Cancelled;
                }
                catch (MapperatorinatorRunException e)
                {
                    Logger.Log($"[mapperatorinator] generation failed: {e.Message}\n{string.Join('\n', e.OutputTail)}");
                    notification.State = ProgressNotificationState.Cancelled;

                    // el diagnostico cuando lo hay; si no, las ultimas lineas de la tool,
                    // que son la unica pista real. el click abre el log entero.
                    string text = e.Diagnosis
                                  ?? (e.OutputTail.Count > 0
                                      ? $"Mapperatorinator failed: {e.Message} Last output: {string.Join(" | ", e.OutputTail.Skip(Math.Max(0, e.OutputTail.Count - 3)))}"
                                      : $"Mapperatorinator failed: {e.Message}");

                    Schedule(() => notifications?.Post(new SimpleErrorNotification
                    {
                        Text = $"{text} Click to open the full log.",
                        Activated = () =>
                        {
                            if (File.Exists(e.LogPath))
                                host.OpenFileExternally(e.LogPath);
                            return true;
                        },
                    }));
                }
                catch (Exception e)
                {
                    Logger.Log($"[mapperatorinator] generation failed: {e}");
                    notification.State = ProgressNotificationState.Cancelled;
                    Schedule(() => notifications?.Post(new SimpleErrorNotification { Text = $"Mapperatorinator failed: {e.Message}" }));
                }
                finally
                {
                    try { Directory.Delete(job.Request.WorkDirectory, true); }
                    catch { }

                    if (mergeDirectory != null)
                    {
                        try { Directory.Delete(mergeDirectory, true); }
                        catch { }
                    }

                    Schedule(() =>
                    {
                        ticker.Cancel();
                        currentJob = null;
                        processNext();
                    });
                }
            }, CancellationToken.None);
        }

        private void onCompleted(MapperatorinatorJob job, Live<BeatmapSetInfo> imported, string? newDifficultyName)
        {
            var notification = job.Notification!;

            notification.CompletionText = job.TargetSetSnapshot != null
                ? $"New difficulty ready: {job.DisplayName} [{newDifficultyName}] — click to open it!"
                : $"{job.DisplayName} is ready — click to open it!";

            notification.CompletionClickAction = () =>
            {
                try
                {
                    imported.PerformRead(set =>
                    {
                        var detached = set.Detach();
                        game?.PresentBeatmap(detached, string.IsNullOrEmpty(newDifficultyName) ? null : b => b.DifficultyName == newDifficultyName);
                    });
                }
                catch (Exception e)
                {
                    Logger.Log($"[mapperatorinator] couldn't present the generated map: {e.Message}");
                }

                return true;
            };

            // Completed postea la notificacion de finalizado (pinneada: se queda hasta
            // que la clickeen o la descarten, generar un mapa es una accion deliberada).
            notification.State = ProgressNotificationState.Completed;

            // aviso al feed de discord SOLO cuando el mapa salio con identidad propia
            // (titulo + artista + imagen custom). un mapa generado por probar no spamea.
            if (job.AnnounceToFeed && job.TargetSetSnapshot == null)
                api.Queue(new MapperatorinatorFeedRequest(job.Overrides.Title!, job.Overrides.Artist!, newDifficultyName, job.Request.Model.ConfigName()));
        }

        /// <summary>
        /// Builds a directory holding the existing set's files (byte-identical, which is
        /// what lets ImportAsUpdate keep their scores) plus the freshly generated .osu
        /// repointed at the set's audio/background, under a collision-free diff name.
        /// Runs on the background task; everything it needs was snapshotted at enqueue.
        /// </summary>
        private (string directory, string difficultyName) buildMergedSet(MapperatorinatorJob job, string oszPath)
        {
            string dir = NewWorkDirectory() + @"-merge";
            Directory.CreateDirectory(dir);

            foreach ((string filename, string sourcePath) in job.TargetSetFiles)
            {
                string dest = Path.Combine(dir, filename);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(sourcePath, dest, true);
            }

            // el nombre de la diff nueva es el que genero la tool, des-colisionado
            // contra las versiones que REALMENTE hay en el set (las del snapshot mas
            // las que leemos de los .osu ya copiados, que son la verdad del disco).
            var taken = new HashSet<string>(job.TargetSetVersions.Where(v => v.Length > 0), StringComparer.OrdinalIgnoreCase);

            foreach (string existing in Directory.EnumerateFiles(dir, @"*.osu", SearchOption.AllDirectories))
            {
                string? existingVersion = readVersion(existing);
                if (existingVersion != null)
                    taken.Add(existingVersion);
            }

            string baseVersion = emptyToNull(OszPostProcessor.Peek(oszPath)?.version) ?? $"AI {job.Request.Difficulty:0.0} stars";
            string version = baseVersion;
            int suffix = 2;

            while (taken.Contains(version))
                version = $"{baseVersion} ({suffix++})";

            job.Overrides.Version = version;
            job.Overrides.AudioFilename = job.TargetAudioFilename;
            job.Overrides.BackgroundFilename = job.TargetBackgroundFilename;
            OszPostProcessor.Apply(oszPath, job.Overrides);

            // del archive generado solo interesa el .osu: audio y fondo ya son los del set.
            using (var zip = ZipFile.OpenRead(oszPath))
            {
                var entry = zip.Entries.FirstOrDefault(e => e.Name.EndsWith(@".osu", StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidOperationException(@"the generated archive has no .osu inside.");

                string filename = sanitizeFilename($"{job.Overrides.Artist} - {job.Overrides.Title} ({job.Overrides.Creator}) [{version}].osu");
                string destination = Path.Combine(dir, filename);

                // pisar un .osu existente borraria esa diff (y sus scores) del set.
                for (int i = 2; File.Exists(destination); i++)
                    destination = Path.Combine(dir, sanitizeFilename($"{Path.GetFileNameWithoutExtension(filename)} ({i}).osu"));

                entry.ExtractToFile(destination, false);
            }

            // sidecar actualizado con los settings de ESTA generacion (pisa el copiado).
            File.WriteAllText(Path.Combine(dir, MapperatorinatorSidecar.FILENAME), MapperatorinatorSidecar.FromRequest(job.Request, false).Serialize());

            return (dir, version);
        }

        /// <summary>The difficulty name of an .osu on disk, or null if it has none.</summary>
        private static string? readVersion(string osuPath)
        {
            try
            {
                foreach (string line in File.ReadLines(osuPath))
                {
                    string trimmed = line.Trim();

                    if (trimmed.StartsWith(@"Version:", StringComparison.Ordinal))
                        return emptyToNull(trimmed.Substring(8).Trim());

                    if (trimmed == @"[Difficulty]" || trimmed == @"[Events]")
                        break;
                }
            }
            catch
            {
            }

            return null;
        }

        private static string copyAudioToWorkDir(Func<Stream?> open, string extension, string workDirectory)
        {
            string dest = Path.Combine(workDirectory, $"audio{extension}");

            using (var source = open())
            using (var output = File.Create(dest))
            {
                if (source == null)
                    throw new InvalidOperationException(@"Couldn't open the audio file from storage.");

                source.CopyTo(output);
            }

            return dest;
        }

        private static string sanitizeFilename(string name) => string.Join(@"_", name.Split(Path.GetInvalidFileNameChars()));

        private static string? emptyToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;


        /// <summary>
        /// The progress notification for a generation: pinned on screen while the run is
        /// alive, and its completion notification stays pinned until clicked/dismissed.
        /// </summary>
        private partial class GenerationProgressNotification : ProgressNotification
        {
            public override bool KeepVisibleAsToast => Ongoing;

            protected override Notification CreateCompletionNotification() => new PinnedCompletionNotification
            {
                Activated = CompletionClickAction,
                Text = CompletionText,
            };

            private partial class PinnedCompletionNotification : ProgressCompletionNotification
            {
                public override bool KeepVisibleAsToast => true;
            }
        }
    }

    /// <summary>
    /// Everything a background generation needs, snapshotted on the update thread so the
    /// task never touches realm. Built through the manager's CreateJob* helpers.
    /// </summary>
    public class MapperatorinatorJob
    {
        public MapperatorinatorRequest Request = null!;
        public OszPostProcessor.MetadataOverrides Overrides = new OszPostProcessor.MetadataOverrides();
        public Func<Stream?> OpenAudio = null!;
        public string AudioExtension = @".mp3";
        public double AudioLengthSeconds = 180;
        public string DisplayName = @"new map";

        /// <summary>Whether the community feed should hear about this one (custom title + artist + background).</summary>
        public bool AnnounceToFeed;

        // presentes solo cuando la diff generada se suma al set de origen:
        public BeatmapSetInfo? TargetSetSnapshot;
        public List<(string filename, string path)> TargetSetFiles = new List<(string, string)>();
        public List<string> TargetSetVersions = new List<string>();
        public string? TargetAudioFilename;
        public string? TargetBackgroundFilename;

        internal CancellationTokenSource? Cancellation;
        internal ProgressNotification? Notification;
    }
}
