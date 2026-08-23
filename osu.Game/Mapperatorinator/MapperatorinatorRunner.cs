// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework;
using osu.Framework.Logging;

namespace osu.Game.Mapperatorinator
{
    /// <summary>
    /// Runs a local Mapperatorinator install (https://github.com/OliBomby/Mapperatorinator)
    /// as an external process and reports progress. The tool itself is python + pytorch,
    /// so we never bundle it; the user points us at their checkout once and we remember it.
    /// </summary>
    public class MapperatorinatorRunner
    {
        public const string CONFIG_FILENAME = @"mapperatorinator.json";

        private readonly string configPath;
        private readonly string dataDirectory;

        public MapperatorinatorRunnerConfig Config { get; private set; } = new MapperatorinatorRunnerConfig();

        /// <summary>Where the full output of the most recent generation gets written.</summary>
        public string LastRunLogPath => Path.Combine(dataDirectory, @"logs", @"mapperatorinator-last-run.log");

        public MapperatorinatorRunner(string dataDirectory)
        {
            this.dataDirectory = dataDirectory;
            configPath = Path.Combine(dataDirectory, CONFIG_FILENAME);
            load();
        }

        private void load()
        {
            try
            {
                if (File.Exists(configPath))
                    Config = JsonSerializer.Deserialize<MapperatorinatorRunnerConfig>(File.ReadAllText(configPath)) ?? new MapperatorinatorRunnerConfig();
            }
            catch (Exception e)
            {
                Logger.Log($"[mapperatorinator] config unreadable, starting fresh: {e.Message}");
                Config = new MapperatorinatorRunnerConfig();
            }
        }

        public void Save()
        {
            try
            {
                File.WriteAllText(configPath, JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception e)
            {
                Logger.Log($"[mapperatorinator] couldn't save config: {e.Message}");
            }
        }

        /// <summary>
        /// Whether the configured install looks usable (folder exists and has inference.py).
        /// </summary>
        public bool InstallLooksValid =>
            !string.IsNullOrEmpty(Config.InstallPath)
            && File.Exists(Path.Combine(Config.InstallPath, @"inference.py"));

        /// <summary>
        /// The python to use: an install-local venv if present, otherwise whatever is on PATH.
        /// </summary>
        public string PythonExecutable
        {
            get
            {
                if (!string.IsNullOrEmpty(Config.PythonPath))
                    return Config.PythonPath;

                if (!string.IsNullOrEmpty(Config.InstallPath))
                {
                    // the README's recommended setup is a venv inside the checkout.
                    foreach (string candidate in new[] { @"venv\Scripts\python.exe", @".venv\Scripts\python.exe", @"venv/bin/python", @".venv/bin/python" })
                    {
                        string full = Path.Combine(Config.InstallPath, candidate);
                        if (File.Exists(full))
                            return full;
                    }
                }

                return @"python";
            }
        }

        /// <summary>
        /// Best-effort device detection. CUDA if an nvidia GPU is visible, otherwise CPU.
        /// The distinction only drives the ETA; inference.py picks its own device with device=auto.
        /// </summary>
        public string DetectDevice()
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(@"nvidia-smi", @"-L")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (p != null)
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(4000);
                    if (p.ExitCode == 0 && outp.Contains(@"GPU"))
                        return @"cuda";
                }
            }
            catch
            {
                // no nvidia-smi -> no cuda.
            }

            // inference.py's device=auto picks mps on apple silicon, same as we do here.
            if (MapperatorinatorReadiness.IsAppleSilicon)
                return @"mps";

