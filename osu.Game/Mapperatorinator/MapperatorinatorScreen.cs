// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;

using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Screens;
using osuTK;

namespace osu.Game.Mapperatorinator
{
    /// <summary>
    /// The "use this song for Mapperatorinator" flow: generator options up top, the
    /// metadata you want on the finished map below (held aside and applied to the
    /// .osz after generation). Pressing generate hands the job to the game-wide
    /// manager and leaves; progress lives in a pinned notification from there on.
    /// </summary>
    public partial class MapperatorinatorScreen : OsuScreen
    {
        private readonly BeatmapInfo? sourceBeatmap;
        private readonly string? externalAudioPath;
        private readonly bool addToExistingSet;

        [Resolved(canBeNull: true)]
        private INotificationOverlay? notifications { get; set; }

        [Resolved(canBeNull: true)]
        private MapperatorinatorGenerationManager? generationManager { get; set; }

        private MapperatorinatorRunner runner = null!;

        // paso 1: opciones del generador
        private FormEnumDropdown<MapperatorinatorModel> model = null!;
        private FormEnumDropdown<MapperatorinatorGamemode> gamemode = null!;
        private FormSliderBar<double> difficulty = null!;
        private FormNumberBox year = null!;
        private FormNumberBox mapperId = null!;
        private FormNumberBox keycount = null!;
        private FormNumberBox seed = null!;
        private FormTextBox descriptors = null!;
        private FormTextBox negativeDescriptors = null!;
        private DescriptorPicker descriptorPicker = null!;
        private Drawable descriptorPickerCaption = null!;
        private FormCheckBox hitsounds = null!;
        private FormCheckBox superTiming = null!;

        // paso 2: metadata que se aplica DESPUES de generar
        private FormTextBox titleBox = null!;
        private FormTextBox artistBox = null!;
        private FormTextBox creatorBox = null!;
        private FormTextBox tagsBox = null!;
        private FormFileSelector backgroundSelector = null!;

        // setup
        private FormFileSelector installSelector = null!;

        private RoundedButton generateButton = null!;
        private OsuSpriteText etaText = null!;
        private OsuTextFlowContainer logFlow = null!;
        private OsuScrollContainer logScroll = null!;

        private bool installing;
        private CancellationTokenSource? installCancellation;
        private string device = @"cpu";
        private TimeSpan currentEstimate;
        private int logLines;

        private const int max_log_lines = 400;

        // capturado en load(): BeatmapInfo es un objeto de realm atado al update
        // thread, asi que NADA del task de generacion puede volver a tocarlo.
        private double audioLengthSeconds = 180;

        public MapperatorinatorScreen(BeatmapInfo beatmap, bool addToExistingSet = false)
        {
            sourceBeatmap = beatmap;
            this.addToExistingSet = addToExistingSet;
        }

