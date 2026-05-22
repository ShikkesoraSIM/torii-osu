// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering.LowLatency;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays.Dialog;

namespace osu.Game.Overlays.Settings.Sections.Graphics
{
    public partial class RendererSettings : SettingsSubsection
    {
        protected override LocalisableString Header => GraphicsSettingsStrings.RendererHeader;

        private bool automaticRendererInUse;

        private FormEnumDropdown<LatencyMode>? latencySetting;
        private SettingsItemV2? latencySettingItem;
        private FormCheckBox? dangerousUnlimitedCheckbox;
        private readonly Bindable<SettingsNote.Data?> latencySettingNote = new Bindable<SettingsNote.Data?>();
        private readonly Bindable<SettingsNote.Data?> dangerousUnlimitedNote = new Bindable<SettingsNote.Data?>();

        private LatencyProviderType currentProvider = LatencyProviderType.None;

        private enum LatencyProviderType
        {
            None,
            NVIDIA,
            AMD
        }

        [BackgroundDependencyLoader]
        private void load(FrameworkConfigManager config, OsuConfigManager osuConfig, IDialogOverlay? dialogOverlay, OsuGame? game, GameHost host)
        {
            var renderer = config.GetBindable<RendererType>(FrameworkSetting.Renderer);
            automaticRendererInUse = renderer.Value == RendererType.Automatic;

            var reflexMode = config.GetBindable<LatencyMode>(FrameworkSetting.LatencyMode);
            var frameSyncMode = config.GetBindable<FrameSync>(FrameworkSetting.FrameSync);
            var dangerousUnlimitedNoCap = config.GetBindable<bool>(FrameworkSetting.AllowDangerousUnlimitedNoCap);

            Children = new Drawable[]
            {
                new SettingsItemV2(new RendererSettingsDropdown
                {
                    Caption = GraphicsSettingsStrings.Renderer,
                    Current = renderer,
                    // D3D12 (both immediate and Deferred_Direct3D12) is hidden
                    // from the UI dropdown because the backend is still
                    // experimental and we don't want users accidentally
                    // selecting it from settings. Power users can still opt
                    // in by editing %APPDATA%\osu-torii\framework.ini and
                    // setting `Renderer = Deferred_Direct3D12` — the backend
                    // remains fully functional in osu-framework's renderer
                    // fallback list, just not advertised.
                    Items = host.GetPreferredRenderersForCurrentPlatform().Order()
#pragma warning disable CS0612 // Type or member is obsolete
                                .Where(t => t != RendererType.Vulkan
                                            && t != RendererType.OpenGLLegacy
                                            && t != RendererType.Direct3D12
                                            && t != RendererType.Deferred_Direct3D12),
#pragma warning restore CS0612 // Type or member is obsolete
                })
                {
                    Keywords = new[] { @"compatibility", @"directx" },
                },
                new SettingsItemV2(new FrameSyncSettingsDropdown
                {
                    Caption = GraphicsSettingsStrings.FrameLimiter,
                    Current = frameSyncMode,
                })
                {
                    Keywords = new[] { @"fps" },
                },
                // Torii: input + audio thread rate. The historical Torii
                // competitive default of 2000 Hz used to be hardcoded
                // regardless of the frame limiter pick — that was fine on
                // capable desktops but pinned weaker machines at unnecessarily
                // high CPU cost (especially in VSync / Limit2x modes where
                // the user is explicitly trying to keep things cool). The
                // setting now lets users pick their own cap; lower for
                // older hardware, higher if they want the tightest input
                // latency. Lives directly under the Frame limiter dropdown
                // because the two are closely related (input/audio rate
                // pairs with how often draw/update rate caps are evaluated).
                new SettingsItemV2(new FormEnumDropdown<ToriiInputAudioHzMode>
                {
                    Caption = "Input/audio thread rate",
                    HintText = "How fast the input + audio threads run. Higher = tighter input latency but more CPU. "
                               + "2000 Hz is the Torii default; drop to 500/1000 if your machine struggles, "
                               + "push to 4000/8000 only if you have CPU headroom to spare. "
                               + "Does not apply when 'I am stupid' is on (that mode runs fully uncapped).",
                    Current = osuConfig.GetBindable<ToriiInputAudioHzMode>(OsuSetting.ToriiInputAudioHz),
                    NewFeatureId = NewFeatureRegistry.InputAudioHz,
                })
                {
                    Keywords = new[] { @"hz", @"input", @"audio", @"thread", @"polling", @"rate", @"latency", @"frequency", @"competitive", @"2000", @"1000", @"4000", @"8000", @"torii" },
                },
                new SettingsItemV2(dangerousUnlimitedCheckbox = new FormCheckBox
                {
                    Caption = "I am stupid, I ignore warnings and want no limits",
                    HintText = "Allows the experimental Unlimited mode to uncap update, input, and audio scheduling too. This can cause audio pops, stutters, heat, and general gremlin behaviour.",
                    Current = dangerousUnlimitedNoCap,
                })
                {
                    Keywords = new[] { @"fps", @"unlimited", @"no cap", @"danger", @"audio" },
                    Note = { BindTarget = dangerousUnlimitedNote },
                },
                new SettingsItemV2(new FormEnumDropdown<ExecutionMode>
                {
                    Caption = GraphicsSettingsStrings.ThreadingMode,
                    Current = config.GetBindable<ExecutionMode>(FrameworkSetting.ExecutionMode)
                }),
                latencySettingItem = new SettingsItemV2(latencySetting = new FormEnumDropdown<LatencyMode>
                {
                    Caption = "Low Latency Mode",
                    Current = reflexMode,
                    HintText = "Reduces input-to-display latency using GPU vendor-specific technologies.\nRequires compatible NVIDIA or AMD GPU with recent drivers."
                })
                {
                    Keywords = new[] { @"latency", @"low", @"input", @"lag", @"nvidia", @"amd", @"reflex", @"anti-lag", @"antilag" },
                    Note = { BindTarget = latencySettingNote },
                },
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = GraphicsSettingsStrings.ShowFPS,
                    Current = osuConfig.GetBindable<bool>(OsuSetting.ShowFpsDisplay)
                }),
            };

