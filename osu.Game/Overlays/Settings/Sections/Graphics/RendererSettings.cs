// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Localisation;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays.Dialog;
using System;

namespace osu.Game.Overlays.Settings.Sections.Graphics
{
    public partial class RendererSettings : SettingsSubsection
    {
        protected override LocalisableString Header => GraphicsSettingsStrings.RendererHeader;

        private bool automaticRendererInUse;

        private FormCheckBox? dangerousUnlimitedCheckbox;
        private readonly Bindable<SettingsNote.Data?> dangerousUnlimitedNote = new Bindable<SettingsNote.Data?>();

        [BackgroundDependencyLoader]
        private void load(FrameworkConfigManager config, OsuConfigManager osuConfig, IDialogOverlay? dialogOverlay, OsuGame? game, GameHost host)
        {
            var renderer = config.GetBindable<RendererType>(FrameworkSetting.Renderer);
            automaticRendererInUse = renderer.Value == RendererType.Automatic;

            var dangerousUnlimitedNoCap = config.GetBindable<bool>(FrameworkSetting.AllowDangerousUnlimitedNoCap);
            var toriiInputAudioHz = osuConfig.GetBindable<ToriiInputAudioHzMode>(OsuSetting.ToriiInputAudioHz);

            IEnumerable<RendererType> availableRenderers = host.GetPreferredRenderersForCurrentPlatform().Order();

            // Vulkan renderers are pretty broken to the point it may result in a startup crash at worst.
            // If a user isn't already using it let's hide it until we can fix.
            if (renderer.Value != RendererType.Deferred_Vulkan)
                availableRenderers = availableRenderers.Where(t => t != RendererType.Deferred_Vulkan);
            if (renderer.Value != RendererType.Vulkan)
                availableRenderers = availableRenderers.Where(t => t != RendererType.Vulkan);

            Children = new Drawable[]
            {
                new SettingsItemV2(new RendererDropdown
                {
                    Caption = GraphicsSettingsStrings.Renderer,
                    Current = renderer,
                    Items = availableRenderers,
                })
                {
                    Keywords = new[] { @"compatibility", @"directx" },
                },
                // TODO: this needs to be a custom dropdown at some point
                new SettingsItemV2(new FormEnumDropdown<FrameSync>
                {
                    Caption = GraphicsSettingsStrings.FrameLimiter,
                    Current = config.GetBindable<FrameSync>(FrameworkSetting.FrameSync),
                })
                {
                    Keywords = new[] { @"fps", @"framerate" },
                },
                // torii: mismo dropdown de Hz que esta en Torii > Interface, mirroreado aca al lado del
                // frame limiter (mismo bindable, quedan sincronizados); su casa natural es Graphics.
                new SettingsItemV2(new FormEnumDropdown<ToriiInputAudioHzMode>
                {
                    Caption = "Input/audio thread rate",
                    HintText = "How fast the input, audio and update threads run. Higher rates suit high-polling-rate mice (e.g. 8000 Hz) but cost more CPU. 2000 Hz is a safe default. Applies instantly. Ignored (and greyed out) while 'I am stupid' is on, since that runs everything uncapped.",
                    Current = toriiInputAudioHz,
                })
                {
                    Keywords = new[] { @"hz", @"polling", @"rate", @"input", @"audio", @"thread", @"latency", @"8000", @"performance" },
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
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = GraphicsSettingsStrings.ShowFPS,
                    Current = osuConfig.GetBindable<bool>(OsuSetting.ShowFpsDisplay),
                })
                {
                    Keywords = new[] { @"framerate", @"counter" },
                },
                // torii: el desglose de latencias, aparte del contador de siempre.
                new SettingsItemV2(new FormCheckBox
                {
                    Caption = "Detailed performance counter",
                    HintText = "Shows what each part of the chain costs: audio output, input polling and drawing, with the frame rate on the end.",
                    Current = osuConfig.GetBindable<bool>(OsuSetting.ToriiPerformanceCounter),
                })
                {
                    Keywords = new[] { @"framerate", @"counter", @"latency", @"performance", @"audio", @"input" },
                },
            };

