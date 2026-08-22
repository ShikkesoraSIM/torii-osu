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
