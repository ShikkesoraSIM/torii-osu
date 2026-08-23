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
        public bool Actionable => State == RequirementState.Missing
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
                return new HardwareInfo
                {
                    Device = device,
                    GpuName = amd.Name,
                    AmdOffered = !runner.Config.RocmBlocked && amd.KfdAccessible,
                    Description = runner.Config.RocmBlocked
                        ? $"{amd.Name} found, but it faulted when we tried to generate on it, so generation runs on the CPU. Slower, but it always works."
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

        private static HardwareInfo amdHardware(string device)
        {
            var amd = MapperatorinatorRunner.DetectAmdGpu();

            if (amd == null)
                return new HardwareInfo { Device = device, GpuName = @"AMD GPU", Description = @"AMD GPU found: generation runs on it through ROCm." };

            if (!amd.KfdAccessible)
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
                    : hardware.AmdOffered && installed ? @"Want to try it? We'll install the ROCm build of pytorch and run a two-second test on the card first. If it doesn't hold up, everything goes back to the CPU by itself."
                    : hardware.AmdOffered ? @"Install Mapperatorinator first (below); once it's there you can try the GPU from here."
                    : string.Empty,
                // el boton solo cuando la tool ya esta: sin venv no hay nada que testear.
                CanAutoInstall = hardware.AmdOffered && installed,
                AutoInstallLabel = @"Try the GPU (experimental)",
            });

            if (hardware.IsMobile)
                return list;

            // 2. python 3.10
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
            bool wrongTorch = installed && gpuUnusable;

            list.Add(new Requirement
            {
                Kind = RequirementKind.Tool,
                Title = @"Mapperatorinator",
                State = !installed ? RequirementState.Missing : wrongTorch ? RequirementState.Warning : RequirementState.Ok,
                Detail = !installed
                    ? @"not installed yet. One click does it: the tool, the python packages and pytorch (about 8 GB; the model downloads on your first generation)."
                    : wrongTorch
                        ? $"installed, but its pytorch is the CPU build, so generation ignores the {hardware.GpuName} and runs on the CPU."
                        : $"installed ({runner.Config.InstallPath})",
                Instructions = wrongTorch ? torchFixInstructions(runner, hardware.Device) : string.Empty,
                AutoInstallLabel = wrongTorch ? @"Use the GPU" : @"Install automatically",
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
