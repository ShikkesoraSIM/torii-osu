// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// AMD Anti-Lag 2 SDK DirectX 11 Header
// Based on GPUOpen Anti-Lag 2 SDK v2.0
// This header provides the modern FidelityFX Anti-Lag 2 API using AMD Driver Extension

#pragma once

#include <dxgi.h>
#include <d3d11.h>
#include <windows.h>

#ifdef __cplusplus
namespace AMD {
namespace AntiLag2DX11 {

    struct Context;

    // Initialize function - call this once before the Update function.
    // context - Declare a persistent Context variable in your game code. Ensure the contents are zero'ed, and pass the address in to initialize it.
    //           Be sure to use the *same* context object everywhere when calling the Anti-Lag 2.0 SDK functions.
    // A return value of S_OK indicates that Anti-Lag 2.0 is available on the system.
    HRESULT Initialize( Context* context );

    // DeInitialize function - call this on game exit.
    // context - address of the game's context object.
    // The return value is the reference count of the internal API. It should be 0.
    ULONG DeInitialize( Context* context );

    // Update function - call this just before the input to the game is polled.
    // context - address of the game's context object.
    // enable - enables or disables Anti-Lag 2.0.
    // maxFPS - sets a framerate limit. Zero will disable the limiter.
    HRESULT Update( Context* context, bool enable, unsigned int maxFPS );

    // Forward declaration of the Anti-Lag 2.0 interface into the DX11 driver
    class IAmdDxExtInterface
    {
    public:
        virtual unsigned int AddRef() = 0;
        virtual unsigned int Release() = 0;

    protected:
        IAmdDxExtInterface() {}
        virtual ~IAmdDxExtInterface() = 0 {}
    };

    // Structure version 1 for Anti-Lag 2.0:
    struct APIData_v1
    {
        unsigned int    uiSize;
        unsigned int    uiVersion;
        unsigned int    eMode;
        const char*     sControlStr;
        unsigned int    uiControlStrLength;
        unsigned int    maxFPS;
    };

    // Forward declaration of the Anti-Lag interface into the DX11 driver
    struct IAmdDxExtAntiLagApi : public IAmdDxExtInterface
    {
    public:
        virtual HRESULT UpdateAntiLagStateDx11( APIData_v1* pApiCallbackData ) = 0;
    };

    // Context structure for the SDK. Declare a persistent object of this type *once* in your game code.
    // Ensure the contents are initialized to zero before calling Initialize() but do not modify these members directly after that.
    struct Context
    {
        IAmdDxExtAntiLagApi*  m_pAntiLagAPI = nullptr;
        bool                  m_enabled = false;
        unsigned int          m_maxFPS = 0;
    };

} // namespace AntiLag2DX11
} // namespace AMD

extern "C" {
#endif

    // Legacy C-compatible API (for backward compatibility with older drivers)
    // These functions use the internal amd_antilag_dx11.dll which works on RDNA 1-3 (RX 5000-7000 series)
    // For RDNA 4 (RX 9000 series) support, use the C++ namespace API above instead.

    typedef struct AntiLag2DX11Context
    {
        void* reserved[8];
    } AntiLag2DX11Context;

    typedef enum AntiLag2Mode
    {
        ANTI_LAG_2_MODE_OFF = 0,
        ANTI_LAG_2_MODE_ON = 1,
        ANTI_LAG_2_MODE_BOOST = 2
    } AntiLag2Mode;

    typedef enum AntiLag2Result
    {
        ANTI_LAG_2_RESULT_OK = 0,
        ANTI_LAG_2_RESULT_FAIL = -1,
        ANTI_LAG_2_RESULT_UNSUPPORTED = -2,
        ANTI_LAG_2_RESULT_INVALID_ARGUMENT = -3,
        ANTI_LAG_2_RESULT_NOT_INITIALIZED = -4
    } AntiLag2Result;

    // Initialize Anti-Lag 2 for DirectX 11 (Legacy)
    AntiLag2Result AmdAntiLag2Dx11Initialize(
        AntiLag2DX11Context* context,
        ID3D11Device* device);

    // Update Anti-Lag 2 state (Legacy)
    AntiLag2Result AmdAntiLag2Dx11Update(
        AntiLag2DX11Context* context,
        bool enable,
        unsigned int maxFps);

    // Deinitialize Anti-Lag 2 (Legacy)
    AntiLag2Result AmdAntiLag2Dx11DeInitialize(
        AntiLag2DX11Context* context);

#ifdef __cplusplus
}
#endif
