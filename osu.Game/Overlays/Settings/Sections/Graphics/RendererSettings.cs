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
        private readonly Bindable<SettingsNote.Data?> latencySettingNote = new Bindable<SettingsNote.Data?>();

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

            Children = new Drawable[]
            {
                new SettingsItemV2(new RendererSettingsDropdown
                {
                    Caption = GraphicsSettingsStrings.Renderer,
                    Current = renderer,
                    Items = host.GetPreferredRenderersForCurrentPlatform().Order()
#pragma warning disable CS0612 // Type or member is obsolete
                                .Where(t => t != RendererType.Vulkan && t != RendererType.OpenGLLegacy),
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
                    // When low latency is enabled, only allow unlimited FPS options
                    // This allows competitive players to use unlimited FPS while keeping low latency benefits
                    frameSyncMode.Disabled = false; // Keep enabled but filter options

                    // If current mode is not unlimited, switch to UnlimitedNoCap for best performance
                    if (frameSyncMode.Value != FrameSync.Unlimited && frameSyncMode.Value != FrameSync.UnlimitedNoCap)
                        frameSyncMode.Value = FrameSync.UnlimitedNoCap;
                }
                else
                {
                    frameSyncMode.Disabled = false;
                }

                latencySettingNote.Value = null;

                if (r.NewValue == LatencyMode.Boost)
                    SetLatencyBoostNotice();
            }, true);

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
                if (item == RendererType.Automatic && automaticRendererInUse)
                    return LocalisableString.Interpolate($"{base.GenerateItemText(item)} ({hostResolvedRenderer.GetDescription()})");

                return base.GenerateItemText(item);
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

                if (latencyMode.Value != LatencyMode.Off)
                {
                    // When low latency is enabled, only show unlimited options
                    Items = allItems.Where(x => x == FrameSync.Unlimited || x == FrameSync.UnlimitedNoCap).Order();
                }
                else
                {
                    // When low latency is disabled, show all options
                    Items = allItems.Order();
                }
            }
        }
    }
}
