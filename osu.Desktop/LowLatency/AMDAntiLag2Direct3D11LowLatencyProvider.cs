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
    /// Uses the AMD Driver Extension API (AmdDxExtCreate11) which works on all RDNA-based GPUs including RX 9000 series.
    /// </summary>
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SupportedOSPlatform("windows")]
    internal sealed class AMDAntiLag2Direct3D11LowLatencyProvider : IDirect3D11LowLatencyProvider
    {
        public bool IsAvailable { get; private set; }

        private IntPtr _deviceHandle;
        private IntPtr _amdDxExtInterface;
        private bool _initialized;
        private LatencyMode currentMode = LatencyMode.Off;

        [ComImport]
        [Guid("C4FE4B80-5EE9-4B4E-8BC2-5A9A7C85A07D")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAmdDxExtInterface
        {
            uint AddRef();
            uint Release();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct APIData_v1
        {
            public uint uiSize;
            public uint uiVersion;
            public uint eMode;
            public IntPtr sControlStr;
            public uint uiControlStrLength;
            public uint maxFPS;
        }

        [Guid("7E4D8A8E-3B3C-4D4A-9C5A-2E8D9B7C5F3A")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IAmdDxExtAntiLagApi
        {
            uint AddRef();
            uint Release();
            int UpdateAntiLagStateDx11(IntPtr pApiCallbackData);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int AmdDxExtCreate11Delegate(IntPtr pDevice, ref IntPtr ppAntiLagApi);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int AmdDxExtCreate11NullDelegate(ref IntPtr ppAntiLagApi);

        private AmdDxExtCreate11Delegate? _amdDxExtCreate11;
        private bool _useNewAPI;

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
                if (!tryInitializeNewAPI())
                {
                    if (!tryInitializeOldAPI())
                    {
                        IsAvailable = false;
                        return;
                    }
                }

                IsAvailable = true;
                _initialized = true;
                currentMode = LatencyMode.Off;
                Logger.Log($"AMD Anti-Lag 2 initialized successfully using {(_useNewAPI ? "AMD Driver Extension API (RDNA 1-4)" : "Legacy DLL (RDNA 1-3)")}.");
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                Logger.Error(ex, "Failed to initialize AMD Anti-Lag 2");
            }
        }

        private bool tryInitializeNewAPI()
        {
            IntPtr amdDxExt = GetModuleHandle("amdxx64.dll");
            if (amdDxExt == IntPtr.Zero)
            {
                Logger.Log("AMD Driver Extension (amdxx64.dll) not found. Trying legacy API...");
                return false;
            }

            IntPtr createFunc = GetProcAddress(amdDxExt, "AmdDxExtCreate11");
            if (createFunc == IntPtr.Zero)
            {
                Logger.Log("AmdDxExtCreate11 export not found in amdxx64.dll. Trying legacy API...");
                return false;
            }

            _amdDxExtCreate11 = Marshal.GetDelegateForFunctionPointer<AmdDxExtCreate11Delegate>(createFunc);

            IntPtr interfacePtr = IntPtr.Zero;

            // Set up the "magic" request identifier (from AMD SDK)
            long magicValue = 0xbf380ebc5ab4d0a6;
            interfacePtr = new IntPtr(magicValue);

            int hr = _amdDxExtCreate11(_deviceHandle, ref interfacePtr);

            if (hr != 0 || interfacePtr == IntPtr.Zero)
            {
                Logger.Log($"AmdDxExtCreate11 failed with HRESULT: {hr}. Trying legacy API...");
                _amdDxExtCreate11 = null;
                return false;
            }

            // Query for the Anti-Lag API interface
            try
            {
                IAmdDxExtInterface? extInterface = Marshal.GetObjectForIUnknown(interfacePtr) as IAmdDxExtInterface;
                if (extInterface == null)
                {
                    extInterface?.Release();
                    Logger.Log("Failed to query Anti-Lag interface. Trying legacy API...");
                    return false;
                }

                _amdDxExtInterface = interfacePtr;
                _useNewAPI = true;

                // Initialize with disabled state
                IAmdDxExtAntiLagApi? antiLagApi = extInterface as IAmdDxExtAntiLagApi;
                if (antiLagApi != null)
                {
                    APIData_v1 initData = new APIData_v1
                    {
                        uiSize = (uint)Marshal.SizeOf<APIData_v1>(),
                        uiVersion = 1,
                        eMode = 2,
                        sControlStr = IntPtr.Zero,
                        uiControlStrLength = 0,
                        maxFPS = 0
                    };

                    IntPtr initDataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<APIData_v1>());
                    try
                    {
                        Marshal.StructureToPtr(initData, initDataPtr, false);
                        antiLagApi.UpdateAntiLagStateDx11(initDataPtr);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(initDataPtr);
                    }
                }

                extInterface.Release();
                return true;
            }
            catch
            {
                if (interfacePtr != IntPtr.Zero)
                {
                    IAmdDxExtInterface? extInterface = Marshal.GetObjectForIUnknown(interfacePtr) as IAmdDxExtInterface;
                    extInterface?.Release();
                }
                Logger.Log("Failed to initialize Anti-Lag via new API. Trying legacy API...");
                return false;
            }
        }

        private bool tryInitializeOldAPI()
        {
            IntPtr antiLagDll = GetModuleHandle("amd_antilag_dx11.dll");
            if (antiLagDll == IntPtr.Zero)
            {
                Logger.Log("AMD Anti-Lag 2 DLL (amd_antilag_dx11.dll) not found. Please ensure AMD drivers with Anti-Lag 2 support are installed.");
                return false;
            }

            IntPtr initFunc = GetProcAddress(antiLagDll, "AmdAntiLag2Dx11Initialize");
            if (initFunc == IntPtr.Zero)
            {
                Logger.Log("AmdAntiLag2Dx11Initialize not found in amd_antilag_dx11.dll");
                return false;
            }

            var initializeDelegate = Marshal.GetDelegateForFunctionPointer<OldInitializeDelegate>(initFunc);

            var context = new OldAntiLag2Context();
            var result = initializeDelegate(ref context, _deviceHandle);

            if (result != OldAntiLag2Result.ANTI_LAG_2_RESULT_OK)
            {
                Logger.Log($"Legacy AMD Anti-Lag 2 initialization failed with result: {result}");
                return false;
            }

            _useNewAPI = false;
            return true;
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

                if (_useNewAPI)
                {
                    updateNewAPI(enable, 0);
                }
                else
                {
                    updateOldAPI(enable, 0);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to set AMD Anti-Lag 2 mode");
                throw new InvalidOperationException($"Failed to set AMD Anti-Lag 2 mode: {ex.Message}");
            }
        }

        private void updateNewAPI(bool enable, uint maxFps)
        {
            if (_amdDxExtInterface == IntPtr.Zero)
                return;

            IAmdDxExtInterface? extInterface = Marshal.GetObjectForIUnknown(_amdDxExtInterface) as IAmdDxExtInterface;
            IAmdDxExtAntiLagApi? antiLagApi = extInterface as IAmdDxExtAntiLagApi;

            if (antiLagApi == null)
                return;

            APIData_v1 data = new APIData_v1
            {
                uiSize = (uint)Marshal.SizeOf<APIData_v1>(),
                uiVersion = 1,
                eMode = enable ? 1u : 2u,
                maxFPS = maxFps
            };

            string controlStr = "delag_next_osd_supported_in_dxxp = 1";
            data.sControlStr = Marshal.StringToHGlobalAnsi(controlStr);
            data.uiControlStrLength = (uint)controlStr.Length;

            IntPtr dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<APIData_v1>());
            try
            {
                Marshal.StructureToPtr(data, dataPtr, false);
                antiLagApi.UpdateAntiLagStateDx11(dataPtr);
                antiLagApi.UpdateAntiLagStateDx11(IntPtr.Zero);
            }
            finally
            {
                Marshal.FreeHGlobal(data.sControlStr);
                Marshal.FreeHGlobal(dataPtr);
            }
        }

        private void updateOldAPI(bool enable, uint maxFps)
        {
            IntPtr antiLagDll = GetModuleHandle("amd_antilag_dx11.dll");
            if (antiLagDll == IntPtr.Zero)
                return;

            IntPtr updateFunc = GetProcAddress(antiLagDll, "AmdAntiLag2Dx11Update");
            if (updateFunc == IntPtr.Zero)
                return;

            var updateDelegate = Marshal.GetDelegateForFunctionPointer<OldUpdateDelegate>(updateFunc);

            var context = new OldAntiLag2Context();
            var result = updateDelegate(ref context, enable, maxFps);

            if (result != OldAntiLag2Result.ANTI_LAG_2_RESULT_OK)
                throw new InvalidOperationException($"Failed to set AMD Anti-Lag 2 mode: {result}");
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
        }

        /// <summary>
        /// Ensure this is called once per frame, at the start of the Update phase, to allow AMD Anti-Lag 2 to manage frame timing.
        /// </summary>
        /// <exception cref="InvalidOperationException">Throws an exception if the Sleep attempt was unsuccessful.</exception>
        public void FrameSleep()
        {
            if (!IsAvailable || !_initialized)
                return;
        }

        #region Native Methods

        [DllImport("kernel32.dll", EntryPoint = "GetModuleHandle")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", EntryPoint = "GetProcAddress")]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        #endregion

        #region Legacy API Types

        [StructLayout(LayoutKind.Sequential)]
        private struct OldAntiLag2Context
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public IntPtr[] reserved;
        }

        private enum OldAntiLag2Result
        {
            ANTI_LAG_2_RESULT_OK = 0,
            ANTI_LAG_2_RESULT_FAIL = -1,
            ANTI_LAG_2_RESULT_UNSUPPORTED = -2,
            ANTI_LAG_2_RESULT_INVALID_ARGUMENT = -3,
            ANTI_LAG_2_RESULT_NOT_INITIALIZED = -4
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate OldAntiLag2Result OldInitializeDelegate(ref OldAntiLag2Context context, IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate OldAntiLag2Result OldUpdateDelegate(ref OldAntiLag2Context context, bool enable, uint maxFps);

        #endregion
    }
}
