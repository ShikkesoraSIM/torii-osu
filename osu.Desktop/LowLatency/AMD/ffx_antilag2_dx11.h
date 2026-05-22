// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

// AMD Anti-Lag 2 SDK DirectX 11 Header
// Based on GPUOpen Anti-Lag 2 SDK documentation

#pragma once

#include <dxgi.h>
#include <d3d11.h>

#ifdef __cplusplus
extern "C" {
#endif

    typedef struct AntiLag2DX11Context
    {
        void* reserved[8];
    } AntiLag2DX11Context;

    // Anti-Lag 2 modes
    typedef enum AntiLag2Mode
    {
        ANTI_LAG_2_MODE_OFF = 0,
        ANTI_LAG_2_MODE_ON = 1,
        ANTI_LAG_2_MODE_BOOST = 2
    } AntiLag2Mode;

    // Anti-Lag 2 return codes
    typedef enum AntiLag2Result
    {
        ANTI_LAG_2_RESULT_OK = 0,
        ANTI_LAG_2_RESULT_FAIL = -1,
        ANTI_LAG_2_RESULT_UNSUPPORTED = -2,
        ANTI_LAG_2_RESULT_INVALID_ARGUMENT = -3,
        ANTI_LAG_2_RESULT_NOT_INITIALIZED = -4
    } AntiLag2Result;

    // Initialize Anti-Lag 2 for DirectX 11
    // Returns ANTI_LAG_2_RESULT_OK on success
    AntiLag2Result AmdAntiLag2Dx11Initialize(
        AntiLag2DX11Context* context,
        ID3D11Device* device);

    // Update Anti-Lag 2 state
    // Call this just before polling input
    // enable: true to enable, false to disable
    // maxFps: 0 to disable frame rate limiting, otherwise target FPS
    AntiLag2Result AmdAntiLag2Dx11Update(
        AntiLag2DX11Context* context,
        bool enable,
        unsigned int maxFps);

    // Deinitialize Anti-Lag 2
    AntiLag2Result AmdAntiLag2Dx11DeInitialize(
        AntiLag2DX11Context* context);

#ifdef __cplusplus
}
#endif