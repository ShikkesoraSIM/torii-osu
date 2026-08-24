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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework;
using osu.Framework.Logging;

namespace osu.Game.Mapperatorinator
{
    /// <summary>Un juego de ruedas de pytorch con ROCm: de donde bajarlas y que pedir.</summary>
    public class RocmWheel
    {
        /// <summary>Lo que se guarda para saber cual quedo instalada.</summary>
        public string Id { get; init; } = string.Empty;

        public string IndexUrl { get; init; } = string.Empty;

        /// <summary>Lo que se le pasa a pip, con la version pineada.</summary>
        public string[] Packages { get; init; } = Array.Empty<string>();

        /// <summary>El indice de AMD no tiene todo lo que torch necesita para instalarse.</summary>
        public bool AlsoPypi { get; init; }
    }

    /// <summary>An AMD GPU the amdgpu driver exposes to ROCm on linux.</summary>
    public class AmdGpuInfo
    {
        public string Name { get; init; } = @"AMD GPU";

        /// <summary>As kfd reports it: 120001 = gfx1201 (major 12, minor 0, step 1).</summary>
        public int GfxTarget { get; init; }

        /// <summary>Whether this user can open /dev/kfd. Without that ROCm sees no GPU at all.</summary>
        public bool KfdAccessible { get; init; }

        /// <summary>La placa esta en windows.</summary>
        public bool Windows { get; init; }

        /// <summary>
        /// Los paquetes de kernels que le tocan en el indice de AMD, o null si no la
        /// reconocimos (AMD solo publica windows para RDNA3 en adelante). Van todos los
        /// chips de la generacion, no el de esta placa: son 48 MB cada uno y asi no hay
        /// forma de errarle al chip por leer mal un numero de modelo.
        /// </summary>
        public string[]? WheelTargets { get; init; }

        /// <summary>
        /// Windows y no tenemos rueda para esta placa: Torii no se la puede instalar, asi
        /// que depende de que la persona tenga un torch que sepa hablarle.
        /// </summary>
        public bool WindowsWithoutRocm => Windows && WheelTargets == null;

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

        /// <summary>Where a report written by <see cref="WriteReport"/> lands.</summary>
        public string ReportPath(string name) => Path.Combine(dataDirectory, @"logs", $"mapperatorinator-{name}.log");