            // Force-SDL3 toggle for Linux / macOS only. Windows + mobile are
            // already SDL3 unconditionally inside osu-framework, so the toggle
            // would be a no-op there - hide it to keep the panel honest. The
            // actual backend swap happens at startup via Program.cs reading
            // game.ini before the host comes up; this setting just persists the
            // user's preference + drives the restart prompt below.
            if (!OperatingSystem.IsWindows() && !OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS())
            {
                var forceSDL3 = osuConfig.GetBindable<bool>(OsuSetting.ForceSDL3);

                // suppress evita el loop infinito: el revert de Cancel re-dispara este handler.
                bool suppressSdl3Dialog = false;

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
                    // ignoramos el re-disparo del revert (Cancel), sino al revertir entraria de nuevo aca
                    // (y con RestartAppWhenExited true cerraria el juego sin querer).
                    if (suppressSdl3Dialog)
                        return;

                    // Mirrors the renderer-change pattern below: if Velopack can
                    // re-launch us, just exit and the updater brings the game back
                    // up with the new env var. Otherwise prompt with a confirm
                    // dialog and roll the toggle back on Cancel so displayed state
                    // and the backend in use stay consistent until next restart.
                    if (game?.RestartAppWhenExited() == true)
                    {
                        game.AttemptExit();
                    }
                    else
                    {
                        dialogOverlay?.Push(new ConfirmDialog(
                            GraphicsSettingsStrings.ChangeSDLBackendConfirmation,
                            () => game?.AttemptExit(),
                            () =>
                            {
                                suppressSdl3Dialog = true;
                                forceSDL3.Value = change.OldValue;
                                suppressSdl3Dialog = false;
                            }));
                    }
                });
            }

            dangerousUnlimitedNoCap.BindValueChanged(v =>
            {
                dangerousUnlimitedNote.Value = v.NewValue
                    ? new SettingsNote.Data("Unsafe mode enabled: Unlimited can now uncap update/input/audio too. Disable this first if audio starts doubling, popping, or stuttering.", SettingsNote.Type.Warning)
                    : new SettingsNote.Data("Recommended: leave this off. Unlimited will still uncap rendering, but keeps audio/input/update protected.", SettingsNote.Type.Informational);

                // torii: con "I am stupid" el input/audio van sin cap, asi que el dropdown de Hz no aplica:
                // lo griseamos para dejarlo claro. propaga al gemelo de Torii > Interface por el bindable.
                toriiInputAudioHz.Disabled = v.NewValue;
            }, true);

            // torii (CRITICO): el toggle "I am stupid" es peligroso en renderers Deferred. el Deferred
            // encola draw events del update thread al draw thread; si el update corre sin cap (que es lo
            // que prende este toggle) los eventos se encolan mas rapido de lo que la GPU los consume ->
            // memoria sin limite -> crash por OOM en ~30s. los renderers inmediatos no tienen esa cola y
            // el toggle se comporta como dice el warning (pops + calor, no crash). el default ahora es
            // Automatic (= D3D11 inmediato), pero alguien que eligio Deferred a mano (o que viene de una
            // Nova vieja con Deferred guardado en el ini) igual lo puede pegar: lo forzamos off + disabled
            // cuando se resuelve un Deferred, y lo habilitamos solo si se resuelve uno no-deferred.
            bool isDeferredRenderer(RendererType t) =>
                t == RendererType.Deferred_Direct3D11
                || t == RendererType.Deferred_Metal
                || t == RendererType.Deferred_OpenGL
                || t == RendererType.Deferred_Vulkan;

            void applyDangerousUnlimitedGate(RendererType resolvedRenderer)
            {
                if (isDeferredRenderer(resolvedRenderer))
                {
                    // forzamos off asi un `true` guardado de antes no auto-dispara el OOM al arrancar.
                    if (dangerousUnlimitedNoCap.Value)
                        dangerousUnlimitedNoCap.Value = false;

                    dangerousUnlimitedNoCap.Disabled = true;
                    if (dangerousUnlimitedCheckbox != null)
                        dangerousUnlimitedCheckbox.Current.Disabled = true;

                    dangerousUnlimitedNote.Value = new SettingsNote.Data(
                        "Disabled on the Deferred renderer: uncapped update + Deferred = unbounded memory growth + crash. Switch to a non-deferred renderer if you really want this.",
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

        private partial class RendererDropdown : FormEnumDropdown<RendererType>
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
    }
}
