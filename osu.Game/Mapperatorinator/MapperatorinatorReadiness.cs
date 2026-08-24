// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using osu.Framework;

namespace osu.Game.Mapperatorinator
{
    public enum RequirementState
    {
        /// <summary>Not checked yet.</summary>
        Unknown,

        /// <summary>Present and usable.</summary>
        Ok,

        /// <summary>Usable, but worth knowing about (slow hardware, experimental backend).</summary>
        Warning,

        /// <summary>Missing. Generation can't run until it's sorted.</summary>
        Missing,

        /// <summary>This machine can't do it at all (phones, tablets).</summary>
        Unsupported,
    }

    /// <summary>
    /// One thing generation needs. Carries everything the checklist shows: what it is,
    /// how it went, and what the user can do about it (auto-install, download link,
    /// or instructions for their platform).
    /// </summary>
    public class Requirement
    {
        public RequirementKind Kind { get; init; }
        public string Title { get; init; } = string.Empty;
        public RequirementState State { get; set; } = RequirementState.Unknown;

        /// <summary>What the check found, in one line ("found at C:\...", "not on PATH").</summary>
        public string Detail { get; set; } = string.Empty;

        /// <summary>What to do when it's missing, for this platform.</summary>
        public string Instructions { get; set; } = string.Empty;

        /// <summary>A page to download it from, when we can't install it ourselves.</summary>
        public string? DownloadUrl { get; set; }

        /// <summary>What the download button says.</summary>
        public string DownloadLabel { get; set; } = @"Download page";

        /// <summary>What the install button says.</summary>
        public string AutoInstallLabel { get; set; } = @"Install automatically";

        /// <summary>
        /// There is something the person can press or read here. Warnings count when they
        /// come with a fix: "generation works but runs on the CPU" is worth showing.
        /// </summary>
        /// <summary>Buttons here matter even when the row is green (turning the GPU back off).</summary>
        public bool AlwaysActionable { get; set; }

        public bool Actionable => AlwaysActionable
                                  || State == RequirementState.Missing
                                  || (State == RequirementState.Warning && (CanAutoInstall || DownloadUrl != null));

        /// <summary>Whether the game can install this one by itself.</summary>
        public bool CanAutoInstall { get; set; }

        /// <summary>Whether generation is blocked while this isn't <see cref="RequirementState.Ok"/>.</summary>
        public bool Required { get; init; } = true;

        public bool Satisfied => State == RequirementState.Ok || State == RequirementState.Warning || !Required;
    }

    public enum RequirementKind
    {
        Platform,
        Python,
        Ffmpeg,
        DiskSpace,
        Tool,
    }

    /// <summary>
    /// The hardware situation, as far as generation is concerned.
    /// </summary>
    public class HardwareInfo
    {
        /// <summary>"cuda", "rocm", "mps" or "cpu": what inference.py's device=auto will end up on.</summary>
        public string Device { get; init; } = @"cpu";

        public bool IsMobile { get; init; }

        /// <summary>There is a usable GPU but this user isn't allowed to open it (linux groups).</summary>
        public bool GpuAccessBlocked { get; init; }

        /// <summary>How to call the gpu in messages, when there is one.</summary>
        public string GpuName { get; init; } = @"the GPU";

        /// <summary>There's an AMD card sitting unused and the user can choose to try it.</summary>
        public bool AmdOffered { get; init; }

        public string Description { get; init; } = string.Empty;
    }

    /// <summary>
    /// Works out whether this machine can generate maps and what's missing if not. Every
    /// check is a real probe (spawns the tool, looks at the disk), so callers run it off
    /// the update thread and re-run it after the user fixes something.
    /// </summary>
    public static class MapperatorinatorReadiness
    {
        public const long REQUIRED_FREE_BYTES = 15L * 1024 * 1024 * 1024;

        public static readonly string PYTHON_DOWNLOAD_URL = @"https://www.python.org/downloads/release/python-31011/";
        public static readonly string FFMPEG_DOWNLOAD_URL = @"https://ffmpeg.org/download.html";
        public static readonly string HOMEBREW_URL = @"https://brew.sh";