        public MapperatorinatorScreen(string audioPath)
        {
            externalAudioPath = audioPath;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours, Storage storage)
        {
            // el runner del manager es la unica fuente de verdad de config/install;
            // instanciar uno propio solo si no hay manager (test scenes).
            runner = generationManager?.Runner ?? new MapperatorinatorRunner(storage.GetFullPath(string.Empty));
            device = runner.DetectDevice();
            audioLengthSeconds = (sourceBeatmap?.Length ?? 180_000) / 1000.0;

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(colours.GreySeaFoamDark, colours.GreySeaFoamDarker),
                },
                // FormFileSelector abre su selector en un popover, y sin un
                // PopoverContainer en la jerarquia HidePopover revienta el juego entero.
                new PopoverContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new OsuScrollContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        // la barra de scroll reserva su carril en vez de dibujarse
                        // arriba de los controles del formulario.
                        ScrollbarOverlapsContent = false,
                        Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding { Horizontal = 60, Top = 40, Bottom = 90 },
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 8),
                        Children = new Drawable[]
                        {
                            heading(@"Mapperatorinator", 32),
                            caption(sourceBeatmap != null
                                ? (addToExistingSet
                                    ? $"new difficulty for: {sourceBeatmap.Metadata.Artist} - {sourceBeatmap.Metadata.Title} (added to the same set)"
                                    : $"song: {sourceBeatmap.Metadata.Artist} - {sourceBeatmap.Metadata.Title}")
                                : $"audio: {Path.GetFileName(externalAudioPath)}"),

                            heading(@"Generator", 22),
                            model = new FormEnumDropdown<MapperatorinatorModel>
                            {
                                Caption = @"Model",
                            },
                            gamemode = new FormEnumDropdown<MapperatorinatorGamemode>
                            {
                                Caption = @"Game mode",
                            },
                            difficulty = new FormSliderBar<double>
                            {
                                Caption = @"Target star rating",
                                Current = new BindableDouble(5) { MinValue = 1, MaxValue = 10, Precision = 0.1 },
                            },
                            year = new FormNumberBox
                            {
                                Caption = @"Mapping style year (optional; range depends on model)",
                                PlaceholderText = @"model default",
                            },
                            mapperId = new FormNumberBox
                            {
                                Caption = @"Mapper id to imitate (osu! user id, optional)",
                                PlaceholderText = @"nobody in particular",
                            },
                            keycount = new FormNumberBox
                            {
                                Caption = @"Key count (mania only)",
                                PlaceholderText = @"4",
                            },
                            seed = new FormNumberBox
                            {
                                Caption = @"Seed (optional, same seed = same map)",
                                PlaceholderText = @"random",
                            },
                            descriptorPickerCaption = caption(@"click a style once to ask for it (green), twice to avoid it (red), again to clear."),
                            descriptorPicker = new DescriptorPicker(),
                            descriptors = new FormTextBox
                            {
                                Caption = @"Style descriptors (comma separated, optional)",
                                PlaceholderText = @"style/clean, skillset/jumps",
                            },
                            negativeDescriptors = new FormTextBox
                            {
                                Caption = @"Styles to avoid (comma separated, optional)",
                                PlaceholderText = @"style/messy",
                            },
                            hitsounds = new FormCheckBox
                            {
                                Caption = @"Generate hitsounds",
                                Current = { Value = true },
                            },
                            superTiming = new FormCheckBox
                            {
                                Caption = @"Super timing (slower, better for variable BPM songs)",
                            },

                            metadataHeading = heading(@"Map info (applied after generation)", 22),
                            metadataCaption = caption(@"these don't affect generation; the finished map comes back with them set."),
                            titleBox = new FormTextBox { Caption = @"Title", PlaceholderText = @"keep generated" },
                            artistBox = new FormTextBox { Caption = @"Artist", PlaceholderText = @"keep generated" },
                            creatorBox = new FormTextBox { Caption = @"Creator", PlaceholderText = @"Mapperatorinator" },
                            tagsBox = new FormTextBox { Caption = @"Tags", PlaceholderText = @"ai generated mapperatorinator" },
                            backgroundSelector = new FormFileSelector(@".jpg", @".jpeg", @".png")
                            {
                                Caption = @"Background image",
                            },

                            setupHeading = heading(@"Setup", 22),
                            setupCaption = caption(@"Mapperatorinator isn't installed yet. One click installs everything (about 8 GB: the tool, python packages and the AI model)."),
                            installButton = new RoundedButton
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 40,
                                Text = @"Install Mapperatorinator automatically",
                                Action = startInstall,
                            },
                            installSelector = new FormFileSelector
                            {
                                Caption = @"Or point to an existing install (its inference.py)",
                            },
                            etaText = new OsuSpriteText { Font = OsuFont.Default.With(size: 16) },

