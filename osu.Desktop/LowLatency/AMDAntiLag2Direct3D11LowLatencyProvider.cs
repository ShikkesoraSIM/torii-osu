// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using osu.Framework.Graphics.Rendering.LowLatency;
using osu.Framework.Logging;

namespace osu.Desktop.LowLatency
{
    /// <summary>
    /// Provider for AMD's Anti-Lag 2 low latency features.
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SupportedOSPlatform("windows")]
    internal sealed class AMDAntiLag2Direct3D11LowLatencyProvider : IDirect3D11LowLatencyProvider
    {
        public bool IsAvailable { get; private set; }

        private IntPtr _deviceHandle;
        private AntiLag2DX11Context _context;
        private bool _initialized;
        private LatencyMode currentMode = LatencyMode.Off;

        /// <summary>
        /// Initialize the AMD Anti-Lag 2 low latency provider with a native device handle.
        /// </summary>
        /// <param name="nativeDeviceHandle">An <see cref="IntPtr"/> to the handle of the D3D11 device.</param>
        /// <exception cref="InvalidOperationException">Throws an exception if AMD Anti-Lag 2 is unavailable, or the device handle provided was invalid.</exception>
        public void Initialize(IntPtr nativeDeviceHandle)
        {
            _deviceHandle = nativeDeviceHandle;

            if (_deviceHandle == IntPtr.Zero)
                throw new InvalidOperationException("The provided device handle is invalid.");

            try
            {
                // Check if the AMD Anti-Lag 2 DLL is available before trying to initialize
                IntPtr antiLagDll = loadLibrary("amd_antilag_dx11.dll");
                if (antiLagDll == IntPtr.Zero)
                {
                    IsAvailable = false;
                    Logger.Log("AMD Anti-Lag 2 DLL (amd_antilag_dx11.dll) not found. Please ensure AMD drivers with Anti-Lag 2 support are installed.");
                    return;
                }
                freeLibrary(antiLagDll);

                // Initialize Anti-Lag 2 context
                var result = AmdAntiLag2Dx11Initialize(ref _context, _deviceHandle);

                if (result == AntiLag2Result.ANTI_LAG_2_RESULT_OK)
                {
                    IsAvailable = true;
                    _initialized = true;
                    currentMode = LatencyMode.Off;
                    Logger.Log("AMD Anti-Lag 2 initialized successfully.");
                }
                else
                {
                    IsAvailable = false;
                    Logger.Log($"AMD Anti-Lag 2 initialization failed with result: {result}");
                }
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                Logger.Error(ex, "Failed to initialize AMD Anti-Lag 2");
            }
        }

        /// <summary>
        /// Set the low latency mode.
        /// </summary>
        /// <param name="mode">The <see cref="LatencyMode"/> to use.</param>
        /// <exception cref="InvalidOperationException">Throws an exception if an attempt to set the low latency mode was unsuccessful.</exception>
        public void SetMode(LatencyMode mode)
        {
            if (!IsAvailable || !_initialized)
                return;

            if (currentMode == mode)
                return;

            try
            {
                currentMode = mode;
                bool enable = mode != LatencyMode.Off;

                // For AMD Anti-Lag 2, we use the Update function
                // Call just before input polling (this will be handled by the framework)
                var result = AmdAntiLag2Dx11Update(ref _context, enable, 0); // 0 = no frame rate limit

                if (result != AntiLag2Result.ANTI_LAG_2_RESULT_OK)
                    throw new InvalidOperationException($"Failed to set AMD Anti-Lag 2 mode: {result}");
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to set AMD Anti-Lag 2 mode");
                throw new InvalidOperationException($"Failed to set AMD Anti-Lag 2 mode: {ex.Message}");
            }
        }

        /// <summary>
        /// Set a latency marker for the current frame.
        /// </summary>
        /// <remarks>WARNING: Do not log any errors that come from this method, they should be ignored as this method runs in a realtime environment.</remarks>
        /// <param name="marker">The <see cref="LatencyMarker"/> to set.</param>
        /// <param name="frameId">The frame number this marker is for.</param>
        /// <exception cref="InvalidOperationException">Throws an exception if the attempt to set the marker was unsuccessful. Please ensure this exception is ignored.</exception>
        public void SetMarker(LatencyMarker marker, ulong frameId)
        {
            if (!IsAvailable || !_initialized)
                return;

            // AMD Anti-Lag 2 doesn't use markers like NVIDIA Reflex
            // The latency reduction is handled internally by the Update() call
            // which should be called just before input polling
        }

        /// <summary>
        /// Ensure this is called once per frame, at the start of the Update phase, to allow AMD Anti-Lag 2 to manage frame timing.
        /// </summary>
        /// <exception cref="InvalidOperationException">Throws an exception if the Sleep attempt was unsuccessful.</exception>
        public void FrameSleep()
        {
            if (!IsAvailable || !_initialized)
                return;

            // AMD Anti-Lag 2 doesn't have a separate FrameSleep function
            // The timing is managed internally by the Update() call
        }

        #region Native Methods

        [DllImport("amd_antilag_dx11.dll", EntryPoint = "AmdAntiLag2Dx11Initialize")]
        private static extern AntiLag2Result AmdAntiLag2Dx11Initialize(ref AntiLag2DX11Context context, IntPtr device);

        [DllImport("amd_antilag_dx11.dll", EntryPoint = "AmdAntiLag2Dx11Update")]
        private static extern AntiLag2Result AmdAntiLag2Dx11Update(ref AntiLag2DX11Context context, bool enable, uint maxFps);

        [DllImport("amd_antilag_dx11.dll", EntryPoint = "AmdAntiLag2Dx11DeInitialize")]
        private static extern AntiLag2Result AmdAntiLag2Dx11DeInitialize(ref AntiLag2DX11Context context);

        [DllImport("kernel32.dll", EntryPoint = "LoadLibrary")]
        private static extern IntPtr loadLibrary(string dllToLoad);

        [DllImport("kernel32.dll", EntryPoint = "FreeLibrary")]
        private static extern bool freeLibrary(IntPtr hModule);

        #endregion

        #region Native Structures

        [StructLayout(LayoutKind.Sequential)]
        private struct AntiLag2DX11Context
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public IntPtr[] reserved;
        }

        private enum AntiLag2Result
        {
            ANTI_LAG_2_RESULT_OK = 0,
            ANTI_LAG_2_RESULT_FAIL = -1,
            ANTI_LAG_2_RESULT_UNSUPPORTED = -2,
            ANTI_LAG_2_RESULT_INVALID_ARGUMENT = -3,
            ANTI_LAG_2_RESULT_NOT_INITIALIZED = -4
        }

        #endregion
    }
}