        public static HardwareInfo DetectHardware(MapperatorinatorRunner runner)
        {
            if (RuntimeInfo.IsMobile)
            {
                return new HardwareInfo
                {
                    Device = @"cpu",
                    IsMobile = true,
                    Description = @"Phones and tablets can't run the model: it needs a desktop with python and a few GB of disk.",
                };
            }

            string device = runner.DetectDevice();

            // hay placa amd pero la generacion NO la usa: o no la pidieron todavia, o ya
            // fallo. es informacion, no un problema: en cpu anda igual.
            var amd = MapperatorinatorRunner.DetectAmdGpu();

            if (device == @"cpu" && amd != null)
            {
                // windows: no hay kfd que mirar, asi que la placa esta o no esta, y si
                // se puede usar lo decide la prueba. Decir "no se encontro ninguna gpu"
                // con una 9070 XT adentro es mentira.
                if (amd.Windows)
                {
                    return new HardwareInfo
                    {
                        Device = device,
                        GpuName = amd.Name,
                        AmdOffered = !runner.Config.RocmBlocked,
                        Description = runner.Config.RocmBlocked
                            ? $"{amd.Name} found, but it couldn't be used: {runner.Config.RocmLastError ?? "the pytorch installed here can't talk to it"}. Generation runs on the CPU."
                            : amd.WheelTargets != null
                                ? $"{amd.Name} found. Generating on an AMD card needs ROCm, and AMD publishes a Windows build of pytorch for this card: press the button and Torii installs it and runs a two-second test."
                                : $"{amd.Name} found. Generating on an AMD card needs ROCm, and AMD only publishes a Windows build for RX 7000 and newer, so Torii can't set this one up for you. If you have a pytorch that works with your card, press the button and we'll test it.",
                    };
                }

                return new HardwareInfo
                {
                    Device = device,
                    GpuName = amd.Name,
                    AmdOffered = !runner.Config.RocmBlocked && amd.KfdAccessible,
                    Description = runner.Config.RocmBlocked
                        ? $"{amd.Name} found, but it can't be used for generating: {runner.Config.RocmLastError ?? "it faulted when we tried"}{archNote(runner)}. Generation runs on the CPU, which is slower but always works."
                        : !amd.KfdAccessible
                            ? $"{amd.Name} found, but your user isn't allowed to open it (/dev/kfd), so generation runs on the CPU."
                            : $"{amd.Name} found. Generation runs on the CPU: using the card needs ROCm and on some setups that takes the display driver down, so it's off until you ask for it.",
                };
            }

            return device switch
            {
                @"cuda" => new HardwareInfo { Device = device, GpuName = @"NVIDIA GPU", Description = @"NVIDIA GPU found: generation runs on CUDA, the fast path." },
                @"rocm" => amdHardware(device),
                @"mps" => new HardwareInfo { Device = device, Description = @"Apple Silicon: generation runs on the GPU through MPS. Supported, but slower than an NVIDIA card." },
                _ => new HardwareInfo { Device = device, Description = @"No supported GPU found: generation runs on the CPU. It works, but expect several minutes per map." },
            };
        }

        /// <summary>What we learned from torch about the card, when we've asked it.</summary>
        private static string archNote(MapperatorinatorRunner runner)
        {
            if (runner.Config.RocmArch == null)
                return string.Empty;

            string list = runner.Config.RocmArchList ?? @"none";
            return $" (your card is {runner.Config.RocmArch}; that pytorch carries kernels for {list})";
        }

        private static HardwareInfo amdHardware(string device)
        {
            var amd = MapperatorinatorRunner.DetectAmdGpu();

            if (amd == null)
                return new HardwareInfo { Device = device, GpuName = @"AMD GPU", Description = @"AMD GPU found: generation runs on it through ROCm." };

            // windows no tiene kfd: pedirle a alguien que corra usermod ahi es un
            // callejon sin salida, y encima la placa ya esta andando cuando se llega aca.
            if (!amd.KfdAccessible && !amd.Windows)
            {
                return new HardwareInfo
                {
                    Device = device,
                    GpuName = amd.Name,
                    GpuAccessBlocked = true,
                    Description = $"{amd.Name} found, but your user isn't allowed to open it (/dev/kfd), so ROCm can't see it and generation would fall back to the CPU.",
                };
            }

            return new HardwareInfo
            {
                Device = device,
                GpuName = amd.Name,
                Description = $"{amd.Name} found: generation runs on it through ROCm (the runtime comes with pytorch, nothing else to install).",
            };
        }