        /// <summary>
        /// Dumps whatever a long job printed to a file, so it can be read after the fact
        /// and sent to someone. The in-game log panel is for watching, not for keeping.
        /// </summary>
        public string? WriteReport(string name, IEnumerable<string> lines)
        {
            try
            {
                string path = ReportPath(name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllLines(path, lines);
                return path;
            }
            catch
            {
                return null;
            }
        }

        public MapperatorinatorRunner(string dataDirectory)
        {
            this.dataDirectory = dataDirectory;
            configPath = Path.Combine(dataDirectory, CONFIG_FILENAME);
            load();

            // la prueba anterior nunca termino: el unico final que no escribe nada es que
            // la placa se llevo el juego puesta. no se vuelve a intentar sola.
            if (Config.RocmTrialPending)
            {
                Config.RocmTrialPending = false;
                MarkRocmBlocked(@"the game went down while testing the card");
            }
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

        private readonly object saveLock = new object();

        public void Save()
        {
            // se llama desde el hilo de la ui y desde las tareas de install a la vez: sin
            // esto dos escrituras se pisan y el json queda cortado por la mitad, que es
            // perder el install path entero.
            lock (saveLock)
            {
                try
                {
                    string json = JsonSerializer.Serialize(Config, new JsonSerializerOptions { WriteIndented = true });

                    // se escribe al lado y se mueve encima: si el juego se muere en el
                    // medio, queda el de antes y no uno a medio escribir.
                    string temp = configPath + @".tmp";
                    File.WriteAllText(temp, json);
                    File.Move(temp, configPath, true);
                }
                catch (Exception e)
                {
                    Logger.Log($"[mapperatorinator] couldn't save config: {e.Message}");
                }
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
        /// Que eligio la persona: dejarnos decidir, forzar la placa, o forzar el
        /// procesador. Existe porque adivinar por sistema operativo y marca de placa deja
        /// afuera a cualquiera con una configuracion rara, y las hay: la misma placa que
        /// no anda en una maquina anda en otra.
        /// </summary>
        public string DevicePreference
        {
            get => Config.DevicePreference ?? @"auto";
            set
            {
                Config.DevicePreference = value == @"auto" ? null : value;
                Save();
            }
        }

        /// <summary>Our device name in inference.py's vocabulary (it only knows cuda/mps/cpu).</summary>
        private static string toolDevice(string device) => device switch
        {
            @"cuda" or @"rocm" => @"cuda",
            @"mps" => @"mps",
            _ => @"cpu",
        };

        /// <summary>The pytorch wheel index for a device, or null when the default one is right.</summary>
        public static string? TorchIndexUrl(string device)
        {
            switch (device)
            {
                case @"cuda":
                    return @"https://download.pytorch.org/whl/cu126";

                case @"rocm":
                    // 7.0: la primera con kernels para rdna4 (rx 9000) que todavia publica
                    // ruedas de python 3.10 con torch y torchaudio de la misma version.
                    return @"https://download.pytorch.org/whl/rocm7.0";

                default:
                    // la rueda default de linux trae cuda (2 GB de mas); sin gpu va la cpu.
                    return RuntimeInfo.OS == RuntimeInfo.Platform.Linux ? @"https://download.pytorch.org/whl/cpu" : null;
            }
        }

        /// <summary>The python inside the tool's venv, when the tool is installed.</summary>
        public string? VenvPython
        {
            get
            {
                if (string.IsNullOrEmpty(Config.InstallPath))
                    return null;

                string windows = Path.Combine(Config.InstallPath, @".venv", @"Scripts", @"python.exe");
                if (File.Exists(windows))
                    return windows;

                string unix = Path.Combine(Config.InstallPath, @".venv", @"bin", @"python");
                return File.Exists(unix) ? unix : null;
            }
        }

        /// <summary>
        /// The one line that installs the right pytorch by hand, for people who would
        /// rather type it than press a button. Null when the tool isn't installed.
        /// </summary>
        public string? ManualTorchCommand(string device)
        {
            string? python = VenvPython;

            if (python == null)
                return null;

            string quoted = python.Contains(' ') ? $"\"{python}\"" : python;
            string? index = TorchIndexUrl(device);

            return $"{quoted} -m pip install --force-reinstall torch torchaudio"
                   + (index != null ? $" --index-url {index}" : string.Empty);
        }

        /// <summary>What torch knows about this machine's GPU. No kernel ever runs to get this.</summary>
        /// <summary>Una de las placas que torch enumera.</summary>
        public class GpuDevice
        {
            public int Index { get; init; }
            public string Name { get; init; } = string.Empty;
            public string Arch { get; init; } = string.Empty;

            /// <summary>Unidades de computo. Una integrada tiene 2, una placa de verdad 60 y pico.</summary>
            public int Cus { get; init; }

            public long MemoryMB { get; init; }

            public override string ToString() =>
                $"#{Index} {Name}" + (Arch.Length > 0 ? $" ({Arch})" : string.Empty) + $", {Cus} CUs, {MemoryMB} MB";
        }

        public class GpuProbe
        {
            public string? TorchVersion { get; set; }
            public string? HipVersion { get; set; }
            public int DeviceCount { get; set; }
            public string? DeviceName { get; set; }

            /// <summary>The card's chip as the driver reports it, e.g. "gfx1201".</summary>
            public string? Arch { get; set; }

            /// <summary>The chips this pytorch build carries kernels for.</summary>
            public List<string> ArchList { get; set; } = new List<string>();

            /// <summary>Todas las placas que torch ve, en el orden en que las enumera.</summary>
            public List<GpuDevice> Devices { get; } = new List<GpuDevice>();

            /// <summary>La que elegimos para generar: la mas grande, no la primera.</summary>
            public int ChosenIndex { get; set; }

            public string? Error { get; set; }

            /// <summary>The card's chip is among the ones the build can run. Without this, dispatching faults the GPU.</summary>
            public bool ArchSupported => Arch != null && ArchList.Contains(Arch, StringComparer.OrdinalIgnoreCase);

            /// <summary>El build instalado no tiene soporte de GPU de ningun tipo.</summary>
            public bool IsCpuOnly => string.IsNullOrEmpty(HipVersion)
                                     && (TorchVersion?.Contains(@"+cpu", StringComparison.OrdinalIgnoreCase) == true || ArchList.Count == 0)
                                     && DeviceCount == 0;

            public string Summary
            {
                get
                {
                    if (Error != null)
                        return $"torch couldn't look at the GPU: {Error}";

                    if (IsCpuOnly)
                        return $"the pytorch installed here is the CPU build ({TorchVersion}): it has no GPU support at all, no matter which card you have.";

                    return $"{DeviceName ?? "unknown card"} ({Arch ?? "unknown chip"}), pytorch {TorchVersion}"
                           + (string.IsNullOrEmpty(HipVersion) ? string.Empty : $" for ROCm {HipVersion}")
                           + $", kernels for: {(ArchList.Count > 0 ? string.Join(", ", ArchList) : "none listed")}";
                }
            }
        }

        /// <summary>
        /// Asks torch what the card is and what the installed build can run. This only
        /// reads properties: no kernel is dispatched, so it can't fault the GPU. It's the
        /// check that was missing: dispatching to a chip the build has no kernels for is
        /// what ends in a hardware exception and a dead display driver.
        /// </summary>
        public async Task<GpuProbe> ProbeGpuAsync(Action<string> onLogLine, CancellationToken cancellation)
        {
            var probe = new GpuProbe();
            // el que la persona eligio si eligio uno: no tiene sentido sondear el venv
            // nuestro cuando la placa anda en el python de ella.
            string venvPython = PythonExecutable;

            if (pythonProblem() is string problem)
            {
                probe.Error = problem;
                return probe;
            }

            const string script = @"import json
info = {}
try:
    import torch
    info['torch'] = torch.__version__
    info['hip'] = getattr(torch.version, 'hip', None)
    try:
        info['arch_list'] = [a.split(':')[0] for a in torch.cuda.get_arch_list()]
    except Exception as e:
        info['arch_list'] = []
        info['arch_list_error'] = str(e)
    info['count'] = torch.cuda.device_count()
    devices = []
    for i in range(info['count']):
        # una placa que no contesta no puede tapar a las demas: sin esto, una sola
        # que falle deja el reporte entero en 'error' y no se ve ninguna.
        try:
            p = torch.cuda.get_device_properties(i)
            devices.append({
                'index': i,
                'name': p.name,
                'arch': getattr(p, 'gcnArchName', '').split(':')[0],
                'cus': getattr(p, 'multi_processor_count', 0),
                'mb': getattr(p, 'total_memory', 0) // (1024 * 1024),
            })
        except Exception as e:
            devices.append({'index': i, 'name': 'unreadable (' + str(e) + ')', 'arch': '', 'cus': 0, 'mb': 0})
    info['devices'] = devices
except Exception as e:
    info['error'] = str(e)
print('torii-gpu-probe ' + json.dumps(info))";

            string[] launcher = splitLauncher(venvPython);
            var psi = new ProcessStartInfo(launcher[0])
            {
                WorkingDirectory = Config.InstallPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // sin el filtro de placa: es justamente el sondeo el que decide cual usar, y
            // con el filtro puesto solo veria la que elegimos la vez pasada.
            applyProcessEnvironment(psi, selectDevice: false);

            for (int i = 1; i < launcher.Length; i++)
                psi.ArgumentList.Add(launcher[i]);

            psi.ArgumentList.Add(@"-c");
            psi.ArgumentList.Add(script);

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            string? json = null;

            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;

                const string marker = @"torii-gpu-probe ";
                if (e.Data.StartsWith(marker, StringComparison.Ordinal))
                    json = e.Data.Substring(marker.Length);
                else
                    onLogLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLogLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); }
                catch { }

                cancellation.ThrowIfCancellationRequested();
                probe.Error = @"torch never answered (the driver is stuck)";
                return probe;
            }

            if (json == null)
            {
                probe.Error = $"the probe didn't report back (exit code {process.ExitCode})";
                return probe;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                probe.TorchVersion = str(root, @"torch");
                probe.HipVersion = str(root, @"hip");
                probe.Error = str(root, @"error");
                probe.DeviceCount = root.TryGetProperty(@"count", out var c) && c.TryGetInt32(out int n) ? n : 0;

                if (root.TryGetProperty(@"devices", out var devices) && devices.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in devices.EnumerateArray())
                    {
                        probe.Devices.Add(new GpuDevice
                        {
                            Index = num(d, @"index"),
                            Name = str(d, @"name") ?? @"unknown card",
                            Arch = str(d, @"arch") ?? string.Empty,
                            Cus = num(d, @"cus"),
                            MemoryMB = num(d, @"mb"),
                        });
                    }
                }

                if (root.TryGetProperty(@"arch_list", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in list.EnumerateArray())
                    {
                        if (a.GetString() is string s && s.Length > 0)
                            probe.ArchList.Add(s);
                    }
                }
            }
            catch (Exception e)
            {
                probe.Error = $"couldn't read the probe output: {e.Message}";
            }

            // torch enumera primero la integrada del procesador y despues la placa de
            // verdad. Mandarle el trabajo a la primera sin mirar es como termina una 9070
            // XT generando en una radeon integrada de 2 CUs: la integrada tambien tiene
            // kernels, acepta el trabajo, y se cae con violacion de acceso.
            var best = probe.Devices.OrderByDescending(d => d.Cus).ThenByDescending(d => d.MemoryMB).FirstOrDefault();

            if (best != null)
            {
                probe.ChosenIndex = best.Index;
                probe.DeviceName = best.Name;
                probe.Arch = best.Arch.Length > 0 ? best.Arch : null;

                if (probe.Devices.Count > 1)
                {
                    onLogLine($"{probe.Devices.Count} gpus here: {string.Join(@"; ", probe.Devices)}");
                    onLogLine($"using {best.Name} (the biggest one), not the one the system lists first.");
                }

                Config.GpuIndex = best.Index;
                Config.GpuSignature = currentGpuSignature();
            }

            onLogLine(probe.Summary);

            Config.RocmArch = probe.Arch;
            Config.RocmArchList = probe.ArchList.Count > 0 ? string.Join(@", ", probe.ArchList) : null;
            Save();

            return probe;

            static string? str(JsonElement root, string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

            static int num(JsonElement root, string name) =>
                root.TryGetProperty(name, out var v) && v.TryGetInt32(out int i) ? i : 0;
        }

        /// <summary>
        /// The HSA_OVERRIDE_GFX_VERSION that points a card at the closest chip the build
        /// does carry kernels for, or null when nothing in the list is close enough.
        /// Same major generation only: pointing RDNA at CDNA kernels is how GPUs fault.
        /// </summary>
        public static string? OverrideFor(string arch, IEnumerable<string> archList)
        {
            (int major, int minor, int step)? target = parseGfx(arch);

            if (target == null)
                return null;

            (int major, int minor, int step)? best = null;

            foreach (string candidate in archList)
            {
                var parsed = parseGfx(candidate);

                if (parsed == null || parsed.Value.major != target.Value.major)
                    continue;

                // el pariente mas cercano por debajo: los kernels de un chip mas viejo de
                // la misma generacion corren, los de uno mas nuevo no existen todavia.
                if (compare(parsed.Value, target.Value) > 0)
                    continue;

                if (best == null || compare(parsed.Value, best.Value) > 0)
                    best = parsed;
            }

            return best == null ? null : $"{best.Value.major}.{best.Value.minor}.{best.Value.step}";

            static int compare((int major, int minor, int step) a, (int major, int minor, int step) b)
                => a.major != b.major ? a.major.CompareTo(b.major) : a.minor != b.minor ? a.minor.CompareTo(b.minor) : a.step.CompareTo(b.step);
        }

        /// <summary>"gfx1201" -> (12, 0, 1). The last digit is hex ("gfx90a" -> (9, 0, 10)).</summary>
        private static (int major, int minor, int step)? parseGfx(string arch)
        {
            string digits = arch.Trim().ToLowerInvariant();

            if (!digits.StartsWith(@"gfx", StringComparison.Ordinal))
                return null;

            digits = digits.Substring(3).Split(':')[0];

            if (digits.Length < 3)
                return null;

            try
            {
                int step = Convert.ToInt32(digits.Substring(digits.Length - 1), 16);
                int minor = Convert.ToInt32(digits.Substring(digits.Length - 2, 1), 16);
                int major = int.Parse(digits.Substring(0, digits.Length - 2), CultureInfo.InvariantCulture);
                return (major, minor, step);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The ROCm wheel indexes worth trying, best guess first. Each entry pins a
        /// torch/torchaudio pair that exists for python 3.10 in that index: the model
        /// needs torchaudio (v32's spectrogram is the torchaudio one), so a torch-only
        /// install would leave the tool unable to read the audio at all.
        /// </summary>
        public static IEnumerable<RocmWheel> RocmIndexes(AmdGpuInfo? card = null)
        {
            if (RuntimeInfo.OS == RuntimeInfo.Platform.Windows)
            {
                // con una nvidia en la maquina, el torch que hay es el de CUDA y sirve:
                // bajarle encima el de AMD por una integrada Radeon le rompe lo que ya
                // andaba. La placa que vale es la nvidia y va por el otro camino.
                if (HasNvidiaGpu)
                    yield break;

                // AMD publica pytorch con ROCm para windows, con ruedas para python 3.10
                // (el que instalamos). Los kernels NO vienen adentro de torch: van en un
                // paquete aparte por familia de chip, y sin ese paquete torch ve la placa
                // pero no puede correr nada encima.
                string[]? targets = (card ?? DetectAmdGpu())?.WheelTargets;

                if (targets != null)
                {
                    const string version = @"2.11.0+rocm7.14.0";

                    var packages = new List<string> { $"torch=={version}", $"torchaudio=={version}" };
                    packages.AddRange(targets.Select(target => $"amd-torch-device-{target}=={version}"));

                    yield return new RocmWheel
                    {
                        Id = $"amd-rocm7.14-{targets[0]}",
                        IndexUrl = @"https://repo.amd.com/rocm/whl-multi-arch/",
                        Packages = packages.ToArray(),
                        AlsoPypi = true,
                    };
                }

                // placa que no reconocimos: no hay nada que bajar y se prueba con el torch
                // que la persona tenga puesto.
                yield break;
            }

            // la etiqueta local (+rocm7.0) va SIEMPRE: sin ella, "torch==2.10.0" lo cumple
            // igual un 2.10.0+cpu ya instalado, pip no baja nada, y el marcador queda
            // diciendo rocm sobre una rueda de cpu.
            yield return new RocmWheel
            {
                Id = @"rocm7.0",
                IndexUrl = @"https://download.pytorch.org/whl/rocm7.0",
                Packages = new[] { @"torch==2.10.0+rocm7.0", @"torchaudio==2.10.0+rocm7.0" },
            };

            // kernels mas nuevos, misma familia: la segunda oportunidad para una placa
            // que la 7.0 reconoce pero no aguanta.
            yield return new RocmWheel
            {
                Id = @"rocm7.2",
                IndexUrl = @"https://download.pytorch.org/whl/rocm7.2",
                Packages = new[] { @"torch==2.11.0+rocm7.2", @"torchaudio==2.11.0+rocm7.2" },
            };

            // placas viejas (rdna2 y anteriores) que las 7.x ya no traen.
            yield return new RocmWheel
            {
                Id = @"rocm6.4",
                IndexUrl = @"https://download.pytorch.org/whl/rocm6.4",
                Packages = new[] { @"torch==2.9.1+rocm6.4", @"torchaudio==2.9.1+rocm6.4" },
            };
        }

        /// <summary>Installs a specific ROCm pytorch set over whatever is in the venv.</summary>
        public async Task InstallRocmTorchAsync(RocmWheel wheel, Action<string> onLogLine, CancellationToken cancellation)
        {
            string checkout = Config.InstallPath ?? throw new InvalidOperationException(@"Mapperatorinator isn't installed yet.");
            string venvPython = VenvPython ?? throw new InvalidOperationException(@"The python environment inside the install is missing.");
            var pipEnv = pipEnvironment(Path.GetDirectoryName(checkout) ?? checkout);

            onLogLine($"installing pytorch for {wheel.Id} (a few GB)...");
            await installTorchFromWheel(venvPython, checkout, pipEnv, wheel, onLogLine, cancellation).ConfigureAwait(false);
        }

        /// <summary>Baja e instala una rueda de ROCm en un venv concreto.</summary>
        private async Task installTorchFromWheel(string venvPython, string checkout, Dictionary<string, string> pipEnv, RocmWheel wheel, Action<string> onLogLine, CancellationToken cancellation)
        {
            // las versiones van pineadas, asi que pip reemplaza lo que haya sin desinstalar
            // primero. Es a proposito: si la bajada se corta a la mitad, el entorno se
            // queda con el torch que ya tenia en vez de quedarse sin ninguno.
            var args = new List<string> { "-m", "pip", "install" };
            args.AddRange(wheel.Packages);
            args.Add("--index-url");
            args.Add(wheel.IndexUrl);

            if (wheel.AlsoPypi)
            {
                args.Add("--extra-index-url");
                args.Add(@"https://pypi.org/simple");
            }

            await runStep(venvPython, args.ToArray(), checkout, onLogLine, cancellation, pipEnv).ConfigureAwait(false);

            await File.WriteAllTextAsync(Path.Combine(checkout, torch_device_marker), @"rocm", cancellation).ConfigureAwait(false);
            Config.RocmIndex = wheel.Id;
            Save();
        }

        /// <summary>
        /// Two seconds of real GPU work (allocate, copy, multiply) before we commit to a
        /// twenty minute run. The failure we're looking for kills the display driver, so
        /// it's much better to trigger it here than half way through a generation.
        /// Returns true when the card did the maths and gave the right answer.
        /// </summary>
        public async Task<bool> SmokeTestGpuAsync(Action<string> onLogLine, CancellationToken cancellation)
        {
            string venvPython = PythonExecutable;

            if (pythonProblem() is string problem)
            {
                onLogLine(problem);
                return false;
            }

            const string script = @"import torch
assert torch.cuda.is_available(), 'torch cannot see the gpu'
d = torch.device('cuda')
a = torch.ones(512, 512, device=d)
b = (a @ a).sum().item()
assert b == 512 * 512 * 512, f'wrong result: {b}'
print('torii-gpu-ok', torch.cuda.get_device_name(0))";

            onLogLine(@"testing the GPU with a small matrix multiply...");

            string[] launcher = splitLauncher(venvPython);
            var psi = new ProcessStartInfo(launcher[0])
            {
                WorkingDirectory = Config.InstallPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            applyProcessEnvironment(psi);

            for (int i = 1; i < launcher.Length; i++)
                psi.ArgumentList.Add(launcher[i]);

            psi.ArgumentList.Add(@"-c");
            psi.ArgumentList.Add(script);

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            bool ok = false;

            process.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;

                if (e.Data.StartsWith(@"torii-gpu-ok", StringComparison.Ordinal))
                    ok = true;

                onLogLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) onLogLine(e.Data); };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // el primer arranque de HIP carga kernels y puede tardar; pasado eso, colgado.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(true); }
                catch { }

                cancellation.ThrowIfCancellationRequested();
                onLogLine(@"the test never finished: the GPU isn't answering.");
                return false;
            }

            if (process.ExitCode != 0 || !ok)
            {
                onLogLine($"the GPU test failed (exit code {process.ExitCode}).");
                return false;
            }

            onLogLine(@"GPU test passed.");
            return true;
        }

        /// <summary>
        /// Swaps just pytorch in an existing install (minutes, not the full reinstall).
        /// This is what the GPU fix button runs.
        /// </summary>
        public async Task ReinstallTorchAsync(Action<string> onLogLine, CancellationToken cancellation)
        {
            if (!InstallLooksValid)
                throw new InvalidOperationException(@"Mapperatorinator isn't installed yet, so there's no pytorch to replace.");

            string checkout = Config.InstallPath!;
            string venvPython = VenvPython ?? throw new InvalidOperationException(@"The python environment inside the install is missing. Use the full install instead.");

            var pipEnv = pipEnvironment(Path.GetDirectoryName(checkout) ?? checkout);
            await installTorch(venvPython, checkout, pipEnv, DetectDevice(), replaceExisting: true, onLogLine, cancellation).ConfigureAwait(false);
        }

        private Dictionary<string, string> pipEnvironment(string target)
        {
            var env = new Dictionary<string, string>
            {
                [@"PIP_CACHE_DIR"] = Path.Combine(target, @"pip-cache"),
                [@"TMP"] = Path.Combine(target, @"tmp"),
                [@"TEMP"] = Path.Combine(target, @"tmp"),
            };

            Directory.CreateDirectory(env[@"TMP"]);
            return env;
        }

        /// <summary>
        /// La version de pytorch que ya haya en el entorno, o null si no hay ninguno.
        /// A proposito NO se toca la placa: solo se leen versiones, asi que en una maquina
        /// donde inicializar el driver revienta, esto igual contesta.
        /// </summary>
        private async Task<string?> installedTorchVersion(string venvPython, string workDir, CancellationToken cancellation)
        {
            try
            {
                string[] launcher = splitLauncher(venvPython);
                var psi = new ProcessStartInfo(launcher[0])
                {
                    WorkingDirectory = workDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                applyProcessEnvironment(psi);

                for (int i = 1; i < launcher.Length; i++)
                    psi.ArgumentList.Add(launcher[i]);

                psi.ArgumentList.Add(@"-c");
                psi.ArgumentList.Add(@"import torch; print('torii-torch', torch.__version__, torch.version.cuda, getattr(torch.version, 'hip', None))");

                using var p = Process.Start(psi);

                if (p == null)
                    return null;

                string output = await p.StandardOutput.ReadToEndAsync(cancellation).ConfigureAwait(false);
                await p.WaitForExitAsync(cancellation).ConfigureAwait(false);

                if (p.ExitCode != 0)
                    return null;

                foreach (string line in output.Split('\n'))
                {
                    if (line.StartsWith(@"torii-torch ", StringComparison.Ordinal))
                        return line.Substring(@"torii-torch ".Length).Trim();
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task installTorch(string venvPython, string checkout, Dictionary<string, string> pipEnv, string installDevice, bool replaceExisting, Action<string> onLogLine, CancellationToken cancellation)
        {
            onLogLine(installDevice switch
            {
                @"cuda" => @"installing pytorch with CUDA (this is the big one, a few GB)...",
                @"rocm" => @"amd gpu found: installing pytorch with ROCm (this is the big one, a few GB; the runtime comes inside the package, nothing to install on your system)...",
                @"mps" => @"installing pytorch (apple silicon runs it through MPS)...",
                _ => @"no nvidia/amd gpu found: installing cpu pytorch (generation will be SLOW)...",
            });

            var rocmWheel = installDevice == @"rocm" ? RocmIndexes().FirstOrDefault() : null;

            if (installDevice == @"rocm" && rocmWheel == null && RuntimeInfo.OS == RuntimeInfo.Platform.Windows)
            {
                // windows con una placa que no reconocemos: no hay rueda que bajar. Va la
                // de cpu y se dice, que mentirle al marker es peor: despues nadie entiende
                // por que genera lento.
                onLogLine(@"no rocm wheel exists for this card on windows: installing the cpu build instead.");
                installDevice = @"cpu";
            }

            if (rocmWheel != null)
            {
                await installTorchFromWheel(venvPython, checkout, pipEnv, rocmWheel, onLogLine, cancellation).ConfigureAwait(false);
                onLogLine(@"pytorch ready for rocm.");
                return;
            }

            string deviceMarker = Path.Combine(checkout, torch_device_marker);
            string? previousDevice = File.Exists(deviceMarker) ? File.ReadAllText(deviceMarker).Trim() : null;

            if (replaceExisting && previousDevice != installDevice)
            {
                // pip da por cumplido "torch" aunque sea la rueda de otro device: hay que sacarla.
                onLogLine(@"pytorch in there was installed for a different device: replacing it...");
                await runStep(venvPython, new[] { "-m", "pip", "uninstall", "-y", "torch", "torchaudio" }, checkout, onLogLine, cancellation, pipEnv).ConfigureAwait(false);
            }

            var torchArgs = new List<string> { "-m", "pip", "install", "torch", "torchaudio" };
            string? index = TorchIndexUrl(installDevice);

            if (index != null)
            {
                torchArgs.Add("--index-url");
                torchArgs.Add(index);
            }

            await runStep(venvPython, torchArgs.ToArray(), checkout, onLogLine, cancellation, pipEnv).ConfigureAwait(false);
            await File.WriteAllTextAsync(deviceMarker, installDevice, cancellation).ConfigureAwait(false);
            onLogLine($"pytorch ready for {installDevice}.");
        }

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

            // torch puesto por la persona: no sabemos para que esta compilado y no nos
            // corresponde suponer. Se le cree, y si no anda lo dice la prueba.
            if (installedFor == @"custom")
                return detected;

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
        /// El motivo por el que el python configurado no sirve, o null si esta todo bien.
        /// PythonExecutable siempre devuelve algo (termina en "python" a secas), asi que
        /// chequear null no servia de nada: el caso real es el campo de "usar mi propio
        /// python" apuntando a algo que se movio o se borro.
        /// </summary>
        private string? pythonProblem()
        {
            if (!string.IsNullOrEmpty(Config.PythonPath) && !File.Exists(Config.PythonPath))
                return $"the python you pointed at isn't there any more ({Config.PythonPath}). Clear the \"use my own python\" field to go back to the one Torii installed.";

            return null;
        }

        /// <summary>
        /// Best-effort device detection. CUDA if an nvidia GPU is visible, ROCm for an AMD
        /// card on linux, MPS on apple silicon, otherwise CPU.
        /// The distinction only drives the ETA; inference.py picks its own device with device=auto.
        /// </summary>
        public string DetectDevice()
        {
            // lo que la persona pidio manda sobre cualquier deteccion nuestra.
            if (DevicePreference == @"cpu")
                return @"cpu";

            if (DevicePreference == @"gpu")
            {
                if (hasNvidia())
                    return @"cuda";

                // la placa fallo antes: pedir gpu no alcanza para volver a mandarla al
                // muere. Hay que volver a pasar la prueba, que es lo que la desbloquea.
                if (DetectAmdGpu() != null)
                    return Config.RocmBlocked ? @"cpu" : @"rocm";

                if (MapperatorinatorReadiness.IsAppleSilicon)
                    return @"mps";

                // no reconocemos la placa y la pidieron igual: que decida torch, que es el
                // unico que sabe si el build que hay puede con lo que hay.
                return @"cuda";
            }

            try
            {
                if (hasNvidia())
                    return @"cuda";
            }
            catch
            {
                // no nvidia-smi -> no cuda.
            }

            // amd en linux: el driver amdgpu expone la placa en kfd (la puerta de rocm).
            // NO es automatico: en placas nuevas o distros sin el stack de rocm, la placa
            // rechaza el trabajo y se lleva puesto el driver de video (pantallas en negro,
            // todo cerrado), asi que va solo si lo pidieron y no fallo antes.
            if (RocmAvailable)
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

        /// <summary>
        /// Si la placa que se eligio la vez pasada sigue siendo la misma. El indice sale
        /// del orden en que torch enumera, asi que no significa nada por si solo: si
        /// cambio el hardware (una placa mas, una menos) o el json se copio a otra
        /// maquina, ese numero apunta a cualquier cosa. Se guarda junto con la lista de
        /// placas que habia cuando se eligio, y si no coincide no se fija ninguna: torch
        /// elige, que es exactamente lo que pasaba antes de todo esto.
        /// </summary>
        public bool GpuChoiceStillValid => Config.GpuSignature != null && Config.GpuSignature == currentGpuSignature();

        private static string currentGpuSignature()
        {
            var amd = DetectAmdGpu();
            return $"{RuntimeInfo.OS}/nv:{HasNvidiaGpu}/amd:{amd?.Name ?? "none"}";
        }

        /// <summary>Si hay una placa nvidia en la maquina.</summary>
        public static bool HasNvidiaGpu => hasNvidia();

        private static bool? nvidiaPresent;

        /// <summary>
        /// Si hay una placa nvidia. Cacheado igual que la deteccion de AMD: esto arranca
        /// nvidia-smi y lo espera, y se llama desde el hilo de la interfaz cada vez que se
        /// revisan los requisitos. El hardware no cambia a mitad de sesion.
        /// </summary>
        private static bool hasNvidia()
        {
            if (nvidiaPresent is bool cached)
                return cached;

            nvidiaPresent = detectNvidia();
            return nvidiaPresent.Value;
        }

        private static bool detectNvidia()
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

                if (p == null)
                    return false;

                string outp = p.StandardOutput.ReadToEnd();
                p.WaitForExit(4000);
                return p.ExitCode == 0 && outp.Contains(@"GPU");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>An AMD GPU is here, the user opted in, and it hasn't faulted on us.</summary>
        // en windows no hay build oficial de pytorch con ROCm, pero hay gente que tiene
        // la placa andando igual (el HIP SDK de AMD, o un torch propio: cualquiera de
        // esos se presenta como CUDA). Quien lo tenga puede pedirlo, y la prueba de dos
        // segundos decide si anda de verdad, que es mejor juez que el sistema operativo.
        public bool RocmAvailable => Config.RocmEnabled && !Config.RocmBlocked && DetectAmdGpu() != null;

        /// <summary>
        /// The card faulted: never again on its own. Called when the tool dies with a
        /// hardware exception, which is the failure that also kills the display driver.
        /// </summary>
        public void MarkRocmBlocked(string? reason = null)
        {
            Config.RocmEnabled = false;
            Config.RocmBlocked = true;

            if (reason != null)
                Config.RocmLastError = reason;

            Save();
        }

        /// <summary>Turn AMD generation on and let the smoke test have the final word.</summary>
        public void EnableRocm()
        {
            Config.RocmEnabled = true;
            Config.RocmBlocked = false;
            Config.RocmLastError = null;
            Save();
        }

        /// <summary>Back to the CPU because the user said so: no failure, no block.</summary>
        public void DisableRocm()
        {
            Config.RocmEnabled = false;
            Config.RocmBlocked = false;
            Config.RocmLastError = null;
            Config.RocmTrialPending = false;
            Save();
        }

        /// <summary>Written to disk before the card is touched, cleared once it answers.</summary>
        public void BeginRocmTrial()
        {
            Config.RocmTrialPending = true;
            Save();
        }

        public void EndRocmTrial()
        {
            Config.RocmTrialPending = false;
            Save();
        }

        /// <summary>
        /// El torch que ya estaba instalado paso la prueba de la placa: se marca como
        /// propio para que EffectiveDevice deje de mandarlo a la cpu por no reconocerlo.
        /// Sin esto el cartel dice que la gpu esta prendida y la generacion igual corre
        /// en cpu, que es la peor de las dos mentiras posibles.
        /// </summary>
        public void MarkTorchVerified()
        {
            if (string.IsNullOrEmpty(Config.InstallPath))
                return;

            try
            {
                File.WriteAllText(Path.Combine(Config.InstallPath, torch_device_marker), @"custom");
            }
            catch (Exception e)
            {
                Logger.Log($"[mapperatorinator] couldn't write the device marker: {e.Message}");
            }
        }

        /// <summary>Point the runtime at a different chip's kernels (or stop doing that).</summary>
        public void SetRocmOverride(string? hsaVersion)
        {
            Config.RocmOverride = hsaVersion;
            Save();
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
            // el candado y el flag al final van juntos: si el flag se prende antes de
            // tener el valor, cualquier otro hilo que pregunte mientras corre el
            // powershell se lleva un null como si no hubiera placa, y esa respuesta
            // queda pegada en lo que sea que haya decidido con ese null.
            lock (amd_probe_lock)
            {
                if (amdGpuProbed)
                    return amdGpu;

                amdGpu = detectAmdGpuUncached();
                amdGpuProbed = true;
                return amdGpu;
            }
        }

        private static readonly object amd_probe_lock = new object();

        private static AmdGpuInfo? detectAmdGpuUncached()
        {

            // en windows la placa existe igual, pero pytorch no publica build de ROCm
            // para windows: no hay forma de generar en ella. Se detecta lo mismo para
            // poder decirlo con nombre y apellido en vez de "no se encontro ninguna gpu".
            if (RuntimeInfo.OS == RuntimeInfo.Platform.Windows)
                return detectWindowsAmdGpu();

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

                return new AmdGpuInfo { GfxTarget = bestGfx, Name = amdGpuName(bestGfx), KfdAccessible = canOpenKfd() };
                return amdGpu;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// La placa AMD de una maquina windows, si hay. No hay kfd ni gfx que leer: lo
        /// que se busca es la libreria que instala el driver de AMD, y el nombre sale de
        /// la lista de controladoras de video.
        /// </summary>
        private static AmdGpuInfo? detectWindowsAmdGpu()
        {
            try
            {
                string system32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System));

                bool amdDriver = new[] { @"atiadlxx.dll", @"amdhip64.dll", @"amdhip64_6.dll", @"aticfx64.dll" }
                    .Any(dll => File.Exists(Path.Combine(system32, dll)));

                if (!amdDriver)
                    return null;

                var names = windowsGpuNames();

                // los kernels se juntan de TODAS las placas AMD que reconocemos, no de una
                // sola: con una integrada reconocida (un 760M) mas una placa aparte,
                // quedarse con "la primera que reconozco" bajaba los kernels de una y el
                // trabajo lo termina haciendo la otra, la de mas unidades de computo, que
                // se queda sin kernels y encima queda marcada como fallada.
                var targets = new List<string>();

                foreach (string candidate in names)
                {
                    foreach (string target in windowsWheelTargets(candidate) ?? Array.Empty<string>())
                    {
                        if (!targets.Contains(target))
                            targets.Add(target);
                    }
                }

                return new AmdGpuInfo
                {
                    Name = windowsBestName(names) ?? @"Your AMD GPU",
                    GfxTarget = 0,
                    KfdAccessible = false,
                    Windows = true,
                    WheelTargets = targets.Count > 0 ? targets.ToArray() : null,
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Los paquetes de kernels que AMD publica para la generacion de esta placa. Se
        /// baja la generacion entera y no el chip exacto a proposito: son 48 MB por chip,
        /// y la alternativa es adivinar que una "RX 9070" es gfx1201 y una "RX 9060" es
        /// gfx1200 leyendo el nombre que reporta el driver, que es justo la clase de
        /// suposicion que despues deja a alguien sin kernels. Solo RDNA3 en adelante, que
        /// es lo que AMD soporta en windows: mandar una placa vieja a bajar los GB de
        /// ROCm para que despues no arranque es peor que decirle de entrada que no hay.
        /// </summary>
        private static string[]? windowsWheelTargets(string? name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            // "AMD Radeon RX 9070 XT" -> 9070.
            var discrete = Regex.Match(name, RX_MODEL, RegexOptions.IgnoreCase);

            if (discrete.Success && int.TryParse(discrete.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int model))
            {
                return model switch
                {
                    >= 9000 and < 10000 => rdna4,
                    >= 7000 and < 8000 => rdna3,
                    _ => null, // rdna2 y anteriores no tienen windows
                };
            }

            // las PRO llevan el mismo chip que las RX de su generacion: W7900 es gfx1100.
            if (Regex.IsMatch(name, PRO_MODEL, RegexOptions.IgnoreCase))
                return rdna3;

            // integradas: 880M/890M (strix) y 780M/760M (phoenix).
            var igpu = Regex.Match(name, IGPU_MODEL, RegexOptions.IgnoreCase);

            if (igpu.Success)
            {
                return igpu.Groups[1].Value switch
                {
                    @"8" or @"9" => strix,
                    @"7" => phoenix,
                    _ => null,
                };
            }

            // strix halo se llama distinto (8060S, 8050S): termina en S y lleva cuatro
            // digitos, asi que no entra por el molde de las otras integradas.
            var halo = Regex.Match(name, HALO_MODEL, RegexOptions.IgnoreCase);

            if (halo.Success && halo.Groups[1].Value == @"8")
                return strix;

            return null;
        }

        // el primero de cada lista es el paquete de la familia entera, que es el que
        // pesa; los otros son los kernels afinados de cada chip, como los pide AMD.
        private static readonly string[] rdna4 = { @"gfx12-0", @"gfx1200", @"gfx1201" };
        private static readonly string[] rdna3 = { @"gfx110x", @"gfx1100", @"gfx1101", @"gfx1102" };
        private static readonly string[] strix = { @"gfx115x", @"gfx1150", @"gfx1151", @"gfx1152", @"gfx1153" };
        private static readonly string[] phoenix = { @"gfx1103" };

        private const string RX_MODEL = @"RX\s*(\d{4})";
        private const string PRO_MODEL = @"\bW7\d{3}\b";
        private const string HALO_MODEL = @"\b(\d)\d{2}0S\b";
        private const string IGPU_MODEL = @"\b(\d)\d0M\b";

        /// <summary>
        /// La placa AMD que conviene nombrar. Windows enumera primero la integrada del
        /// procesador, que ademas se llama "AMD Radeon(TM) Graphics" a secas: quedarse con
        /// la primera es como una maquina con una 9070 XT adentro termina mostrando el
        /// nombre de la integrada en todos los carteles. Gana la placa aparte, que es la
        /// que efectivamente va a hacer el trabajo.
        /// </summary>
        private static string? windowsBestName(List<string> names) =>
            names.FirstOrDefault(isDiscrete)
            ?? names.FirstOrDefault(n => windowsWheelTargets(n) != null)
            ?? names.FirstOrDefault();

        /// <summary>Una placa aparte (RX o PRO), no la integrada del procesador.</summary>
        private static bool isDiscrete(string name) =>
            Regex.IsMatch(name, RX_MODEL, RegexOptions.IgnoreCase) || Regex.IsMatch(name, PRO_MODEL, RegexOptions.IgnoreCase);

        private static List<string> windowsGpuNames()
        {
            var names = new List<string>();

            try
            {
                var psi = new ProcessStartInfo(@"powershell")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                psi.ArgumentList.Add(@"-NoProfile");
                psi.ArgumentList.Add(@"-Command");
                psi.ArgumentList.Add(@"Get-CimInstance Win32_VideoController | Where-Object { $_.Name -match 'AMD|Radeon' } | ForEach-Object { $_.Name }");

                using var p = Process.Start(psi);

                if (p == null)
                    return names;

                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(8000);

                foreach (string line in output.Split('\n'))
                {
                    string name = line.Trim();

                    if (name.Length > 0)
                        names.Add(name);
                }
            }
            catch
            {
                // sin powershell no hay nombre, y no es motivo para romper nada.
            }

            return names;
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
                // el disco donde va a ir de verdad, que no siempre es el mas grande:
                // en el disco del sistema el install se va al perfil del usuario.
                if (RuntimeInfo.OS == RuntimeInfo.Platform.Windows)
                    return new DriveInfo(Path.GetPathRoot(InstallRoot()) ?? largestFreeWindowsDrive()).AvailableFreeSpace;

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
        private void applyProcessEnvironment(ProcessStartInfo psi, bool selectDevice = true)
        {
            psi.EnvironmentVariables[@"PYTHONUNBUFFERED"] = @"1";
            psi.EnvironmentVariables[@"HYDRA_FULL_ERROR"] = @"1";

            // con mas de una placa, torch usa la primera, y en cualquier maquina con un
            // procesador con video integrado la primera es la integrada. Se le dice cual
            // queremos; adentro del proceso esa pasa a ser la 0, asi que inference.py
            // sigue pidiendo "cuda" y no se entera de nada.
            if (selectDevice && Config.GpuIndex is int gpu && GpuChoiceStillValid)
            {
                string index = gpu.ToString(CultureInfo.InvariantCulture);

                // ROCR filtra ANTES que HIP: si el sistema traia uno puesto, nuestro
                // indice queda contado sobre la lista recortada y termina apuntando a
                // otra placa. Se saca, y el que elige es HIP.
                psi.EnvironmentVariables.Remove(@"ROCR_VISIBLE_DEVICES");
                psi.EnvironmentVariables[@"HIP_VISIBLE_DEVICES"] = index;
                psi.EnvironmentVariables[@"CUDA_VISIBLE_DEVICES"] = index;
            }
            else if (!selectDevice)
            {
                // el sondeo tiene que ver TODAS. Si el sistema ya venia con un filtro
                // puesto (es lo que se recomienda por ahi para esconder la integrada), la
                // lista llega recortada y el indice que guardamos queda contado sobre otra
                // lista que la de la generacion: se termina eligiendo justo la que no era.
                psi.EnvironmentVariables.Remove(@"HIP_VISIBLE_DEVICES");
                psi.EnvironmentVariables.Remove(@"CUDA_VISIBLE_DEVICES");
                psi.EnvironmentVariables.Remove(@"ROCR_VISIBLE_DEVICES");
            }

            // rocm: una placa fuera de los chips que trae la rueda solo corre apuntada al
            // pariente mas cercano. el valor sale de lo que dijo torch en el sondeo; la
            // lista escrita a mano es el respaldo para cuando todavia no sondeamos.
            var amd = DetectAmdGpu();
            string? hsa = Config.RocmOverride ?? amd?.HsaOverride;

            if (hsa != null)
            {
                psi.EnvironmentVariables[@"HSA_OVERRIDE_GFX_VERSION"] = hsa;
            }
            else if (amd != null && psi.EnvironmentVariables.ContainsKey(@"HSA_OVERRIDE_GFX_VERSION"))
            {
                // el sistema ya traia un override puesto (es lo que se recomienda por ahi
                // para placas viejas, y algunas distros lo dejan seteado). Si la placa que
                // hay ES una de las que la rueda soporta, ese override la esta haciendo
                // pasar por otra: se despachan kernels de otro chip y la placa los rechaza
                // con una excepcion de hardware. Para esta corrida, se saca.
                psi.EnvironmentVariables.Remove(@"HSA_OVERRIDE_GFX_VERSION");
            }

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
        /// <summary>
        /// The card itself faulted (not a missing library, not permissions): the queue
        /// aborted mid-dispatch. On linux this is what takes the display driver with it.
        /// </summary>
        public static bool IsGpuFault(string output) =>
            output.Contains(@"HSA_STATUS_ERROR_EXCEPTION", StringComparison.Ordinal)
            || output.Contains(@"HSA_STATUS_ERROR_MEMORY_APERTURE_VIOLATION", StringComparison.Ordinal)
            || output.Contains(@"aborting with error", StringComparison.Ordinal)
            || output.Contains(@"HSA_STATUS_ERROR_HW_EXCEPTION", StringComparison.Ordinal)
            || output.Contains(@"GPU core dump", StringComparison.OrdinalIgnoreCase)
            || output.Contains(@"hipErrorIllegalAddress", StringComparison.Ordinal);

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

            if (IsGpuFault(all))
            {
                return @"Your AMD GPU rejected the work and the run was aborted (this is the failure that can also take the display driver down). GPU generation has been turned off: from now on it runs on the CPU, which is slower but always works.";
            }

            if (all.Contains(@"hipErrorNoDevice") || all.Contains(@"No HIP GPUs are available") || all.Contains(@"hipErrorInvalidDevice"))
                return @"ROCm couldn't see your AMD GPU at all. Usually that's your user not being in the render and video groups: sudo usermod -aG render,video $USER, then log out and back in.";

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
            target = writableInstallRoot(target, onLogLine);
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

            var pipEnv = pipEnvironment(target);

            await runStep(venvPython, new[] { "-m", "pip", "install", "--upgrade", "pip", "--quiet" }, checkout, onLogLine, cancellation, pipEnv).ConfigureAwait(false);

            // 3. pytorch. Si ya hay uno instalado, NO se toca: puede ser uno que la persona
            //    puso a mano para que su placa ande (un build de cuda con una capa de
            //    compatibilidad, uno armado para su hardware). Pisarselo con el nuestro
            //    seria deshacerle el trabajo. El boton de "usar la gpu" si lo reemplaza,
            //    porque ahi lo esta pidiendo.
            string? existingTorch = await installedTorchVersion(venvPython, checkout, cancellation).ConfigureAwait(false);

            if (existingTorch != null)
            {
                onLogLine($"pytorch {existingTorch} ya esta instalado en el entorno: se deja como esta.");
                await File.WriteAllTextAsync(Path.Combine(checkout, torch_device_marker), @"custom", cancellation).ConfigureAwait(false);
            }
            else
            {
                await installTorch(venvPython, checkout, pipEnv, DetectDevice(), venvExisted, onLogLine, cancellation).ConfigureAwait(false);
            }

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
        private static string[] splitLauncher(string exe)
        {
            // "py -3.10" viaja como exe con argumento adentro.
            if (exe.StartsWith(@"py ", StringComparison.Ordinal))
                return exe.Split(' ', 2);

            // un .bat o .cmd no lo puede lanzar el sistema por si solo: va por cmd. Sirve
            // para envolver setups raros (zluda, entrar a un contenedor) en un script y
            // apuntar "usar mi propio python" ahi.
            if (exe.EndsWith(@".bat", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(@".cmd", StringComparison.OrdinalIgnoreCase))
                return new[] { @"cmd.exe", @"/c", exe };

            return new[] { exe };
        }

        /// <summary>
        /// Where the tool gets installed. Windows: the fixed drive with the most free
        /// space (pytorch + model need well over 10 GB and C: is often nearly full), pero
        /// NUNCA la raiz del disco del sistema: ahi windows deja crear la carpeta y
        /// despues niega escribir adentro si no sos administrador, y eso es exactamente
        /// por que a uno le anda (le toco un disco de datos) y al de al lado le pide
        /// permisos de administrador para crear el venv. En un disco de datos la raiz no
        /// tiene ese problema. Elsewhere: inside the user's home, because the filesystem
        /// root isn't writable (macOS mounts it read-only) and that's where things belong.
        /// </summary>
        public static string InstallRoot()
        {
            switch (RuntimeInfo.OS)
            {
                case RuntimeInfo.Platform.Windows:
                {
                    string drive = largestFreeWindowsDrive();

                    return isSystemDrive(drive)
                        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Torii-Mapperatorinator")
                        : Path.Combine(drive, @"Torii-Mapperatorinator");
                }

                case RuntimeInfo.Platform.macOS:
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"Library", @"Application Support", @"Torii-Mapperatorinator");

                default:
                    return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".local", @"share", @"torii-mapperatorinator");
            }
        }

        /// <summary>El disco donde vive windows.</summary>
        private static bool isSystemDrive(string drive)
        {
            try
            {
                string? system = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                return system != null && string.Equals(Path.GetFullPath(system), Path.GetFullPath(drive), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // sin poder saberlo, se asume que si: el perfil del usuario siempre anda.
                return true;
            }
        }

        /// <summary>
        /// Probar a escribir de verdad, no solo que la carpeta exista. Windows deja crear
        /// la carpeta y despues niega crear cosas adentro, asi que se hace lo mismo que
        /// hace el venv: una carpeta adentro y un archivo adentro de esa.
        /// </summary>
        private static bool canWriteInside(string root)
        {
            string probe = Path.Combine(root, @".torii-write-test");

            try
            {
                Directory.CreateDirectory(probe);
                File.WriteAllText(Path.Combine(probe, @"probe"), @"ok");
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(probe))
                        Directory.Delete(probe, true);
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Un lugar donde de verdad se pueda escribir, o el perfil del usuario, que
        /// siempre es suyo. Sin esto el install se cae a la mitad, despues de bajar el
        /// repo, con un "acceso denegado" que solo se arregla abriendo el juego como
        /// administrador, que no es algo que haya que pedirle a nadie.
        /// </summary>
        private static string writableInstallRoot(string target, Action<string> onLogLine)
        {
            if (canWriteInside(target))
                return target;

            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Torii-Mapperatorinator");

            if (string.Equals(fallback, target, StringComparison.OrdinalIgnoreCase) || !canWriteInside(fallback))
                throw new InvalidOperationException($"Windows won't let Torii write to {target}, and neither to your user folder. Install Mapperatorinator yourself wherever you can write, then point the \"Advanced\" field above at its inference.py.");

            onLogLine($"{target} needs administrator rights to write into, so the install goes to {fallback} instead.");
            return fallback;
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

            try
            {
                process.Start();
            }
            catch (Exception e)
            {
                // el sistema operativo no lo pudo arrancar (no existe, no tiene permisos):
                // sin esto sale la excepcion cruda y nadie entiende que fue.
                throw new InvalidOperationException($"couldn't run {exeParts[0]}: {e.Message}", e);
            }

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

            string[] launcher = splitLauncher(PythonExecutable);
            var psi = new ProcessStartInfo(launcher[0])
            {
                WorkingDirectory = Config.InstallPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            applyProcessEnvironment(psi);

            for (int i = 1; i < launcher.Length; i++)
                psi.ArgumentList.Add(launcher[i]);

            foreach (string a in args)
                psi.ArgumentList.Add(a);

            var amdCard = DetectAmdGpu();

            if (amdCard != null)
            {
                onLogLine($"amd gpu: {amdCard.Name} ({amdCard.Gfx}), rocm {(RocmAvailable ? "on" : "off")}");

                string? systemOverride = Environment.GetEnvironmentVariable(@"HSA_OVERRIDE_GFX_VERSION");

                if (systemOverride != null)
                    onLogLine($"HSA_OVERRIDE_GFX_VERSION={systemOverride} comes from your system");
            }

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

            // la placa fallo de verdad: se apaga sola para que la proxima no vuelva a
            // arriesgar la sesion entera de la persona.
            if (process.ExitCode != 0 && IsGpuFault(string.Join('\n', snapshot)))
                MarkRocmBlocked(@"the card faulted during a real generation");

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
                // explicito, nunca auto: con la rueda de rocm instalada torch ve la placa
                // igual, y device=auto la agarraria aunque nosotros la hayamos descartado.
                $"device={toolDevice(EffectiveDevice())}",
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

        /// <summary>Whether the user chose to generate on an AMD GPU. Off until they say so.</summary>
        [JsonPropertyName(@"rocm_enabled")]
        public bool RocmEnabled { get; set; }

        /// <summary>"auto" (o null), "gpu", "cpu": lo que la persona pidio explicitamente.</summary>
        [JsonPropertyName(@"device_preference")]
        public string? DevicePreference { get; set; }

        /// <summary>Set once the card has faulted: we don't put anyone through that twice.</summary>
        [JsonPropertyName(@"rocm_blocked")]
        public bool RocmBlocked { get; set; }

        /// <summary>What torch says the card is ("gfx1201"), straight from the driver.</summary>
        [JsonPropertyName(@"rocm_arch")]
        public string? RocmArch { get; set; }

        /// <summary>The chips the installed pytorch actually has kernels for.</summary>
        [JsonPropertyName(@"rocm_arch_list")]
        public string? RocmArchList { get; set; }

        /// <summary>HSA_OVERRIDE_GFX_VERSION that made the card match, when one was needed.</summary>
        [JsonPropertyName(@"rocm_override")]
        public string? RocmOverride { get; set; }

        /// <summary>Which pytorch wheel index the ROCm build came from.</summary>
        [JsonPropertyName(@"rocm_index")]
        public string? RocmIndex { get; set; }

        /// <summary>Cual de las placas usar cuando hay mas de una. La elige el sondeo.</summary>
        public int? GpuIndex { get; set; }

        /// <summary>Que hardware habia cuando se eligio, para no fijar un indice viejo.</summary>
        public string? GpuSignature { get; set; }

        /// <summary>Why the GPU was given up on, in the user's words.</summary>
        [JsonPropertyName(@"rocm_last_error")]
        public string? RocmLastError { get; set; }

        /// <summary>
        /// True while the card is being tested. If it's still true at startup, the test
        /// took the game down with it, which is all we need to know about that card.
        /// </summary>
        [JsonPropertyName(@"rocm_trial_pending")]
        public bool RocmTrialPending { get; set; }

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
