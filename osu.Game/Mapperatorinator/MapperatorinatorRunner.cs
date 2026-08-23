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
    /// <summary>An AMD GPU the amdgpu driver exposes to ROCm on linux.</summary>
    public class AmdGpuInfo
    {
        public string Name { get; init; } = @"AMD GPU";

        /// <summary>As kfd reports it: 120001 = gfx1201 (major 12, minor 0, step 1).</summary>
        public int GfxTarget { get; init; }

        /// <summary>Whether this user can open /dev/kfd. Without that ROCm sees no GPU at all.</summary>
        public bool KfdAccessible { get; init; }

        public string Gfx => $"gfx{GfxTarget / 10000}{GfxTarget / 100 % 100:x}{GfxTarget % 100:x}";

        /// <summary>
        /// The pytorch rocm wheel ships kernels for a fixed list of chips. Anything else
        /// only runs with HSA_OVERRIDE_GFX_VERSION pointing it at the closest chip that is
        /// in the list (what every "rocm on my 6700 XT" guide tells you to do).
        /// </summary>
        public string? HsaOverride
        {
            get
            {
                if (native_wheel_targets.Contains(Gfx))
                    return null;

                int major = GfxTarget / 10000, minor = GfxTarget / 100 % 100;

                return major switch
                {
                    10 => @"10.3.0",
                    11 when minor == 5 => @"11.5.1",
                    11 => @"11.0.0",
                    12 => @"12.0.1",
                    _ => null,
                };
            }
        }

        // lo que compila la rueda rocm7.0 de pytorch (PYTORCH_ROCM_ARCH).
        private static readonly HashSet<string> native_wheel_targets = new HashSet<string>
        {
            @"gfx900", @"gfx906", @"gfx908", @"gfx90a", @"gfx942", @"gfx950",
            @"gfx1030", @"gfx1100", @"gfx1101", @"gfx1102", @"gfx1150", @"gfx1151", @"gfx1200", @"gfx1201",
        };
    }

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

        private const string torch_device_marker = @"torii-torch-device.txt";

        /// <summary>
        /// The device generation actually runs on: what's detected, unless the installed
        /// pytorch was built for something else. A cpu wheel can't see a gpu, and that is
        /// exactly what an install older than the rocm support has on an amd machine.
        /// </summary>
        public string EffectiveDevice(string? detected = null)
        {
            detected ??= DetectDevice();

            if (detected != @"cuda" && detected != @"rocm")
                return detected;

            // nada instalado todavia: el install va a elegir bien.
            if (!InstallLooksValid)
                return detected;

            string? installedFor = InstalledTorchDevice;

            // sin marker = install de antes: rocm no existia, asi que en amd es cpu seguro;
            // en nvidia se instalaba con cuda.
            if (installedFor == null)
                return detected == @"rocm" ? @"cpu" : detected;

            return installedFor == detected ? detected : @"cpu";
        }

        /// <summary>
        /// Which device pytorch was installed for ("cuda", "rocm", "mps", "cpu"). Null for
        /// installs older than the marker, or when nothing is installed.
        /// </summary>
        public string? InstalledTorchDevice
        {
            get
            {
                if (string.IsNullOrEmpty(Config.InstallPath))
                    return null;

                try
                {
                    string marker = Path.Combine(Config.InstallPath, torch_device_marker);
                    return File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
                }
                catch
                {
                    return null;
                }
            }
        }

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
        /// Best-effort device detection. CUDA if an nvidia GPU is visible, ROCm for an AMD
        /// card on linux, MPS on apple silicon, otherwise CPU.
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

            // amd en linux: el driver amdgpu expone la placa en kfd (la puerta de rocm) y
            // torch.cuda.is_available() da true con la rueda rocm, asi que device=auto la
            // agarra solo. la rueda trae el runtime adentro: no hay que instalar rocm.
            if (DetectAmdGpu() != null)
                return @"rocm";

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

            // donde lo dejan brew / los package managers: nada de esto esta en el PATH
            // de una app abierta desde finder.
            foreach (string candidate in knownFfmpegPaths())
            {
                if (File.Exists(candidate))
                    return candidate;
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

        private static AmdGpuInfo? amdGpu;
        private static bool amdGpuProbed;

        /// <summary>
        /// The AMD GPU ROCm would use, or null. Reads the kfd topology the amdgpu driver
        /// publishes in sysfs: no rocm install needed for that. Integrated GPUs (the 2 CU
        /// thing inside a Ryzen, laptop APUs) are skipped: the model would crawl on them.
        /// Cached: hardware doesn't change mid-session, and group changes need a re-login anyway.
        /// </summary>
        public static AmdGpuInfo? DetectAmdGpu()
        {
            if (amdGpuProbed)
                return amdGpu;

            amdGpuProbed = true;

            if (RuntimeInfo.OS != RuntimeInfo.Platform.Linux || !File.Exists(@"/dev/kfd"))
                return null;

            try
            {
                const string nodes = @"/sys/class/kfd/kfd/topology/nodes";
                if (!Directory.Exists(nodes))
                    return null;

                int bestGfx = 0;

                foreach (string node in Directory.EnumerateDirectories(nodes))
                {
                    string props = Path.Combine(node, @"properties");
                    if (!File.Exists(props))
                        continue;

                    int gfx = 0, simds = 0;

                    foreach (string line in File.ReadLines(props))
                    {
                        int space = line.IndexOf(' ');
                        if (space <= 0 || !int.TryParse(line.AsSpan(space + 1).Trim(), out int value))
                            continue;

                        string key = line.Substring(0, space);
                        if (key == @"gfx_target_version") gfx = value;
                        else if (key == @"simd_count") simds = value;
                    }

                    // la cpu tambien es un nodo (gfx 0). 32 simds = 16 CUs: deja afuera las
                    // igpu y entra cualquier placa de verdad (una 6600 tiene 28 CUs).
                    if (gfx >= 90000 && simds >= 32 && gfx > bestGfx)
                        bestGfx = gfx;
                }

                if (bestGfx == 0)
                    return null;

                amdGpu = new AmdGpuInfo { GfxTarget = bestGfx, Name = amdGpuName(bestGfx), KfdAccessible = canOpenKfd() };
                return amdGpu;
            }
            catch
            {
                return null;
            }
        }

        private static string amdGpuName(int gfx)
        {
            string fallback = $"AMD GPU (gfx{gfx / 10000}{gfx / 100 % 100:x}{gfx % 100:x})";

            try
            {
                using var p = Process.Start(new ProcessStartInfo(@"lspci", @"-d 1002: -nn")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });

                if (p == null)
                    return fallback;

                string outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit(4000);

                // "03:00.0 VGA compatible controller [0300]: Advanced Micro Devices, Inc. [AMD/ATI] Navi 48 [Radeon RX 9070/9070 XT] [1002:7550]"
                foreach (string line in outp.Split('\n'))
                {
                    if (!line.Contains(@"VGA") && !line.Contains(@"Display") && !line.Contains(@"3D"))
                        continue;

                    var m = System.Text.RegularExpressions.Regex.Match(line, @"\[(Radeon[^\]]*)\]");
                    if (m.Success)
                        return m.Groups[1].Value;
                }
            }
            catch
            {
                // sin lspci (pciutils) nos quedamos con el gfx.
            }

            return fallback;
        }

        private static bool canOpenKfd()
        {
            try
            {
                using var f = new FileStream(@"/dev/kfd", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch
            {
                // cualquier otro error no habla de permisos; que lo intente.
                return true;
            }
        }

        private static IEnumerable<string> knownFfmpegPaths()
        {
            switch (RuntimeInfo.OS)
            {
                case RuntimeInfo.Platform.macOS:
                    return new[] { @"/opt/homebrew/bin/ffmpeg", @"/usr/local/bin/ffmpeg" };

                case RuntimeInfo.Platform.Linux:
                    return new[] { @"/usr/bin/ffmpeg", @"/usr/local/bin/ffmpeg", @"/snap/bin/ffmpeg" };

                default:
                    return Array.Empty<string>();
            }
        }

        /// <summary>Homebrew, if it's installed (apple silicon or intel layout). Null if not.</summary>
        public static string? FindBrew()
        {
            if (RuntimeInfo.OS != RuntimeInfo.Platform.macOS)
                return null;

            foreach (string candidate in new[] { @"/opt/homebrew/bin/brew", @"/usr/local/bin/brew" })
            {
                if (File.Exists(candidate))
                    return candidate;
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
                : InstallRoot();

            return Path.Combine(root, @"ffmpeg");
        }

        /// <summary>
        /// Downloads a static ffmpeg build next to the tool. Windows only: elsewhere the
        /// package manager is the right answer and we say so instead.
        /// </summary>
        public async Task InstallFfmpegAsync(Action<string> onLogLine, CancellationToken cancellation)
        {
            if (RuntimeInfo.OS == RuntimeInfo.Platform.macOS)
            {
                string brew = FindBrew() ?? throw new InvalidOperationException(@"Homebrew isn't installed. Install it from brew.sh first, then press Install again.");

                onLogLine($"$ {brew} install ffmpeg");
                await runStep(brew, new[] { "install", "ffmpeg" }, Path.GetTempPath(), onLogLine, cancellation, new Dictionary<string, string>
                {
                    [@"HOMEBREW_NO_AUTO_UPDATE"] = @"1",
                    [@"NONINTERACTIVE"] = @"1",
                }).ConfigureAwait(false);

                string? installed = FindFfmpeg() ?? throw new InvalidOperationException(@"brew finished but ffmpeg still isn't there. Run `brew install ffmpeg` in Terminal to see what it says.");
                onLogLine($"ffmpeg ready: {installed}");
                return;
            }

            if (RuntimeInfo.OS != RuntimeInfo.Platform.Windows)
                throw new InvalidOperationException(@"Install ffmpeg with your package manager: sudo apt install ffmpeg (Debian/Ubuntu), sudo dnf install ffmpeg (Fedora), sudo pacman -S ffmpeg (Arch). Then press Check.");

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

        /// <summary>Free space where the install would go, in bytes.</summary>
        public long LargestFreeSpace()
        {
            try
            {
                if (RuntimeInfo.OS == RuntimeInfo.Platform.Windows)
                    return new DriveInfo(largestFreeWindowsDrive()).AvailableFreeSpace;

                return new DriveInfo(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)).AvailableFreeSpace;
            }
            catch
            {
                return 0;
            }
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

            // rocm: placas fuera de la lista de la rueda andan apuntadas al pariente mas
            // cercano. si el usuario ya lo seteo a mano, se respeta.
            if (DetectAmdGpu()?.HsaOverride is string hsa && !psi.EnvironmentVariables.ContainsKey(@"HSA_OVERRIDE_GFX_VERSION"))
                psi.EnvironmentVariables[@"HSA_OVERRIDE_GFX_VERSION"] = hsa;

            var prepend = new List<string>();

            string? ffmpeg = FindFfmpeg();
            if (ffmpeg != null && ffmpeg != @"ffmpeg")
                prepend.Add(Path.GetDirectoryName(ffmpeg) ?? string.Empty);

            // una app abierta desde finder no tiene los dirs de brew en el PATH, y el
            // python de adentro los necesita para encontrar ffmpeg/ffprobe.
            if (RuntimeInfo.OS == RuntimeInfo.Platform.macOS)
            {
                foreach (string dir in new[] { @"/opt/homebrew/bin", @"/usr/local/bin" })
                {
                    if (Directory.Exists(dir))
                        prepend.Add(dir);
                }
            }

            if (prepend.Count > 0)
            {
                string existing = psi.EnvironmentVariables[@"PATH"] ?? Environment.GetEnvironmentVariable(@"PATH") ?? string.Empty;
                psi.EnvironmentVariables[@"PATH"] = string.Join(Path.PathSeparator, prepend.Where(d => d.Length > 0).Distinct()) + Path.PathSeparator + existing;
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

            if (all.Contains(@"HSA_STATUS_ERROR") || all.Contains(@"hipErrorNoDevice") || all.Contains(@"No HIP GPUs are available") || all.Contains(@"rocBLAS error") || all.Contains(@"hipErrorInvalidDevice"))
                return @"The AMD GPU couldn't be used through ROCm. Look at the 'This machine' line in Mapperatorinator's requirements: usually it's your user not being in the render and video groups.";

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
                @"rocm" => Config.SpeedFactorRocm ?? 1.0,
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

                case @"rocm":
                    Config.SpeedFactorRocm = Config.SpeedFactorRocm == null ? factor : (Config.SpeedFactorRocm * 0.6 + factor * 0.4);
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

            string target = InstallRoot();
            Directory.CreateDirectory(target);
            onLogLine($"installing to {target}");

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
            bool venvExisted = Directory.Exists(Path.Combine(checkout, @".venv"));
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
            string installDevice = DetectDevice();
            onLogLine(installDevice switch
            {
                @"cuda" => @"installing pytorch with CUDA (this is the big one, a few GB)...",
                @"rocm" => @"amd gpu found: installing pytorch with ROCm (this is the big one, a few GB; the runtime comes inside the package, nothing to install on your system)...",
                @"mps" => @"installing pytorch (apple silicon runs it through MPS)...",
                _ => @"no nvidia/amd gpu found: installing cpu pytorch (generation will be SLOW)...",
            });
            string deviceMarker = Path.Combine(checkout, torch_device_marker);
            string? previousDevice = File.Exists(deviceMarker) ? File.ReadAllText(deviceMarker).Trim() : null;

            if (venvExisted && previousDevice != installDevice)
            {
                // pip da por cumplido "torch" aunque sea la rueda de otro device: hay que sacarla.
                onLogLine(@"pytorch in there was installed for a different device: replacing it...");
                await runStep(venvPython, new[] { "-m", "pip", "uninstall", "-y", "torch", "torchaudio" }, checkout, onLogLine, cancellation, pipEnv).ConfigureAwait(false);
            }

            var torchArgs = new List<string> { "-m", "pip", "install", "torch", "torchaudio" };

            switch (installDevice)
            {
                case @"cuda":
                    torchArgs.Add("--index-url");
                    torchArgs.Add("https://download.pytorch.org/whl/cu126");
                    break;

                case @"rocm":
                    // 7.0: la primera con kernels para rdna4 (rx 9000) que todavia publica
                    // ruedas de python 3.10 con torch y torchaudio de la misma version.
                    torchArgs.Add("--index-url");
                    torchArgs.Add("https://download.pytorch.org/whl/rocm7.0");
                    break;

                default:
                    if (RuntimeInfo.OS == RuntimeInfo.Platform.Linux)
                    {
                        // la rueda default de linux trae cuda (2 GB de mas); sin gpu va la cpu.
                        torchArgs.Add("--index-url");
                        torchArgs.Add("https://download.pytorch.org/whl/cpu");
                    }

                    break;
            }
            await runStep(venvPython, torchArgs.ToArray(), checkout, onLogLine, cancellation, pipEnv).ConfigureAwait(false);
            await File.WriteAllTextAsync(deviceMarker, installDevice, cancellation).ConfigureAwait(false);

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
            // el PATH de una app abierta desde finder/explorer es minimo: el python.org
            // de mac queda en /Library/Frameworks y el de brew en /opt/homebrew, y
            // ninguno de los dos esta en ese PATH. se prueban las rutas donde cada
            // instalador lo deja, y recien despues el PATH.
            foreach ((string exe, string[] probeArgs) in pythonCandidates())
            {
                if (probePython(exe, probeArgs))
                    return exe;
            }

            return null;
        }

        private static IEnumerable<(string exe, string[] args)> pythonCandidates()
        {
            const string version = @"--version";
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            switch (RuntimeInfo.OS)
            {
                case RuntimeInfo.Platform.Windows:
                    yield return (@"py -3.10", new[] { version });

                    foreach (string candidate in new[]
                             {
                                 Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs", @"Python", @"Python310", @"python.exe"),
                                 @"C:\Python310\python.exe",
                                 Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Python310", @"python.exe"),
                             })
                    {
                        if (File.Exists(candidate))
                            yield return (candidate, new[] { version });
                    }

                    break;

                case RuntimeInfo.Platform.macOS:
                    foreach (string candidate in new[]
                             {
                                 @"/Library/Frameworks/Python.framework/Versions/3.10/bin/python3.10",
                                 @"/opt/homebrew/bin/python3.10",
                                 @"/opt/homebrew/opt/python@3.10/bin/python3.10",
                                 @"/usr/local/bin/python3.10",
                                 @"/usr/local/opt/python@3.10/bin/python3.10",
                             })
                    {
                        if (File.Exists(candidate))
                            yield return (candidate, new[] { version });
                    }

                    foreach (string candidate in pyenvCandidates(home))
                        yield return (candidate, new[] { version });

                    yield return (@"python3.10", new[] { version });
                    break;

                default:
                    foreach (string candidate in new[] { @"/usr/bin/python3.10", @"/usr/local/bin/python3.10" })
                    {
                        if (File.Exists(candidate))
                            yield return (candidate, new[] { version });
                    }

                    foreach (string candidate in pyenvCandidates(home))
                        yield return (candidate, new[] { version });

                    yield return (@"python3.10", new[] { version });
                    break;
            }
        }

        private static IEnumerable<string> pyenvCandidates(string home)
        {
            string versions = Path.Combine(home, @".pyenv", @"versions");

            if (!Directory.Exists(versions))
                yield break;

            foreach (string dir in Directory.EnumerateDirectories(versions, @"3.10.*").OrderByDescending(d => d))
            {
                string python = Path.Combine(dir, @"bin", @"python");
                if (File.Exists(python))
                    yield return python;
            }
        }

        private static bool probePython(string exe, string[] probeArgs)
        {
            try
            {
                string[] parts = splitLauncher(exe);
                var psi = new ProcessStartInfo(parts[0]) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                for (int i = 1; i < parts.Length; i++) psi.ArgumentList.Add(parts[i]);
                foreach (string a in probeArgs) psi.ArgumentList.Add(a);

                using var p = Process.Start(psi);
                if (p == null) return false;

                string outp = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(6000);
                return p.ExitCode == 0 && outp.Contains(@"3.10");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>"py -3.10" is the one executable that carries an argument; real paths may contain spaces.</summary>
        private static string[] splitLauncher(string exe) => exe.StartsWith(@"py ", StringComparison.Ordinal) ? exe.Split(' ', 2) : new[] { exe };

        /// <summary>
        /// Where the tool gets installed. Windows: the fixed drive with the most free
        /// space (pytorch + model need well over 10 GB and C: is often nearly full).
        /// Elsewhere: inside the user's home, because the filesystem root isn't
        /// writable (macOS mounts it read-only) and that's where things belong anyway.
        /// </summary>
        public static string InstallRoot()
        {
            switch (RuntimeInfo.OS)
            {
                case RuntimeInfo.Platform.Windows:
                    return Path.Combine(largestFreeWindowsDrive(), @"Torii-Mapperatorinator");

                case RuntimeInfo.Platform.macOS:
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"Library", @"Application Support", @"Torii-Mapperatorinator");

                default:
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".local", @"share", @"torii-mapperatorinator");
            }
        }

        private static string largestFreeWindowsDrive()
        {
            string best = Path.GetPathRoot(Path.GetTempPath()) ?? @"C:\";
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

            return best;
        }

        private async Task runStep(string exe, string[] stepArgs, string workDir, Action<string> onLogLine, CancellationToken cancellation, Dictionary<string, string>? env = null)
        {
            // "py -3.10" viaja como exe con argumento adentro; las rutas reales pueden tener espacios.
            string[] exeParts = splitLauncher(exe);

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

        [JsonPropertyName(@"speed_factor_rocm")]
        public double? SpeedFactorRocm { get; set; }

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
