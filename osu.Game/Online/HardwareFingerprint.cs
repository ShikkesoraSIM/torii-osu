// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace osu.Game.Online
{
    /// <summary>
    /// Stable per-machine identifier derived from OS + CPU + machine name.
    /// Sent as the <c>X-Torii-HWID</c> header on outbound API requests so the
    /// server can correlate multiple accounts that originate from the same
    /// hardware (one signal among several for multi-account detection).
    /// </summary>
    public static class HardwareFingerprint
    {
        private static string? cached;
        private static readonly object cache_lock = new object();

        public static string Compute()
        {
            if (cached != null) return cached;
            lock (cache_lock)
            {
                cached ??= computeInner();
                return cached;
            }
        }

        private static string computeInner()
        {
            string[] parts =
            {
                safe(() => Environment.MachineName),
                safe(() => Environment.UserName),
                safe(() => RuntimeInformation.OSDescription),
                safe(() => RuntimeInformation.OSArchitecture.ToString()),
                safe(() => Environment.ProcessorCount.ToString()),
                safe(readCpuModel) ?? "",
                safe(readBoardSerial) ?? "",
            };

            string joined = string.Join("|", parts);
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
            // 32 hex chars is plenty of entropy and short enough to fit
            // comfortably in an HTTP header value.
            return Convert.ToHexString(hash, 0, 16).ToLowerInvariant();
        }

        private static string safe(Func<string> f)
        {
            try { return f(); }
            catch { return string.Empty; }
        }

        private static string? readCpuModel()
        {
            // /proc/cpuinfo on Linux gives us a CPU model string. Windows
            // / macOS need WMI / sysctl which we don't want to drag a
            // dependency in for, so they fall back to just OS + arch.
            if (!OperatingSystem.IsLinux()) return null;
            if (!File.Exists("/proc/cpuinfo")) return null;
            foreach (string line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("model name", StringComparison.Ordinal))
                    return line.Split(':', 2).LastOrDefault()?.Trim();
            }
            return null;
        }

        private static string? readBoardSerial()
        {
            // /sys/class/dmi/id/board_serial on Linux. Usually readable by
            // root only; on machines where it's accessible it gives us a
            // very stable signal. Failure is fine.
            const string path = "/sys/class/dmi/id/board_serial";
            if (!File.Exists(path)) return null;
            try { return File.ReadAllText(path).Trim(); }
            catch { return null; }
        }
    }
}