        /// <summary>
        /// Runs every check. Slow (spawns processes); never call from the update thread.
        /// </summary>
        public static List<Requirement> Check(MapperatorinatorRunner runner)
        {
            var list = new List<Requirement>();
            var hardware = DetectHardware(runner);
            bool installed = runner.InstallLooksValid;

            // hay gpu pero el pytorch instalado no la ve (la rueda de cpu de un install
            // anterior): no se dice "gpu ok" cuando va a correr en cpu.
            bool gpuUnusable = (hardware.Device == @"cuda" || hardware.Device == @"rocm") && runner.EffectiveDevice(hardware.Device) == @"cpu";

            // 1. plataforma / hardware
            var amdCard = MapperatorinatorRunner.DetectAmdGpu();
            // en windows no hay kfd que consultar: si hay placa y la tool esta, se ofrece
            // probar, y el resultado lo decide la prueba, no el sistema operativo.
            // con una nvidia adelante, la placa que vale es esa: ofrecer "probar la GPU"
            // por una Radeon integrada termina pisando el pytorch de CUDA que ya andaba.
            bool amdUsable = amdCard != null && installed && !MapperatorinatorRunner.HasNvidiaGpu
                             && (amdCard.Windows || amdCard.KfdAccessible);

            list.Add(new Requirement
            {
                Kind = RequirementKind.Platform,
                Title = @"This machine",
                State = hardware.IsMobile ? RequirementState.Unsupported
                    : (hardware.Device == @"cuda" || hardware.Device == @"rocm") && !hardware.GpuAccessBlocked && !gpuUnusable ? RequirementState.Ok
                    : RequirementState.Warning,
                Detail = gpuUnusable
                    ? $"{hardware.GpuName} found, but the installed pytorch is the CPU build and can't use it: generation runs on the CPU until that's fixed (Mapperatorinator row below)."
                    : hardware.Description,
                Instructions = hardware.IsMobile ? @"Generate on a PC or Mac, then play the map anywhere."
                    : hardware.GpuAccessBlocked ? @"In a terminal: sudo usermod -aG render,video $USER. Then log out and back in (groups only apply at login), open Torii again and press Check."
                    : runner.Config.RocmEnabled && amdUsable ? @"Generating uses your card. If anything misbehaves (a freeze, a black screen, a crash), press ""Back to the CPU"" and everything keeps working, just slower."
                    : runner.Config.RocmBlocked && amdUsable ? @"You can try again, but save your work first: on a setup that can't handle it, the card takes the display driver down with it. ""GPU report"" writes a file with your card, your kernel and what this pytorch supports: that's the file to send if you want someone to look at it."
                    : amdUsable && amdCard?.WheelTargets != null ? @"Torii downloads AMD's ROCm build of pytorch for your card (a few GB), checks it can see the card, and runs a two-second test. If your machine has more than one GPU, the biggest one is used, not the one Windows lists first. If it doesn't hold up, everything goes back to the CPU by itself."
                    : amdUsable && amdCard?.WindowsWithoutRocm == true ? @"We ask pytorch whether it can see your card at all (a build that talks to it reports itself as CUDA, which is normal), and if it can, we run a two-second test. If pytorch can't see it, install one that does into Torii's environment, or point ""Use my own python"" at yours. ""GPU report"" shows what's there without touching the card."
                    : amdUsable ? @"Want to try it? We check what your card is and whether this pytorch has kernels for it before touching it at all, then run a two-second test. If it doesn't hold up, everything goes back to the CPU by itself. ""GPU report"" writes a file with all of that and doesn't touch the card."
                    : hardware.AmdOffered ? @"Install Mapperatorinator first (below); once it's there you can try the GPU from here."
                    : string.Empty,
                // siempre hay salida: probar, reintentar, o volver a la cpu. antes, si la
                // placa fallaba, los botones desaparecian y no quedaba forma de tocar nada.
                CanAutoInstall = amdUsable,
                AutoInstallLabel = runner.Config.RocmEnabled ? @"Back to the CPU"
                    : runner.Config.RocmBlocked ? @"Try the GPU again"
                    : @"Try the GPU (experimental)",
                AlwaysActionable = amdUsable,
            });

            if (hardware.IsMobile)
                return list;

            // 2. python 3.10. Si la persona apunto a un python propio, ese manda: puede
            //    estar adentro de un contenedor con la placa andando, y ahi no hay nada
            //    que salga a buscar.
            if (!string.IsNullOrEmpty(runner.Config.PythonPath))
            {
                list.Add(new Requirement
                {
                    Kind = RequirementKind.Python,
                    Title = @"Python",
                    State = File.Exists(runner.Config.PythonPath) ? RequirementState.Ok : RequirementState.Missing,
                    Detail = File.Exists(runner.Config.PythonPath)
                        ? $"using yours: {runner.Config.PythonPath}"
                        : $"you pointed at {runner.Config.PythonPath} and there's nothing there.",
                    Instructions = @"Clear the ""Use my own python"" field above to go back to the one Torii installs.",
                });
            }
            else
            {

            string? python = MapperatorinatorRunner.FindPython310();
            list.Add(new Requirement
            {
                Kind = RequirementKind.Python,
                Title = @"Python 3.10",
                State = python != null ? RequirementState.Ok : RequirementState.Missing,
                Detail = python != null ? $"found ({python})" : @"not found. The model only runs on 3.10, not newer. (Just installed it? Press Check.)",
                Instructions = pythonInstructions(),
                DownloadUrl = PYTHON_DOWNLOAD_URL,
            });
            }

            // 3. ffmpeg (pydub decodes the audio through it; without it the run dies with
            //    a bare "exit code 1")
            string? ffmpeg = runner.FindFfmpeg();
            bool hasBrew = MapperatorinatorRunner.FindBrew() != null;
            list.Add(new Requirement
            {
                Kind = RequirementKind.Ffmpeg,
                Title = @"FFmpeg",
                State = ffmpeg != null ? RequirementState.Ok : RequirementState.Missing,
                Detail = ffmpeg != null ? $"found ({(ffmpeg == @"ffmpeg" ? "on PATH" : ffmpeg)})" : @"not found. The audio can't be decoded without it.",
                Instructions = ffmpegInstructions(hasBrew),
                // en mac el unico camino sano es brew: mandar a ffmpeg.org (builds de
                // intel, sin instalador) es un laberinto. en linux, el package manager.
                DownloadUrl = RuntimeInfo.OS switch
                {
                    RuntimeInfo.Platform.Windows => FFMPEG_DOWNLOAD_URL,
                    RuntimeInfo.Platform.macOS => hasBrew ? null : HOMEBREW_URL,
                    _ => null,
                },
                DownloadLabel = RuntimeInfo.OS == RuntimeInfo.Platform.macOS ? @"Get Homebrew" : @"Download page",
                CanAutoInstall = RuntimeInfo.OS == RuntimeInfo.Platform.Windows || (RuntimeInfo.OS == RuntimeInfo.Platform.macOS && hasBrew),
            });

            // 4. espacio: solo importa antes de instalar la tool
            long free = runner.LargestFreeSpace();
            list.Add(new Requirement
            {
                Kind = RequirementKind.DiskSpace,
                Title = @"Disk space",
                Required = !installed,
                State = installed || free >= REQUIRED_FREE_BYTES ? RequirementState.Ok : RequirementState.Missing,
                Detail = installed
                    ? @"already installed, nothing more to download."
                    : $"{free / (1024.0 * 1024 * 1024):0.#} GB free on the roomiest drive; the tool, pytorch and the model need about 15 GB.",
                Instructions = @"Free up space on any drive; the install picks whichever has the most room.",
            });

            // 5. la tool en si (checkout + venv con torch). si pytorch quedo instalado para
            //    otro device del que hay ahora (cpu en una maquina amd de antes del soporte
            //    rocm, o una gpu nueva), avisar y ofrecer reinstalar: solo se reemplaza torch.
            // la placa esta y pytorch la ve, pero esa build no tiene kernels para ella: es
            // el caso de una RTX 50xx con la rueda de cuda 12.6. Se detecta comparando el
            // indice del que salio con el que le corresponde hoy, asi no hay que esperar a
            // que una generacion se caiga para enterarse.
            bool staleCuda = installed && hardware.Device == @"cuda"
                                       && runner.Config.CudaIndex != null
                                       && runner.Config.CudaIndex != MapperatorinatorRunner.TorchIndexUrl(@"cuda");

            bool wrongTorch = installed && (gpuUnusable || staleCuda);

            list.Add(new Requirement
            {
                Kind = RequirementKind.Tool,
                Title = @"Mapperatorinator",
                State = !installed ? RequirementState.Missing : wrongTorch ? RequirementState.Warning : RequirementState.Ok,
                Detail = !installed
                    ? @"not installed yet. One click does it: the tool, the python packages and pytorch (about 8 GB; the model downloads on your first generation)."
                    : staleCuda
                        ? $"installed, but its pytorch has no kernels for the {hardware.GpuName}: generating would die with cudaErrorNoKernelImageForDevice. One press swaps it for the build that does."
                        : wrongTorch
                            ? $"installed, but its pytorch is the CPU build, so generation ignores the {hardware.GpuName} and runs on the CPU."
                            : $"installed ({runner.Config.InstallPath})",
                Instructions = wrongTorch ? torchFixInstructions(runner, hardware.Device) : string.Empty,
                AutoInstallLabel = staleCuda ? @"Fix pytorch for this card" : wrongTorch ? @"Use the GPU" : @"Install automatically",
                CanAutoInstall = true,
            });

            return list;
        }