                            generateButton = new RoundedButton
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 50,
                                Text = @"Generate",
                                Action = startGeneration,
                            },

                            logScroll = new OsuScrollContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 240,
                                Alpha = 0,
                                // el boton de back de la pantalla flota abajo a la
                                // izquierda; sin este margen se dibuja arriba del log.
                                Margin = new MarginPadding { Bottom = 50 },
                                Child = logFlow = new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(Typeface.Inter, 13))
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                },
                            },
                        },
                        },
                    },
                },
            };

            if (!string.IsNullOrEmpty(runner.Config.InstallPath) && Directory.Exists(runner.Config.InstallPath))
            {
                // FormFileSelector trabaja con archivos; el ancla visible es inference.py.
                string anchor = Path.Combine(runner.Config.InstallPath, @"inference.py");
                if (File.Exists(anchor))
                    installSelector.Current.Value = new FileInfo(anchor);
            }

            model.Current.BindValueChanged(_ => applyModelCapabilities());
            gamemode.Current.BindValueChanged(_ => applyModelCapabilities());
            installSelector.Current.BindValueChanged(f =>
            {
                if (f.NewValue == null) return;

                string chosen = f.NewValue.FullName;
                runner.Config.InstallPath = Directory.Exists(chosen) ? chosen : Path.GetDirectoryName(chosen);
                runner.Save();
                updateSetupVisibility();
            });

            applyModelCapabilities();
            updateSetupVisibility();
            updateIdleEta();

            if (addToExistingSet)
            {
                // la metadata es por-set y la diff nueva la hereda; mostrarla aca
                // solo confundiria.
                metadataHeading.Alpha = 0;
                metadataCaption.Alpha = 0;
                titleBox.Alpha = 0;
                artistBox.Alpha = 0;
                creatorBox.Alpha = 0;
                tagsBox.Alpha = 0;
                backgroundSelector.Alpha = 0;

                if (sourceBeatmap != null && generationManager?.ReadSidecar(sourceBeatmap) is MapperatorinatorSidecar sidecar)
                    prefillFromSidecar(sidecar);
            }
        }

        /// <summary>
        /// Preloads the form with the settings the original generation used, so
        /// "tweak settings" starts from what actually produced this map.
        /// </summary>
        private void prefillFromSidecar(MapperatorinatorSidecar sidecar)
        {
            var request = sidecar.ToRequest();

            model.Current.Value = request.Model;
            gamemode.Current.Value = request.Gamemode;
            if (request.Difficulty != null)
                difficulty.Current.Value = request.Difficulty.Value;
            year.Current.Value = request.Year?.ToString() ?? string.Empty;
            mapperId.Current.Value = request.MapperId?.ToString() ?? string.Empty;
            keycount.Current.Value = request.Keycount?.ToString() ?? string.Empty;
            hitsounds.Current.Value = request.Hitsounded;
            superTiming.Current.Value = request.SuperTiming;

            descriptorPicker.SetStates(request.Descriptors, request.NegativeDescriptors);
            descriptors.Current.Value = string.Join(@", ", request.Descriptors);
            negativeDescriptors.Current.Value = string.Join(@", ", request.NegativeDescriptors);

            applyModelCapabilities();
        }

        private Drawable setupHeading = null!;
        private Drawable setupCaption = null!;
        private RoundedButton installButton = null!;
        private Drawable metadataHeading = null!;
        private Drawable metadataCaption = null!;

        /// <summary>
        /// Shows only what the selected model actually understands, and clamps year to
        /// its trained range. Source: the capability table next to the model enum.
        /// </summary>
        private void applyModelCapabilities()
        {
            var m = model.Current.Value;

            year.Alpha = m.SupportsYear() ? 1 : 0;
            mapperId.Alpha = m.SupportsMapperId() ? 1 : 0;

            // la familia v32 usa el vocabulario user_tags, que es el que tienen los
            // chips; los modelos viejos usan el set omdb, que va por texto libre.
            bool esV32 = m is MapperatorinatorModel.V32 or MapperatorinatorModel.V32Mini;
            descriptorPicker.Alpha = m.SupportsDescriptors() && esV32 ? 1 : 0;
            descriptorPickerCaption.Alpha = descriptorPicker.Alpha;
            descriptorPicker.GamemodeId = (int)gamemode.Current.Value;
            descriptors.Alpha = m.SupportsDescriptors() && !esV32 ? 1 : 0;
            negativeDescriptors.Alpha = descriptors.Alpha;
            hitsounds.Alpha = m.SupportsHitsoundsToggle() ? 1 : 0;

            // v30 solo sabe osu! standard
            if (!m.SupportsGamemode(gamemode.Current.Value))
                gamemode.Current.Value = MapperatorinatorGamemode.Osu;

            keycount.Alpha = gamemode.Current.Value == MapperatorinatorGamemode.Mania ? 1 : 0;
        }

        private void updateSetupVisibility()
        {
            bool installed = runner.InstallLooksValid;

            setupHeading.Alpha = installed ? 0 : 1;
            setupCaption.Alpha = installed ? 0 : 1;
            installButton.Alpha = installed ? 0 : 1;
            installSelector.Alpha = installed ? 0 : 1;
            generateButton.Enabled.Value = installed;
        }

        private void startInstall()
        {
            if (installing) return;

            installing = true;
            installButton.Enabled.Value = false;
            installButton.Text = @"Installing... (watch the log below)";
            logFlow.Clear();
            logLines = 0;
            logScroll.FadeIn(200);
            installCancellation = new CancellationTokenSource();
            var token = installCancellation.Token;

            Task.Run(async () =>
            {
                try
                {
                    await runner.InstallAsync(line => Schedule(() => appendLog(line)), token).ConfigureAwait(false);
                    Schedule(() =>
                    {
                        appendLog(@"ready to generate!");
                        notifications?.Post(new SimpleNotification { Text = @"Mapperatorinator installed! You can generate maps now." });
                    });
                }
                catch (OperationCanceledException)
                {
                    Schedule(() => appendLog(@"install cancelled."));
                }
                catch (Exception e)
                {
                    Logger.Log($"[mapperatorinator] install failed: {e}");
                    Schedule(() => appendLog($"install failed: {e.Message}"));
                }
                finally
                {
                    Schedule(() =>
                    {
                        installing = false;
                        installButton.Enabled.Value = true;
                        installButton.Text = @"Install Mapperatorinator automatically";
                        updateSetupVisibility();
                        updateIdleEta();
                    });
                }
            }, CancellationToken.None);
        }

        private Drawable heading(string text, int size) => new OsuSpriteText
        {
            Text = text,
            Font = OsuFont.GetFont(size: size, weight: FontWeight.SemiBold),
            Margin = new MarginPadding { Top = size > 24 ? 0 : 18, Bottom = 4 },
        };

        private Drawable caption(string text) => new OsuSpriteText
        {
            Text = text,
            Font = OsuFont.Default.With(size: 14),
            Colour = Colour4.White.Opacity(0.6f),
            Margin = new MarginPadding { Bottom = 6 },
        };

        private void updateIdleEta()
        {
            currentEstimate = runner.Estimate(audioLengthSeconds, device);
            etaText.Text = $"device: {device} | estimated time: ~{format(currentEstimate)}"
                           + (device == @"cpu" ? @"  (no CUDA gpu found; this will be slow)" : string.Empty);
        }

        private static string format(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{(int)t.TotalMinutes}m {t.Seconds:00}s";

        private void startGeneration()
        {
            if (installing) return;

            if (!runner.InstallLooksValid)
            {
                notifications?.Post(new SimpleErrorNotification
                {
                    Text = @"Point the setup at your Mapperatorinator folder first (it needs inference.py inside).",
                });
                return;
            }

            // validaciones baratas antes de gastar minutos de gpu. el rango de anio
            // depende del modelo (2023 para v28-v31, 2024 para la familia v32), y un
            // valor fuera de rango se recorta al borde en vez de frenar todo.
            var selectedModel = model.Current.Value;
            int? yearValue = selectedModel.SupportsYear() ? parseIntOrNull(year.Current.Value) : null;

            if (yearValue != null)
            {
                int clamped = Math.Clamp(yearValue.Value, MapperatorinatorModelCapabilities.MIN_YEAR, selectedModel.MaxYear());
                if (clamped != yearValue.Value)
                {
                    notifications?.Post(new SimpleNotification { Text = $"Year adjusted to {clamped} ({selectedModel.ConfigName()} supports {MapperatorinatorModelCapabilities.MIN_YEAR}-{selectedModel.MaxYear()})." });
                    year.Current.Value = clamped.ToString();
                }

                yearValue = clamped;
            }

            var request = new MapperatorinatorRequest
            {
                Model = selectedModel,
                WorkDirectory = MapperatorinatorGenerationManager.NewWorkDirectory(),
                Gamemode = gamemode.Current.Value,
                Difficulty = difficulty.Current.Value,
                Year = yearValue,
                MapperId = selectedModel.SupportsMapperId() ? parseIntOrNull(mapperId.Current.Value) : null,
                Seed = parseIntOrNull(seed.Current.Value),
                Keycount = parseIntOrNull(keycount.Current.Value) is int k ? Math.Clamp(k, 1, 10) : null,
                Hitsounded = !selectedModel.SupportsHitsoundsToggle() || hitsounds.Current.Value,
                SuperTiming = superTiming.Current.Value,
            };

            if (selectedModel.SupportsDescriptors())
            {
                if (selectedModel is MapperatorinatorModel.V32 or MapperatorinatorModel.V32Mini)
                {
                    request.Descriptors.AddRange(descriptorPicker.Wanted);
                    request.NegativeDescriptors.AddRange(descriptorPicker.Avoided);
                }
                else
                {
                    request.Descriptors.AddRange(splitList(descriptors.Current.Value));
                    request.NegativeDescriptors.AddRange(splitList(negativeDescriptors.Current.Value));
                }
            }

            var overrides = new OszPostProcessor.MetadataOverrides
            {
                Title = emptyToNull(titleBox.Current.Value),
                Artist = emptyToNull(artistBox.Current.Value),
                Creator = emptyToNull(creatorBox.Current.Value),
                Tags = emptyToNull(tagsBox.Current.Value),
                BackgroundImagePath = backgroundSelector.Current.Value?.FullName,
            };

            if (generationManager == null)
                return;

            MapperatorinatorJob job;

            try
            {
                // el manager resuelve el audio y (en modo add-to-set) el snapshot del
                // set en el update thread; de aca en adelante todo corre de fondo.
                job = externalAudioPath != null
                    ? generationManager.CreateJobFromExternalAudio(externalAudioPath, request, overrides)
                    : generationManager.CreateJobFromBeatmap(sourceBeatmap!, request, overrides, addToExistingSet);
            }
            catch (Exception e)
            {
                notifications?.Post(new SimpleErrorNotification { Text = e.Message });
                return;
            }

            // el feed de discord solo se entera si el mapa salio con identidad propia
            // (en modo add-to-set el manager reemplaza los overrides por los del set,
            // asi que esto queda false solo, que es lo que corresponde).
            job.AnnounceToFeed = overrides.Title != null && overrides.Artist != null && overrides.BackgroundImagePath != null;

            generationManager.Enqueue(job);

            // la pantalla ya no hace falta: el progreso vive en la notificacion
            // pinneada y al terminar el click te lleva al mapa.
            this.Exit();
        }

        private void appendLog(string line)
        {
            // el log de una corrida larga puede ser enorme; con las ultimas lineas alcanza
            if (++logLines > max_log_lines)
            {
                logFlow.Clear();
                logFlow.AddParagraph(@"[... older lines trimmed ...]");
                logLines = 1;
            }

            logFlow.AddParagraph(line);

            // el parrafo recien agregado se mide en el proximo frame; scrollear recien
            // despues de los hijos o el "end" queda una linea corto.
            SchedulerAfterChildren.AddOnce(scrollLogToEnd);
        }

        private void scrollLogToEnd() => logScroll.ScrollToEnd();

        private static int? parseIntOrNull(string? s) => int.TryParse(s, out int v) ? v : null;

        private static string? emptyToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private static IEnumerable<string> splitList(string? s) =>
            string.IsNullOrWhiteSpace(s)
                ? Enumerable.Empty<string>()
                : s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        public override bool OnExiting(ScreenExitEvent e)
        {
            // una instalacion a medias no puede quedar corriendo huerfana; las
            // generaciones viven en el manager y siguen solas.
            installCancellation?.Cancel();
            return base.OnExiting(e);
        }
    }
}
