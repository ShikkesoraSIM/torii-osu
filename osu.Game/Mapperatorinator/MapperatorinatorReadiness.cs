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
        /// <summary>"cuda", "mps" or "cpu": what inference.py's device=auto will end up on.</summary>
        public string Device { get; init; } = @"cpu";

        public bool IsMobile { get; init; }

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

            return device switch
            {
                @"cuda" => new HardwareInfo { Device = device, Description = @"NVIDIA GPU found: generation runs on CUDA, the fast path." },
                @"mps" => new HardwareInfo { Device = device, Description = @"Apple Silicon: generation runs on the GPU through MPS. Supported, but slower than an NVIDIA card." },
                _ => new HardwareInfo { Device = device, Description = @"No supported GPU found: generation runs on the CPU. It works, but expect several minutes per map." },
            };
        }

        /// <summary>
        /// Runs every check. Slow (spawns processes); never call from the update thread.
        /// </summary>
        public static List<Requirement> Check(MapperatorinatorRunner runner)
        {
            var list = new List<Requirement>();
            var hardware = DetectHardware(runner);

            // 1. plataforma / hardware
            list.Add(new Requirement
            {
                Kind = RequirementKind.Platform,
                Title = @"This machine",
                State = hardware.IsMobile ? RequirementState.Unsupported : hardware.Device == @"cuda" ? RequirementState.Ok : RequirementState.Warning,
                Detail = hardware.Description,
                Instructions = hardware.IsMobile ? @"Generate on a PC or Mac, then play the map anywhere." : string.Empty,
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
                Detail = python != null ? $"found ({python})" : @"not found. The model only runs on 3.10, not newer.",
                Instructions = pythonInstructions(),
                DownloadUrl = PYTHON_DOWNLOAD_URL,
            });

            // 3. ffmpeg (pydub decodes the audio through it; without it the run dies with
            //    a bare "exit code 1")
            string? ffmpeg = runner.FindFfmpeg();
            list.Add(new Requirement
            {
                Kind = RequirementKind.Ffmpeg,
                Title = @"FFmpeg",
                State = ffmpeg != null ? RequirementState.Ok : RequirementState.Missing,
                Detail = ffmpeg != null ? $"found ({(ffmpeg == @"ffmpeg" ? "on PATH" : ffmpeg)})" : @"not found. The audio can't be decoded without it.",
                Instructions = ffmpegInstructions(),
                DownloadUrl = FFMPEG_DOWNLOAD_URL,
                CanAutoInstall = RuntimeInfo.OS == RuntimeInfo.Platform.Windows,
            });

            // 4. espacio: solo importa antes de instalar la tool
            bool installed = runner.InstallLooksValid;
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

            // 5. la tool en si (checkout + venv con torch)
            list.Add(new Requirement
            {
                Kind = RequirementKind.Tool,
                Title = @"Mapperatorinator",
                State = installed ? RequirementState.Ok : RequirementState.Missing,
                Detail = installed ? $"installed ({runner.Config.InstallPath})" : @"not installed yet. One click does it: the tool, the python packages and pytorch (about 8 GB; the model downloads on your first generation).",
                CanAutoInstall = true,
            });

            return list;
        }

        private static string pythonInstructions()
        {
            switch (RuntimeInfo.OS)
            {
                case RuntimeInfo.Platform.Windows:
                    return @"Download the 3.10 installer (Windows installer 64-bit), run it and tick ""Add python.exe to PATH"" before pressing Install. Then press Check.";

                case RuntimeInfo.Platform.macOS:
                    return @"Download the 3.10 macOS installer from the same page and run it. Then press Check.";

                default:
                    return @"Install it from your package manager, e.g. sudo apt install python3.10 python3.10-venv (Debian/Ubuntu) or sudo dnf install python3.10 (Fedora). Then press Check.";
            }
        }

        private static string ffmpegInstructions()
        {
            switch (RuntimeInfo.OS)
            {
                case RuntimeInfo.Platform.Windows:
                    return @"Let the game install it (it's kept inside the Mapperatorinator folder, nothing on your system changes), or grab a build yourself and put its bin folder on PATH.";

                case RuntimeInfo.Platform.macOS:
                    return @"brew install ffmpeg (with Homebrew), or download a static build and put it on PATH. Then press Check.";

                default:
                    return @"sudo apt install ffmpeg (Debian/Ubuntu), sudo dnf install ffmpeg (Fedora), sudo pacman -S ffmpeg (Arch). Then press Check.";
            }
        }

        /// <summary>Apple Silicon is the one non-NVIDIA setup with a real GPU path.</summary>
        public static bool IsAppleSilicon => RuntimeInfo.OS == RuntimeInfo.Platform.macOS && RuntimeInformation.OSArchitecture == Architecture.Arm64;
    }
}
