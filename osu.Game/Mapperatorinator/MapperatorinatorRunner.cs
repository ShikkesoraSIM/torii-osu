// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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

        public MapperatorinatorRunnerConfig Config { get; private set; } = new MapperatorinatorRunnerConfig();

        public MapperatorinatorRunner(string dataDirectory)
        {
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

            return @"cpu";
        }

        /// <summary>
        /// Rough time estimate for generating a map over <paramref name="audioSeconds"/> of audio.
        /// Starts from coarse defaults and self-corrects: after every successful run the
        /// observed seconds-per-audio-second for that device is stored and reused.
        /// </summary>
        public TimeSpan Estimate(double audioSeconds, string device)
        {
            double factor = device == @"cuda"
                ? (Config.SpeedFactorCuda ?? 0.6)
                : (Config.SpeedFactorCpu ?? 8.0);

            // model load + audio preprocessing is a fixed-ish tax on top.
            return TimeSpan.FromSeconds(45 + audioSeconds * factor);
        }

        public void RecordObservedSpeed(double audioSeconds, TimeSpan elapsed, string device)
        {
            if (audioSeconds <= 0) return;

            double factor = Math.Max(0.05, (elapsed.TotalSeconds - 45) / audioSeconds);

            // smooth over runs so one weird result doesn't whiplash the estimate.
            if (device == @"cuda")
                Config.SpeedFactorCuda = Config.SpeedFactorCuda == null ? factor : (Config.SpeedFactorCuda * 0.6 + factor * 0.4);
            else
                Config.SpeedFactorCpu = Config.SpeedFactorCpu == null ? factor : (Config.SpeedFactorCpu * 0.6 + factor * 0.4);

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
            string python = findPython310() ?? throw new InvalidOperationException(
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

        private static string? findPython310()
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

            // sin esto python bufferea stdout y el log en pantalla parece muerto
            // hasta el final. HYDRA_FULL_ERROR hace legibles los errores de config.
            psi.EnvironmentVariables[@"PYTHONUNBUFFERED"] = @"1";
            psi.EnvironmentVariables[@"HYDRA_FULL_ERROR"] = @"1";

            foreach (string a in args)
                psi.ArgumentList.Add(a);

            onLogLine($"$ {Path.GetFileName(PythonExecutable)} {string.Join(' ', args)}");

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            string? resultPath = null;

            void handle(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;

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
                throw new InvalidOperationException($"Couldn't start \"{PythonExecutable}\": {e.Message}. Is python installed?");
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

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"inference.py exited with code {process.ExitCode}. Check the log for details.");

            // fallback: some versions only log relative names; scan the output folder.
            if (resultPath == null || !File.Exists(resultPath))
            {
                foreach (string f in Directory.GetFiles(outputDir, @"*.osz", SearchOption.AllDirectories))
                    resultPath = f;
            }

            if (resultPath == null || !File.Exists(resultPath))
                throw new InvalidOperationException(@"Generation finished but no .osz was produced. Check the log.");

            return resultPath;
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
                args.Add($"difficulty={request.Difficulty.Value.ToString(@"0.0#", CultureInfo.InvariantCulture)}");
            if (request.Year != null)
                args.Add($"year={request.Year.Value}");
            if (request.MapperId != null)
                args.Add($"mapper_id={request.MapperId.Value}");
            if (request.Seed != null)
                args.Add($"seed={request.Seed.Value}");
            if (request.Gamemode == MapperatorinatorGamemode.Mania && request.Keycount != null)
                args.Add($"keycount={request.Keycount.Value}");
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

        public bool Hitsounded { get; set; } = true;

        /// <summary>Slower but much better timing for songs with variable BPM.</summary>
        public bool SuperTiming { get; set; }

        public List<string> Descriptors { get; } = new List<string>();

        public List<string> NegativeDescriptors { get; } = new List<string>();
    }
}