        /// <summary>
        /// The fix for "there's a GPU but pytorch is the CPU build", with the exact command
        /// for people who would rather type it: the version is the question everyone asks.
        /// </summary>
        private static string torchFixInstructions(MapperatorinatorRunner runner, string device)
        {
            string label = device == @"rocm" ? @"the ROCm build" : @"the CUDA build";
            string text = $"Press \"Use the GPU\": it replaces pytorch with {label} (a few GB) and leaves everything else alone. Nothing else on your system changes.";

            string? manual = runner.ManualTorchCommand(device);

            if (manual != null)
                text += $"\n\nRather do it yourself? In a terminal:\n{manual}";

            return text;
        }

        private static string pythonInstructions()
        {
            switch (RuntimeInfo.OS)
            {
                case RuntimeInfo.Platform.Windows:
                    return @"Press Download page, get ""Windows installer (64-bit)"" for 3.10, run it with the defaults, then press Check. We look where it installs, so nothing else to set up.";

                case RuntimeInfo.Platform.macOS:
                    return @"Press Download page, get ""macOS 64-bit universal2 installer"" for 3.10, run it, then press Check. We look where it installs, so nothing else to set up. (Homebrew users: brew install python@3.10 works too.)";

                default:
                    return @"In a terminal: sudo apt install python3.10 python3.10-venv (Debian/Ubuntu), sudo dnf install python3.10 (Fedora), or your distro's equivalent. Then press Check.";
            }
        }

        private static string ffmpegInstructions(bool hasBrew)
        {
            switch (RuntimeInfo.OS)
            {
                case RuntimeInfo.Platform.Windows:
                    return @"Press Install automatically. It's kept inside the Mapperatorinator folder, nothing on your system changes.";

                case RuntimeInfo.Platform.macOS:
                    return hasBrew
                        ? @"Press Install automatically (it runs brew install ffmpeg for you), or type that in Terminal yourself. Then press Check."
                        : @"This needs Homebrew. Press Get Homebrew, paste the one line from that page into Terminal, wait for it to finish, then come back and press Check: the Install button appears.";

                default:
                    return @"In a terminal: sudo apt install ffmpeg (Debian/Ubuntu), sudo dnf install ffmpeg (Fedora), sudo pacman -S ffmpeg (Arch). Then press Check.";
            }
        }

        /// <summary>Apple Silicon is the one non-NVIDIA setup with a real GPU path.</summary>
        public static bool IsAppleSilicon => RuntimeInfo.OS == RuntimeInfo.Platform.macOS && RuntimeInformation.OSArchitecture == Architecture.Arm64;
    }
}