            // Force-SDL3 toggle for Linux / macOS only. Windows + mobile are
            // already SDL3 unconditionally inside osu-framework, so the
            // toggle would be a no-op there — hide it to keep the panel
            // honest. The actual backend swap happens at startup via
            // Program.cs reading game.ini before the host comes up; this
            // setting just persists the user's preference + drives the
            // restart prompt below.
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS())
            {
                var forceSDL3 = osuConfig.GetBindable<bool>(OsuSetting.ForceSDL3);

                AddRange(new Drawable[]
                {
                    new SettingsItemV2(new FormCheckBox
                    {
                        Caption = GraphicsSettingsStrings.ForceSDL3,
                        HintText = GraphicsSettingsStrings.ForceSDL3Description,
                        Current = forceSDL3,
                    })
                    {
                        Keywords = new[] { @"sdl", @"sdl2", @"sdl3", @"backend", @"linux", @"macos", @"window", @"input" },
                    },
                });

                forceSDL3.BindValueChanged(change =>
                {
                    // Mirrors the renderer-change pattern below: if Velopack
                    // can re-launch us, just exit and the updater brings the
                    // game back up with the new env var. Otherwise prompt the
                    // user with a confirm dialog and roll back the toggle to
                    // its previous value on Cancel, so the displayed state
                    // and the backend in use stay consistent until the next
                    // restart.
                    if (game?.RestartAppWhenExited() == true)
                    {
                        game.AttemptExit();
                    }
                    else
                    {
                        dialogOverlay?.Push(new ConfirmDialog(
                            GraphicsSettingsStrings.ChangeSDLBackendConfirmation,
                            () => game?.AttemptExit(),
                            () => forceSDL3.Value = change.OldValue));
                    }
                });
            }

            // Determine which low latency provider is available
            UpdateLatencyProvider(host);

            // Hide low latency settings if not using Direct3D 11 renderer
            if (host.ResolvedRenderer is not (RendererType.Deferred_Direct3D11 or RendererType.Direct3D11))
            {
                reflexMode.Value = LatencyMode.Off;
                latencySettingItem.CanBeShown.Value = false;
            }
            else
            {
                UpdateLatencyProviderUI();
            }

            // Handle frame limiter when low latency mode is enabled
            reflexMode.BindValueChanged(r =>
            {
                if (r.NewValue != LatencyMode.Off)
                {
                    // Keep the user's frame limiter unchanged. Forcing the no-cap mode can starve
                    // audio scheduling on some systems and causes audible pops/stutters.
                    frameSyncMode.Disabled = false;
                }
                else
                {
                    frameSyncMode.Disabled = false;
                }

                latencySettingNote.Value = null;

                if (r.NewValue == LatencyMode.Boost)
                    SetLatencyBoostNotice();
            }, true);

            dangerousUnlimitedNoCap.BindValueChanged(v =>
            {
                dangerousUnlimitedNote.Value = v.NewValue
                    ? new SettingsNote.Data("Unsafe mode enabled: Unlimited can now uncap update/input/audio too. Disable this first if audio starts doubling, popping, or stuttering.", SettingsNote.Type.Warning)
                    : new SettingsNote.Data("Recommended: leave this off. Unlimited will still uncap rendering, but keeps audio/input/update protected.", SettingsNote.Type.Informational);
            }, true);

            // CRITICAL safety: the "I am stupid, ignore limits" toggle is
            // unsafe on Deferred renderers. Deferred queues per-frame draw
            // events from the update thread to the draw thread; if the
            // update thread runs uncapped (which this toggle enables),
            // events queue up faster than the GPU can consume them →
            // unbounded memory growth → OOM crash within ~30 seconds.
            // Immediate (non-deferred) renderers don't have this queue
            // and the toggle behaves as the warning text describes
            // (audio pops + heat, not RAM-exhaustion crash).
            //
            // Torii Nova ships Deferred as the DEFAULT, so most users
            // would hit this if they enabled the toggle. Force-disable
            // + force-off when Deferred is resolved; re-enable for
            // power users who explicitly picked a non-deferred renderer.
            bool isDeferredRenderer(RendererType t) =>
                t == RendererType.Deferred_Direct3D11
                || t == RendererType.Deferred_Direct3D12
                || t == RendererType.Deferred_Metal
                || t == RendererType.Deferred_OpenGL
                || t == RendererType.Deferred_Vulkan;

            void applyDangerousUnlimitedGate(RendererType resolvedRenderer)
            {
                bool deferred = isDeferredRenderer(resolvedRenderer);
                if (deferred)
                {
                    // Force the value off so a previously-saved `true`
                    // doesn't auto-trigger the OOM crash on first launch
                    // after the user switched into a deferred renderer.
                    if (dangerousUnlimitedNoCap.Value)
                        dangerousUnlimitedNoCap.Value = false;

                    dangerousUnlimitedNoCap.Disabled = true;
                    if (dangerousUnlimitedCheckbox != null)
                        dangerousUnlimitedCheckbox.Current.Disabled = true;

                    dangerousUnlimitedNote.Value = new SettingsNote.Data(
                        "Disabled on the Deferred renderer — uncapped update + Deferred = unbounded memory growth + crash. Switch to a non-deferred renderer (or edit framework.ini to opt out of Deferred) if you really want this.",
                        SettingsNote.Type.Warning);
                }
                else
                {
                    dangerousUnlimitedNoCap.Disabled = false;
                    if (dangerousUnlimitedCheckbox != null)
                        dangerousUnlimitedCheckbox.Current.Disabled = false;
                }
            }

            applyDangerousUnlimitedGate(host.ResolvedRenderer);

            renderer.BindValueChanged(r =>
            {
                if (r.NewValue == host.ResolvedRenderer)
                    return;

                // Need to check startup renderer for the "automatic" case, as ResolvedRenderer above will track the final resolved renderer instead.
                if (r.NewValue == RendererType.Automatic && automaticRendererInUse)
                    return;

                // Update latency provider when renderer changes
                UpdateLatencyProvider(host);
                UpdateLatencyProviderUI();

                if (game?.RestartAppWhenExited() == true)
                {
                    game.AttemptExit();
                }
                else
                {
                    dialogOverlay?.Push(new ConfirmDialog(GraphicsSettingsStrings.ChangeRendererConfirmation, () => game?.AttemptExit(), () =>
                    {
                        renderer.Value = automaticRendererInUse ? RendererType.Automatic : host.ResolvedRenderer;
                    }));
                }
            });
        }

        private void UpdateLatencyProvider(GameHost host)
        {
            // Check if we're using Direct3D 11 renderer (required for both NVIDIA and AMD low latency)
            if (host.ResolvedRenderer is (RendererType.Deferred_Direct3D11 or RendererType.Direct3D11))
            {
                // Try to determine GPU vendor from the low latency provider type
                // This is set by the desktop project during startup
                var providerType = host.GetLowLatencyProviderType();

                switch (providerType)
                {
                    case "NVAPIDirect3D11LowLatencyProvider":
                        currentProvider = LatencyProviderType.NVIDIA;
                        Logger.Log("NVIDIA GPU detected - NVIDIA Reflex features available.");
                        break;

                    case "AMDAntiLag2Direct3D11LowLatencyProvider":
                        currentProvider = LatencyProviderType.AMD;
                        Logger.Log("AMD GPU detected - AMD Anti-Lag 2 features available.");
                        break;

                    default:
                        currentProvider = LatencyProviderType.None;
                        Logger.Log("Direct3D 11 renderer detected but no compatible low latency provider found.");
                        break;
                }
            }
            else
            {
                currentProvider = LatencyProviderType.None;
                Logger.Log("Low latency features not available for current renderer.");
            }
        }

        private void UpdateLatencyProviderUI()
        {
            if (latencySetting == null || latencySettingItem == null)
                return;

            switch (currentProvider)
            {
                case LatencyProviderType.NVIDIA:
                    latencySetting.HintText = "Reduces latency by leveraging the NVIDIA Reflex API on NVIDIA GPUs.\nRecommended to have On, turn Off only if experiencing issues.";
                    latencySettingItem.CanBeShown.Value = true;
                    break;

                case LatencyProviderType.AMD:
                    latencySetting.HintText = "Reduces latency by leveraging AMD Anti-Lag 2 on AMD RDNA GPUs.\nRecommended to have On, turn Off only if experiencing issues.";
                    latencySettingItem.CanBeShown.Value = true;
                    break;

                case LatencyProviderType.None:
                    latencySettingItem.CanBeShown.Value = false;
                    break;
            }
        }

        private void SetLatencyBoostNotice()
        {
            string noticeText = currentProvider switch
            {
                LatencyProviderType.NVIDIA => "Boost increases GPU power consumption and may increase latency in some cases. Disable Boost if experiencing issues.",
                LatencyProviderType.AMD => "Boost mode provides maximum latency reduction but may increase GPU power consumption. Disable Boost if experiencing issues.",
                _ => "Boost mode increases GPU power consumption. Disable if experiencing issues."
            };

            latencySettingNote.Value = new SettingsNote.Data(noticeText, SettingsNote.Type.Warning);
        }

        private partial class RendererSettingsDropdown : FormEnumDropdown<RendererType>
        {
            private RendererType hostResolvedRenderer;
            private bool automaticRendererInUse;

            [BackgroundDependencyLoader]
            private void load(FrameworkConfigManager config, GameHost host)
            {
                var renderer = config.GetBindable<RendererType>(FrameworkSetting.Renderer);
                automaticRendererInUse = renderer.Value == RendererType.Automatic;
                hostResolvedRenderer = host.ResolvedRenderer;
            }

            protected override LocalisableString GenerateItemText(RendererType item)
            {
#if TORII_NOVA
                // Torii Nova: full-coverage label switch so the dropdown
                // never falls through to `base.GenerateItemText` for any
                // shipping RendererType value. Previously the (Nova)
                // relabel switch only handled Deferred_* entries and the
                // non-Deferred immediate entries (Direct3D 11 / Direct3D 12 /
                // OpenGL / Vulkan / Metal) fell through to the framework
                // base implementation — which in some configurations of
                // FormEnumDropdown<T> ends up returning an empty string
                // for plain [Description]-annotated enums, leaving those
                // dropdown items literally blank on Nova builds. Spelling
                // out every value here keeps the dropdown legible in any
                // framework version.
                //
                // Stable (Torii) keeps the upstream rendering — this whole
                // block compiles to nothing without the TORII_NOVA define.
                LocalisableString novaText = item switch
                {
                    RendererType.Automatic              => "Automatic",
                    RendererType.Direct3D11             => "Direct3D 11",
                    RendererType.Direct3D12             => "Direct3D 12",
                    RendererType.Metal                  => "Metal",
                    RendererType.OpenGL                 => "OpenGL",
                    RendererType.Vulkan                 => "Vulkan",
                    RendererType.OpenGLLegacy           => "OpenGL (Legacy)",
                    RendererType.Deferred_Direct3D11    => "Direct3D 11 (Nova)",
                    RendererType.Deferred_Direct3D12    => "Direct3D 12 (Nova)",
                    RendererType.Deferred_Metal         => "Metal (Nova)",
                    RendererType.Deferred_OpenGL        => "OpenGL (Nova)",
                    RendererType.Deferred_Vulkan        => "Vulkan (Nova)",
                    _                                   => item.ToString()
                };

                if (item == RendererType.Automatic && automaticRendererInUse)
                    return LocalisableString.Interpolate($"{novaText} ({hostResolvedRenderer.GetDescription()})");

                return novaText;
#else
                if (item == RendererType.Automatic && automaticRendererInUse)
                    return LocalisableString.Interpolate($"{base.GenerateItemText(item)} ({hostResolvedRenderer.GetDescription()})");

                return base.GenerateItemText(item);
#endif
            }
        }

        private partial class FrameSyncSettingsDropdown : FormDropdown<FrameSync>
        {
            private Bindable<LatencyMode> latencyMode = null!;

            [BackgroundDependencyLoader]
            private void load(FrameworkConfigManager config)
            {
                latencyMode = config.GetBindable<LatencyMode>(FrameworkSetting.LatencyMode);
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                latencyMode.BindValueChanged(_ => updateItems(), true);
            }

            private void updateItems()
            {
                var allItems = Enum.GetValues<FrameSync>();

                Items = allItems.Order();
            }
        }
    }
}