            return @"cpu";
        }

        /// <summary>
        /// The ffmpeg to use: the configured one, a copy we installed next to the tool, or
        /// plain "ffmpeg" if it's on PATH. Null if it's nowhere, which is the single most
        /// common reason a run dies with a bare "exit code 1" (pydub decodes through it).
        /// </summary>
        public string? FindFfmpeg()
        {
            if (!string.IsNullOrEmpty(Config.FfmpegPath) && File.Exists(Config.FfmpegPath))
                return Config.FfmpegPath;

            string? bundled = findBundledFfmpeg();

            if (bundled != null)
            {
                Config.FfmpegPath = bundled;
                Save();
                return bundled;
            }

            try
            {
                var psi = new ProcessStartInfo(@"ffmpeg") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                psi.ArgumentList.Add(@"-version");

                using var p = Process.Start(psi);

                if (p != null)
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(6000);
                    if (p.ExitCode == 0)
                        return @"ffmpeg";
                }
            }
            catch
            {
            }

            return null;
        }

        private string? findBundledFfmpeg()
        {
            string exeName = RuntimeInfo.OS == RuntimeInfo.Platform.Windows ? @"ffmpeg.exe" : @"ffmpeg";

            foreach (string dir in new[] { ffmpegHome(), Path.Combine(Config.InstallPath ?? string.Empty, @"ffmpeg") })
            {
                try
                {
                    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                        continue;

                    foreach (string f in Directory.EnumerateFiles(dir, exeName, SearchOption.AllDirectories))
                        return f;
                }
                catch
                {
                }
            }

            return null;
        }

        /// <summary>Where a game-installed ffmpeg lives: next to the tool, never on the system.</summary>
        private string ffmpegHome()
        {
            string root = !string.IsNullOrEmpty(Config.InstallPath)
                ? Path.GetDirectoryName(Config.InstallPath) ?? Config.InstallPath
                : Path.Combine(pickInstallRoot(), @"Torii-Mapperatorinator");

            return Path.Combine(root, @"ffmpeg");
        }

        /// <summary>
        /// Downloads a static ffmpeg build next to the tool. Windows only: elsewhere the
        /// package manager is the right answer and we say so instead.
        /// </summary>
        public async Task InstallFfmpegAsync(Action<string> onLogLine, CancellationToken cancellation)
        {
            if (RuntimeInfo.OS != RuntimeInfo.Platform.Windows)
                throw new InvalidOperationException(@"Automatic ffmpeg install is Windows only. Install it with your package manager (brew install ffmpeg / sudo apt install ffmpeg).");

            string home = ffmpegHome();
            Directory.CreateDirectory(home);

            string zip = Path.Combine(home, @"ffmpeg.zip");
            onLogLine(@"downloading ffmpeg (about 150 MB)...");

            using (var http = new System.Net.Http.HttpClient())
            using (var resp = await http.GetAsync(@"https://github.com/BtbN/FFmpeg-Builds/releases/latest/download/ffmpeg-master-latest-win64-gpl.zip", cancellation).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using var f = File.Create(zip);
                await resp.Content.CopyToAsync(f, cancellation).ConfigureAwait(false);
            }

            onLogLine(@"extracting...");
            System.IO.Compression.ZipFile.ExtractToDirectory(zip, home, true);
            File.Delete(zip);

            string? exe = findBundledFfmpeg() ?? throw new InvalidOperationException(@"The download didn't contain ffmpeg.exe.");

            Config.FfmpegPath = exe;
            Save();
            onLogLine($"ffmpeg ready: {exe}");
        }

        /// <summary>The most free space on any fixed drive, in bytes.</summary>
        public long LargestFreeSpace()
        {
            long best = 0;

            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType == DriveType.Fixed && drive.AvailableFreeSpace > best)
                        best = drive.AvailableFreeSpace;
                }
                catch
                {
                }
            }

            return best;
        }

        /// <summary>
        /// Everything the python processes need in their environment: unbuffered output
        /// (or the log looks dead until the end), readable hydra errors, and our ffmpeg on
        /// PATH so pydub finds it without touching the system PATH.
        /// </summary>
        private void applyProcessEnvironment(ProcessStartInfo psi)
        {
            psi.EnvironmentVariables[@"PYTHONUNBUFFERED"] = @"1";
            psi.EnvironmentVariables[@"HYDRA_FULL_ERROR"] = @"1";

            string? ffmpeg = FindFfmpeg();

            if (ffmpeg != null && ffmpeg != @"ffmpeg")
            {
                string dir = Path.GetDirectoryName(ffmpeg) ?? string.Empty;
                string existing = psi.EnvironmentVariables[@"PATH"] ?? Environment.GetEnvironmentVariable(@"PATH") ?? string.Empty;
                psi.EnvironmentVariables[@"PATH"] = dir + Path.PathSeparator + existing;
            }
        }

        /// <summary>
        /// Turns the tool's output into something a person can act on. Null when nothing
        /// recognisable is in there.
        /// </summary>
        public static string? Diagnose(IEnumerable<string> outputLines)
        {
            string all = string.Join('\n', outputLines);

            if (all.Contains(@"Couldn't find ffmpeg", StringComparison.OrdinalIgnoreCase)
                || all.Contains(@"ffprobe", StringComparison.OrdinalIgnoreCase) && all.Contains(@"not found", StringComparison.OrdinalIgnoreCase)
                || all.Contains(@"ffmpeg", StringComparison.OrdinalIgnoreCase) && (all.Contains(@"WinError 2") || all.Contains(@"No such file or directory")))
                return @"FFmpeg is missing, so the song couldn't be decoded. Open Mapperatorinator from a map's right-click menu: the FFmpeg step in the requirements list sorts it out.";

            if (all.Contains(@"No module named"))
                return @"The python packages are missing or broken. Re-run the Mapperatorinator install from the requirements list.";

            if (all.Contains(@"not compiled with CUDA") || all.Contains(@"Torch not compiled"))
                return @"PyTorch was installed without GPU support. Re-run the Mapperatorinator install from the requirements list.";

            if (all.Contains(@"out of memory", StringComparison.OrdinalIgnoreCase))
                return @"The GPU ran out of memory. Close other programs using it, or try a shorter song.";

            if (all.Contains(@"MemoryError"))
                return @"The machine ran out of RAM while generating.";

            if (all.Contains(@"HTTPError") || all.Contains(@"ConnectionError") || all.Contains(@"Max retries") || all.Contains(@"Name or service not known"))
                return @"The model couldn't be downloaded (network problem). Check your connection and try again.";

            if (all.Contains(@"Unsupported or broken audio", StringComparison.OrdinalIgnoreCase) || all.Contains(@"CouldntDecodeError"))
                return @"The audio file couldn't be decoded. Try a different song, or re-encode this one to mp3.";

            return null;
        }

        private string writeLastRunLog(IEnumerable<string> args, IReadOnlyList<string> output)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LastRunLogPath)!);
                File.WriteAllLines(LastRunLogPath, new[] { $"$ {PythonExecutable} {string.Join(' ', args)}", string.Empty }.Concat(output));
            }
            catch
            {
            }

            return LastRunLogPath;
        }

        /// <summary>
        /// Rough time estimate for generating a map over <paramref name="audioSeconds"/> of audio.
        /// Starts from coarse defaults and self-corrects: after every successful run the
        /// observed seconds-per-audio-second for that device is stored and reused.
        /// </summary>
        public TimeSpan Estimate(double audioSeconds, string device)
        {
            double factor = device switch
            {
                @"cuda" => Config.SpeedFactorCuda ?? 0.6,
                @"mps" => Config.SpeedFactorMps ?? 2.5,
                _ => Config.SpeedFactorCpu ?? 8.0,
            };

            // model load + audio preprocessing is a fixed-ish tax on top.
            return TimeSpan.FromSeconds(45 + audioSeconds * factor);
        }

        public void RecordObservedSpeed(double audioSeconds, TimeSpan elapsed, string device)
        {
            if (audioSeconds <= 0) return;

            double factor = Math.Max(0.05, (elapsed.TotalSeconds - 45) / audioSeconds);

            // smooth over runs so one weird result doesn't whiplash the estimate.
            switch (device)
            {
                case @"cuda":
                    Config.SpeedFactorCuda = Config.SpeedFactorCuda == null ? factor : (Config.SpeedFactorCuda * 0.6 + factor * 0.4);
                    break;

                case @"mps":
                    Config.SpeedFactorMps = Config.SpeedFactorMps == null ? factor : (Config.SpeedFactorMps * 0.6 + factor * 0.4);
                    break;

                default:
                    Config.SpeedFactorCpu = Config.SpeedFactorCpu == null ? factor : (Config.SpeedFactorCpu * 0.6 + factor * 0.4);
                    break;
            }

            Save();
        }

        /// <summary>
        /// Installs Mapperatorinator from scratch without the user touching a terminal:
        /// downloads the repo, creates a python 3.10 venv, installs pytorch (cuda if an
        /// nvidia gpu is present) and the requirements. Mirrors the manual install that
        /// was verified working; every quirk here was hit for real:
        ///  - `slider` comes from git+ in requirements and git can be broken, so it gets
        ///    installed from the tarball and its line dropped from the requirements.
        ///  - torch is NOT in requirements.txt; it goes separately with the right index.
        ///  - everything (checkout, caches) lands on the drive with the most free space,
        ///    because pytorch + model easily exceed 10 GB and C: is often nearly full.
        /// </summary>
        public async Task InstallAsync(Action<string> onLogLine, CancellationToken cancellation)
        {
            string python = FindPython310() ?? throw new InvalidOperationException(
                @"Python 3.10 is required and wasn't found. Install it from python.org (3.10.x) and try again.");

            onLogLine($"python 3.10: {python}");

            string root = pickInstallRoot();
            string target = Path.Combine(root, @"Torii-Mapperatorinator");
            Directory.CreateDirectory(target);
            onLogLine($"installing to {target} (drive with the most free space)");

            // 1. bajar el repo (tarball, sin depender de git)
            string tarball = Path.Combine(target, @"src.tar.gz");
            onLogLine(@"downloading Mapperatorinator...");

            using (var http = new System.Net.Http.HttpClient())
            using (var resp = await http.GetAsync(@"https://github.com/OliBomby/Mapperatorinator/archive/refs/heads/main.tar.gz", cancellation).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using var f = File.Create(tarball);
                await resp.Content.CopyToAsync(f, cancellation).ConfigureAwait(false);
            }

            await runStep(@"tar", new[] { "-xzf", tarball, "-C", target }, target, onLogLine, cancellation).ConfigureAwait(false);
            File.Delete(tarball);

            string checkout = Path.Combine(target, @"Mapperatorinator-main");
            if (!Directory.Exists(checkout))
                throw new InvalidOperationException(@"Download extracted but the expected folder is missing.");

            // 2. venv + pip al dia
            onLogLine(@"creating python environment...");
            await runStep(python, new[] { "-m", "venv", ".venv" }, checkout, onLogLine, cancellation).ConfigureAwait(false);
            string venvPython = Path.Combine(checkout, @".venv", @"Scripts", @"python.exe");
            if (!File.Exists(venvPython))
                venvPython = Path.Combine(checkout, @".venv", @"bin", @"python");

            var pipEnv = new Dictionary<string, string>
            {
                [@"PIP_CACHE_DIR"] = Path.Combine(target, @"pip-cache"),
                [@"TMP"] = Path.Combine(target, @"tmp"),
                [@"TEMP"] = Path.Combine(target, @"tmp"),
            };
            Directory.CreateDirectory(pipEnv[@"TMP"]);

            await runStep(venvPython, new[] { "-m", "pip", "install", "--upgrade", "pip", "--quiet" }, checkout, onLogLine, cancellation, pipEnv).ConfigureAwait(false);

            // 3. pytorch segun gpu (el download gordo, varios GB)
            bool cuda = DetectDevice() == @"cuda";
            onLogLine(cuda
                ? @"installing pytorch with CUDA (this is the big one, a few GB)..."
                : @"no nvidia gpu found: installing cpu pytorch (generation will be SLOW)...");
            var torchArgs = new List<string> { "-m", "pip", "install", "torch", "torchaudio" };
            if (cuda)
            {
                torchArgs.Add("--index-url");
                torchArgs.Add("https://download.pytorch.org/whl/cu126");
            }
            await runStep(venvPython, torchArgs.ToArray(), checkout, onLogLine, cancellation, pipEnv).ConfigureAwait(false);

            // 4. slider desde tarball + requirements sin su linea git
            onLogLine(@"installing dependencies...");
            await runStep(venvPython, new[] { "-m", "pip", "install", "https://github.com/OliBomby/slider/archive/refs/heads/master.tar.gz" }, checkout, onLogLine, cancellation, pipEnv).ConfigureAwait(false);

            string reqs = Path.Combine(checkout, @"requirements.txt");
            string reqsLocal = Path.Combine(checkout, @"requirements.local.txt");
            var kept = new List<string>();
            foreach (string line in await File.ReadAllLinesAsync(reqs, cancellation).ConfigureAwait(false))
            {
                if (!line.StartsWith(@"slider @", StringComparison.Ordinal))
                    kept.Add(line);
            }
            await File.WriteAllLinesAsync(reqsLocal, kept, cancellation).ConfigureAwait(false);
            await runStep(venvPython, new[] { "-m", "pip", "install", "-r", reqsLocal }, checkout, onLogLine, cancellation, pipEnv).ConfigureAwait(false);

            // 5. listo: recordar y validar
            Config.InstallPath = checkout;
            Save();

            if (!InstallLooksValid)
                throw new InvalidOperationException(@"Install finished but inference.py is missing. Something went wrong.");

            onLogLine(@"install complete! the model itself downloads automatically on your first generation.");
        }

        public static string? FindPython310()
        {
            foreach ((string exe, string[] probeArgs) in new[] { (@"py", new[] { @"-3.10", @"--version" }), (@"python3.10", new[] { @"--version" }) })
            {
                try
                {
                    var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                    foreach (string a in probeArgs) psi.ArgumentList.Add(a);
                    using var p = Process.Start(psi);
                    if (p == null) continue;

                    string outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                    p.WaitForExit(6000);
                    if (p.ExitCode == 0 && outp.Contains(@"3.10"))
                        return exe == @"py" ? @"py -3.10" : exe;
                }
                catch
                {
                }
            }

            return null;
        }

        /// <summary>The fixed drive with the most free space; pytorch + model need well over 10 GB.</summary>
        private static string pickInstallRoot()
        {
            string best = Path.GetPathRoot(Path.GetTempPath()) ?? @"C:/";
            long bestFree = 0;

            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType == DriveType.Fixed && drive.AvailableFreeSpace > bestFree)
                    {
                        best = drive.RootDirectory.FullName;
                        bestFree = drive.AvailableFreeSpace;
                    }
                }
                catch
                {
                }
            }

            if (bestFree < 15L * 1024 * 1024 * 1024)
                throw new InvalidOperationException(@"Not enough disk space: the install needs about 15 GB free on some drive.");

            return best;
        }

        private async Task runStep(string exe, string[] stepArgs, string workDir, Action<string> onLogLine, CancellationToken cancellation, Dictionary<string, string>? env = null)
        {
            // "py -3.10" viaja como exe con argumento adentro
            string[] exeParts = exe.Split(' ', 2);

            var psi = new ProcessStartInfo(exeParts[0])
            {
                WorkingDirectory = workDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (exeParts.Length > 1)
                psi.ArgumentList.Add(exeParts[1]);
            foreach (string a in stepArgs)
                psi.ArgumentList.Add(a);

            applyProcessEnvironment(psi);

            if (env != null)
            {
                foreach ((string k, string v) in env)
                    psi.EnvironmentVariables[k] = v;
            }

            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLogLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLogLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (cancellation.Register(() => { try { process.Kill(true); } catch { } }))
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

            cancellation.ThrowIfCancellationRequested();

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"step {Path.GetFileName(exeParts[0])} {string.Join(' ', stepArgs)} failed with exit code {process.ExitCode}.");
        }

        /// <summary>
        /// Runs inference and returns the path of the generated .osz. The caller owns
        /// cleanup of <see cref="MapperatorinatorRequest.WorkDirectory"/> afterwards.
        /// </summary>
        public async Task<string> GenerateAsync(MapperatorinatorRequest request, Action<string> onLogLine, CancellationToken cancellation)
        {
            if (!InstallLooksValid)
                throw new InvalidOperationException(@"Mapperatorinator install path is not set up (needs a folder containing inference.py).");

            if (!File.Exists(request.AudioPath))
                throw new InvalidOperationException($"Audio file not found: {request.AudioPath}");

            string outputDir = Path.Combine(request.WorkDirectory, @"out");
            Directory.CreateDirectory(outputDir);

            var args = buildArguments(request, outputDir);

            var psi = new ProcessStartInfo(PythonExecutable)
            {
                WorkingDirectory = Config.InstallPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            applyProcessEnvironment(psi);

            foreach (string a in args)
                psi.ArgumentList.Add(a);

            onLogLine($"$ {Path.GetFileName(PythonExecutable)} {string.Join(' ', args)}");

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            string? resultPath = null;

            // todo lo que diga la tool queda guardado: cuando falla, el traceback es la
            // unica pista, y "exit code 1" solo no le sirve a nadie.
            var output = new List<string>();

            void handle(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;

                lock (output)
                {
                    output.Add(line);
                    if (output.Count > 800)
                        output.RemoveAt(0);
                }

                onLogLine(line);

                // inference.py logs exactly this on success.
                const string marker_osz = @"Generated .osz saved to ";
                const string marker_osu = @"Generated beatmap saved to ";
                int idx = line.IndexOf(marker_osz, StringComparison.Ordinal);
                if (idx >= 0) resultPath = line.Substring(idx + marker_osz.Length).Trim().Trim('"');
                idx = line.IndexOf(marker_osu, StringComparison.Ordinal);
                if (idx >= 0) resultPath ??= line.Substring(idx + marker_osu.Length).Trim().Trim('"');
            }

            process.OutputDataReceived += (_, e) => handle(e.Data);
            process.ErrorDataReceived += (_, e) => handle(e.Data);

            try
            {
                process.Start();
            }
            catch (Exception e)
            {
                // el caso tipico: no hay python en el PATH y tampoco venv.
                throw new MapperatorinatorRunException($"Couldn't start \"{PythonExecutable}\": {e.Message}",
                    @"Python 3.10 wasn't found. Open Mapperatorinator from a map's right-click menu and follow the Python step in the requirements list.",
                    writeLastRunLog(args, Array.Empty<string>()), Array.Empty<string>());
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using (cancellation.Register(() =>
            {
                try { process.Kill(true); }
                catch { }
            }))
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);

            cancellation.ThrowIfCancellationRequested();

            IReadOnlyList<string> snapshot;
            lock (output)
                snapshot = output.ToArray();

            string logPath = writeLastRunLog(args, snapshot);

            if (process.ExitCode != 0)
                throw new MapperatorinatorRunException($"inference.py exited with code {process.ExitCode}.", Diagnose(snapshot), logPath, tail(snapshot));

            // fallback: some versions only log relative names; scan the output folder.
            if (resultPath == null || !File.Exists(resultPath))
            {
                foreach (string f in Directory.GetFiles(outputDir, @"*.osz", SearchOption.AllDirectories))
                    resultPath = f;
            }

            if (resultPath == null || !File.Exists(resultPath))
                throw new MapperatorinatorRunException(@"Generation finished but no .osz was produced.", Diagnose(snapshot), logPath, tail(snapshot));

            return resultPath;

            static IReadOnlyList<string> tail(IReadOnlyList<string> lines) => lines.Skip(Math.Max(0, lines.Count - 25)).ToArray();
        }

        private List<string> buildArguments(MapperatorinatorRequest request, string outputDir)
        {
            // hydra parsea los overrides; los paths van con barras normales porque los
            // backslashes de windows se comen como escapes en su gramatica.
            string audio = request.AudioPath.Replace('\\', '/');
            string output = outputDir.Replace('\\', '/');

            var args = new List<string>
            {
                @"inference.py",
                @"--config-name", request.Model.ConfigName(),
                $"audio_path=\"{audio}\"",
                $"output_path=\"{output}\"",
                $"gamemode={(int)request.Gamemode}",
                @"export_osz=true",
                @"device=auto",
            };

            if (request.Difficulty != null)
                args.Add($"difficulty={number(request.Difficulty.Value)}");
            if (request.Year != null)
                args.Add($"year={request.Year.Value}");
            if (request.MapperId != null)
                args.Add($"mapper_id={request.MapperId.Value}");
            if (request.Seed != null)
                args.Add($"seed={request.Seed.Value}");
            if (request.Gamemode == MapperatorinatorGamemode.Mania && request.Keycount != null)
                args.Add($"keycount={request.Keycount.Value}");
            if (request.CircleSize != null)
                args.Add($"circle_size={number(request.CircleSize.Value)}");
            if (request.ApproachRate != null)
                args.Add($"approach_rate={number(request.ApproachRate.Value)}");
            if (request.OverallDifficulty != null)
                args.Add($"overall_difficulty={number(request.OverallDifficulty.Value)}");
            if (request.HpDrainRate != null)
                args.Add($"hp_drain_rate={number(request.HpDrainRate.Value)}");

            if (!request.Hitsounded)
                args.Add(@"hitsounded=false");
            if (request.SuperTiming)
                args.Add(@"super_timing=true");

            if (request.Descriptors.Count > 0)
                args.Add($"descriptors=[{string.Join(',', request.Descriptors.ConvertAll(quote))}]");
            if (request.NegativeDescriptors.Count > 0)
                args.Add($"negative_descriptors=[{string.Join(',', request.NegativeDescriptors.ConvertAll(quote))}]");

            if (!string.IsNullOrWhiteSpace(Config.ExtraArguments))
                args.AddRange(Config.ExtraArguments.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            return args;

            // hydra parsea con punto decimal siempre, sin importar la locale de windows.
            static string number(double value) => value.ToString(@"0.0#", CultureInfo.InvariantCulture);

            static string quote(string s) => $"\"{s.Trim().Replace("\"", string.Empty)}\"";
        }
    }

    public class MapperatorinatorRunnerConfig
    {
        [JsonPropertyName(@"install_path")]
        public string? InstallPath { get; set; }

        [JsonPropertyName(@"python_path")]
        public string? PythonPath { get; set; }

        [JsonPropertyName(@"extra_arguments")]
        public string? ExtraArguments { get; set; }

        [JsonPropertyName(@"speed_factor_cuda")]
        public double? SpeedFactorCuda { get; set; }

        [JsonPropertyName(@"speed_factor_cpu")]
        public double? SpeedFactorCpu { get; set; }

        [JsonPropertyName(@"speed_factor_mps")]
        public double? SpeedFactorMps { get; set; }

        /// <summary>Full path of the ffmpeg executable the game installed (or the user pointed at).</summary>
        [JsonPropertyName(@"ffmpeg_path")]
        public string? FfmpegPath { get; set; }
    }

    /// <summary>
    /// A generation that didn't produce a map, with everything needed to tell the user
    /// why: a diagnosis when the output matched something known, the last lines of
    /// output, and where the full log went.
    /// </summary>
    public class MapperatorinatorRunException : Exception
    {
        public string? Diagnosis { get; }
        public string LogPath { get; }
        public IReadOnlyList<string> OutputTail { get; }

        public MapperatorinatorRunException(string message, string? diagnosis, string logPath, IReadOnlyList<string> outputTail)
            : base(message)
        {
            Diagnosis = diagnosis;
            LogPath = logPath;
            OutputTail = outputTail;
        }
    }

    public enum MapperatorinatorModel
    {
        [System.ComponentModel.Description(@"V32 (recommended)")]
        V32,

        [System.ComponentModel.Description(@"V32 mini (faster, lighter)")]
        V32Mini,

        [System.ComponentModel.Description(@"V31")]
        V31,

        [System.ComponentModel.Description(@"V30 (osu! only, basic)")]
        V30,

        [System.ComponentModel.Description(@"V29")]
        V29,

        [System.ComponentModel.Description(@"V28")]
        V28,
    }

    /// <summary>
    /// What each model actually understands. Anything not supported is hidden in the UI:
    /// showing a field the model ignores is worse than not having it.
    ///
    /// Source of truth: configs/train/*.yaml (add_mapper_token / add_year_token /
    /// add_descriptors / min_year / max_year) and static/app.js (modelCapabilities)
    /// in the Mapperatorinator repo. Keep in sync when new models land.
    /// </summary>
    public static class MapperatorinatorModelCapabilities
    {
        public static string ConfigName(this MapperatorinatorModel m) => m switch
        {
            MapperatorinatorModel.V32 => @"v32",
            MapperatorinatorModel.V32Mini => @"v32-mini",
            MapperatorinatorModel.V31 => @"v31",
            MapperatorinatorModel.V30 => @"v30",
            MapperatorinatorModel.V29 => @"v29",
            MapperatorinatorModel.V28 => @"v28",
            _ => @"v32",
        };

        public static bool SupportsYear(this MapperatorinatorModel m) => m != MapperatorinatorModel.V30;

        /// <summary>2024 only for the v32 family; the rest were trained up to 2023.</summary>
        public static int MaxYear(this MapperatorinatorModel m) =>
            m is MapperatorinatorModel.V32 or MapperatorinatorModel.V32Mini ? 2024 : 2023;

        public const int MIN_YEAR = 2007;

        public static bool SupportsMapperId(this MapperatorinatorModel m) =>
            m is MapperatorinatorModel.V32 or MapperatorinatorModel.V32Mini
                or MapperatorinatorModel.V29 or MapperatorinatorModel.V28;

        public static bool SupportsDescriptors(this MapperatorinatorModel m) => m != MapperatorinatorModel.V30;

        public static bool SupportsHitsoundsToggle(this MapperatorinatorModel m) => m != MapperatorinatorModel.V30;

        /// <summary>v30 was only trained on osu! standard.</summary>
        public static bool SupportsGamemode(this MapperatorinatorModel m, MapperatorinatorGamemode mode) =>
            m != MapperatorinatorModel.V30 || mode == MapperatorinatorGamemode.Osu;
    }

    public enum MapperatorinatorGamemode
    {
        [System.ComponentModel.Description(@"osu!")]
        Osu = 0,

        [System.ComponentModel.Description(@"osu!taiko")]
        Taiko = 1,

        [System.ComponentModel.Description(@"osu!catch")]
        Catch = 2,

        [System.ComponentModel.Description(@"osu!mania")]
        Mania = 3,
    }

    /// <summary>
    /// What to generate. Kept deliberately close to inference.py's own vocabulary.
    /// </summary>
    public class MapperatorinatorRequest
    {
        public MapperatorinatorModel Model { get; set; } = MapperatorinatorModel.V32;

        public string AudioPath { get; set; } = string.Empty;

        /// <summary>
        /// Scratch folder owned by this run: the temp audio copy and the tool output both
        /// live here so a single delete cleans everything.
        /// </summary>
        public string WorkDirectory { get; set; } = string.Empty;

        public MapperatorinatorGamemode Gamemode { get; set; }

        public double? Difficulty { get; set; } = 5;

        public int? Year { get; set; }

        /// <summary>osu! user id whose mapping style to imitate.</summary>
        public int? MapperId { get; set; }

        public int? Seed { get; set; }

        /// <summary>Mania only.</summary>
        public int? Keycount { get; set; }

        /// <summary>
        /// Difficulty settings to force on the generated map. Null lets the model pick
        /// what fits the style it is generating.
        /// </summary>
        public double? CircleSize { get; set; }

        public double? ApproachRate { get; set; }

        public double? OverallDifficulty { get; set; }

        public double? HpDrainRate { get; set; }

        public bool Hitsounded { get; set; } = true;

        /// <summary>Slower but much better timing for songs with variable BPM.</summary>
        public bool SuperTiming { get; set; }

        public List<string> Descriptors { get; } = new List<string>();

        public List<string> NegativeDescriptors { get; } = new List<string>();
    }
}
