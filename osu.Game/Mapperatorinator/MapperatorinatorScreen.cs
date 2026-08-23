// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework;
using osu.Framework.Graphics.Sprites;
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
using osu.Game.Configuration;
using osu.Game.Online.API.Requests;
using osu.Game.Graphics.Containers;

using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
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
    public partial class MapperatorinatorScreen : OsuScreen, IHandleDroppedFile
    {
        private readonly BeatmapInfo? sourceBeatmap;
        private string? externalAudioPath;
        private readonly bool addToExistingSet;

        // los controles de formulario (el selector de archivos, sobre todo) piden un
        // OverlayColourProvider. Abriendo la pantalla desde song select venia de arriba;
        // desde el menu principal no hay ninguno y la pantalla se caia al abrirse.
        [Cached]
        private OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Pink);

        [Resolved(canBeNull: true)]
        private INotificationOverlay? notifications { get; set; }

        [Resolved(canBeNull: true)]
        private IDialogOverlay? dialogOverlay { get; set; }

        [Resolved(canBeNull: true)]
        private MapperatorinatorGenerationManager? generationManager { get; set; }

        [Resolved(canBeNull: true)]
        private OsuGame? game { get; set; }

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private MapperatorinatorRunner runner = null!;

        // paso 1: opciones del generador
        private FormEnumDropdown<MapperatorinatorModel> model = null!;
        private FormEnumDropdown<MapperatorinatorGamemode> gamemode = null!;
        private FormSliderBar<double> difficulty = null!;
        private FormNumberBox year = null!;
        private FormNumberBox mapperId = null!;
        private FormNumberBox keycount = null!;
        private FormNumberBox seed = null!;
        private FormNumberBox circleSize = null!;
        private FormNumberBox approachRate = null!;
        private FormNumberBox overallDifficulty = null!;
        private FormNumberBox hpDrain = null!;
        private Drawable difficultySettingsCaption = null!;
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
        private Drawable presetHeading = null!;
        private OsuSpriteText presetCaption = null!;
        private FormDropdown<PresetEntry> presetDropdown = null!;
        private FormTextBox presetName = null!;
        private RoundedButton presetSaveButton = null!;
        private RoundedButton presetDeleteButton = null!;
        private RoundedButton presetManageButton = null!;
        private bool applyingPreset;

        /// <summary>Abierta para mirar como se hizo un mapa, no para generar otro.</summary>
        private readonly bool reviewOnly;

        /// <summary>Abierta sin cancion: primero hay que elegir un audio.</summary>
        private readonly bool standalone;

        private FormFileSelector audioSelector = null!;
        private OsuSpriteText audioHint = null!;
        private RoundedButton openReportButton = null!;
        private OsuScrollContainer logScroll = null!;

        private bool installing;
        private CancellationTokenSource? installCancellation;
        private string device = @"cpu";
        private string detectedDevice = @"cpu";
        private TimeSpan currentEstimate;
        private int logLines;

        private const int max_log_lines = 400;

        // capturado en load(): BeatmapInfo es un objeto de realm atado al update
        // thread, asi que NADA del task de generacion puede volver a tocarlo.
        private double audioLengthSeconds = 180;

        public MapperatorinatorScreen(BeatmapInfo beatmap, bool addToExistingSet = false, bool reviewOnly = false)
        {
            sourceBeatmap = beatmap;
            this.addToExistingSet = addToExistingSet;
            this.reviewOnly = reviewOnly;
        }

        public MapperatorinatorScreen(string audioPath)
        {
            externalAudioPath = audioPath;
        }

        /// <summary>Desde el menu principal: todavia no hay cancion, se elige aca.</summary>
        public MapperatorinatorScreen()
        {
            standalone = true;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours, Storage storage)
        {
            // el runner del manager es la unica fuente de verdad de config/install;
            // instanciar uno propio solo si no hay manager (test scenes).
            runner = generationManager?.Runner ?? new MapperatorinatorRunner(storage.GetFullPath(string.Empty));
            detectedDevice = runner.DetectDevice();
            device = runner.EffectiveDevice(detectedDevice);
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
                            credits(),

                            audioSelector = new FormFileSelector(@".mp3", @".ogg", @".wav", @".flac")
                            {
                                Caption = @"Song",
                                PlaceholderText = @"drop an audio file anywhere on this screen, or click to pick one",
                            },
                            audioHint = caption(@"any mp3, ogg, wav or flac. the map gets built from that audio."),

                            presetHeading = heading(@"Presets", 22),
                            presetCaption = caption(@"settings you saved before. they live on your account, so reinstalling doesn't lose them."),
                            presetDropdown = new FormDropdown<PresetEntry>
                            {
                                Caption = @"Use a preset",
                                HintText = @"Picking one fills in everything below with the settings it was saved with.",
                                Items = new[] { PresetEntry.None },
                                NewFeatureId = NewFeatureRegistry.MapperatorinatorPresets,
                            },
                            presetName = new FormTextBox
                            {
                                Caption = @"Preset name",
                                HintText = @"A name you'll recognise, like ""stream farm 6 stars"". Reusing a name overwrites that preset.",
                                PlaceholderText = @"name it to save the settings below",
                                TabbableContentContainer = this,
                            },
                            presetSaveButton = new RoundedButton
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 36,
                                Text = @"Save these settings as a preset",
                                Action = savePreset,
                            },
                            presetDeleteButton = new RoundedButton
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 32,
                                Text = @"Delete the preset above",
                                Action = deletePreset,
                            },
                            presetManageButton = new RoundedButton
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 32,
                                Text = @"Manage all my presets",
                                Action = () => this.Push(new MapperatorinatorPresetsScreen()),
                            },
                            caption(sourceBeatmap != null
                                ? (addToExistingSet
                                    ? $"new difficulty for: {sourceBeatmap.Metadata.Artist} - {sourceBeatmap.Metadata.Title} (added to the same set)"
                                    : $"song: {sourceBeatmap.Metadata.Artist} - {sourceBeatmap.Metadata.Title}")
                                : standalone
                                    ? @"pick a song below and the model writes a map for it."
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
                            difficultySettingsCaption = caption(@"leave these empty and the model picks whatever fits the style it's generating."),
                            circleSize = new FormNumberBox(allowDecimals: true)
                            {
                                Caption = @"Circle size (CS, 0-10, optional)",
                                PlaceholderText = @"model decides",
                            },
                            approachRate = new FormNumberBox(allowDecimals: true)
                            {
                                Caption = @"Approach rate (AR, 0-10, optional)",
                                PlaceholderText = @"model decides",
                            },
                            overallDifficulty = new FormNumberBox(allowDecimals: true)
                            {
                                Caption = @"Overall difficulty (OD, 0-10, optional)",
                                PlaceholderText = @"model decides",
                            },
                            hpDrain = new FormNumberBox(allowDecimals: true)
                            {
                                Caption = @"HP drain (HP, 0-10, optional)",
                                PlaceholderText = @"model decides",
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

                            requirementsHeading = heading(@"Requirements", 22),
                            requirementsCaption = caption(@"everything generating needs on this machine. sort out anything red, then press Check."),
                            requirementsFlow = new FillFlowContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                AutoSizeAxes = Axes.Y,
                                Direction = FillDirection.Vertical,
                                Spacing = new Vector2(0, 6),
                            },
                            checkButton = new RoundedButton
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 36,
                                Text = @"Check again",
                                Action = runChecks,
                            },
                            installSelector = new FormFileSelector
                            {
                                Caption = @"Advanced: point to an existing Mapperatorinator install (its inference.py)",
                            },
                            etaText = new OsuSpriteText { Font = OsuFont.Default.With(size: 16) },

                            generateButton = new RoundedButton
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 50,
                                Text = @"Generate",
                                Action = startGeneration,
                            },

                            openReportButton = new RoundedButton
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 32,
                                Alpha = 0,
                                Text = @"Open the log file",
                                Action = () =>
                                {
                                    if (lastReportPath != null)
                                        openReport(lastReportPath);
                                },
                            },
                            logScroll = new OsuScrollContainer
                            {
                                RelativeSizeAxes = Axes.X,
                                Height = 340,
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
                runChecks();
            });

            // el mapa sale a tu nombre por default (podes cambiarlo igual), y si la
            // cancion viene de un mapa que ya tenias, hereda su titulo y artista en vez
            // de terminar como "Unknown Title".
            if (api.LocalUser.Value is APIUser localUser && localUser.Id > 1)
                creatorBox.Current.Value = localUser.Username;

            if (sourceBeatmap != null)
            {
                titleBox.Current.Value = sourceBeatmap.Metadata.Title;
                artistBox.Current.Value = sourceBeatmap.Metadata.Artist;
            }

            applyModelCapabilities();
            updateIdleEta();
            runChecks();

            // el selector de cancion solo existe cuando se entro sin mapa de origen.
            audioSelector.Alpha = standalone ? 1 : 0;
            audioHint.Alpha = audioSelector.Alpha;
            audioSelector.Current.BindValueChanged(f =>
            {
                if (f.NewValue == null)
                    return;

                externalAudioPath = f.NewValue.FullName;
                audioLengthSeconds = 180;
                updateIdleEta();
                updateGenerateEnabled();
            });

            presetDropdown.Current.BindValueChanged(e => onPresetSelected(e.NewValue ?? PresetEntry.None));

            // se guarda cuando la persona lo pide, no por pasar por el campo: el commit
            // de un textbox salta tambien al salir del foco, asi que clickear de un campo
            // a otro estaba disparando un guardado (y un cartel de error) sin que nadie
            // lo pidiera. El boton manda; enter es solo un atajo.
            presetName.Current.BindValueChanged(e => presetSaveButton.Enabled.Value = !string.IsNullOrWhiteSpace(e.NewValue), true);
            presetName.OnCommit += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(presetName.Current.Value))
                    savePreset();
            };

            presetDeleteButton.Alpha = 0;
            loadPresets();

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

            if (reviewOnly)
            {
                // el formulario ya quedo con las opciones del mapa: lo que sirve aca es
                // leerlas y guardarlas, no generar otra cosa encima.
                generateButton.Alpha = 0;
                etaText.Alpha = 0;
                presetCaption.Text = @"these are the settings this map was generated with. name them and they're yours to use on any other song.";

                if (sourceBeatmap != null)
                    presetName.Current.Value = $"{sourceBeatmap.Metadata.Title} style";
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
            circleSize.Current.Value = stat(request.CircleSize);
            approachRate.Current.Value = stat(request.ApproachRate);
            overallDifficulty.Current.Value = stat(request.OverallDifficulty);
            hpDrain.Current.Value = stat(request.HpDrainRate);
            hitsounds.Current.Value = request.Hitsounded;
            superTiming.Current.Value = request.SuperTiming;

            descriptorPicker.SetStates(request.Descriptors, request.NegativeDescriptors);
            descriptors.Current.Value = string.Join(@", ", request.Descriptors);
            negativeDescriptors.Current.Value = string.Join(@", ", request.NegativeDescriptors);

            applyModelCapabilities();

            static string stat(double? value) => value?.ToString(@"0.#", CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private Drawable requirementsHeading = null!;
        private OsuSpriteText requirementsCaption = null!;
        private FillFlowContainer requirementsFlow = null!;
        private RoundedButton checkButton = null!;

        /// <summary>Whether the last check found everything generation needs.</summary>
        private bool ready;
        private bool checking;
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

            bool esMania = gamemode.Current.Value == MapperatorinatorGamemode.Mania;
            keycount.Alpha = esMania ? 1 : 0;

            // en mania la "circle size" es la cantidad de teclas (va por keycount) y en
            // taiko no existe; en el resto sirve igual que en el editor.
            circleSize.Alpha = esMania || gamemode.Current.Value == MapperatorinatorGamemode.Taiko ? 0 : 1;

            // taiko y mania no tienen approach rate.
            approachRate.Alpha = esMania || gamemode.Current.Value == MapperatorinatorGamemode.Taiko ? 0 : 1;
        }

        /// <summary>
        /// Re-runs every requirement probe off the update thread and redraws the list.
        /// Cheap to call often: after an install, after the user presses Check, on load.
        /// </summary>
        private void runChecks()
        {
            if (checking) return;

            checking = true;
            checkButton.Enabled.Value = false;
            generateButton.Enabled.Value = false;

            Task.Run(() =>
            {
                List<Requirement> results;

                try
                {
                    results = MapperatorinatorReadiness.Check(runner);
                }
                catch (Exception e)
                {
                    Logger.Log($"[mapperatorinator] readiness check failed: {e}");
                    results = new List<Requirement>
                    {
                        new Requirement { Kind = RequirementKind.Tool, Title = @"Requirements check", State = RequirementState.Missing, Detail = $"the check itself failed: {e.Message}" },
                    };
                }

                Schedule(() =>
                {
                    checking = false;
                    checkButton.Enabled.Value = true;
                    applyReadiness(results);
                });
            });
        }

        private void applyReadiness(List<Requirement> results)
        {
            ready = results.All(r => r.Satisfied);

            requirementsFlow.Clear();

            foreach (var r in results)
            {
                requirementsFlow.Add(new RequirementRow(r)
                {
                    AutoInstall = r.Kind switch
                    {
                        // instalado pero con el torch equivocado: solo se cambia pytorch,
                        // no se rebaja todo de nuevo.
                        RequirementKind.Tool => r.State == RequirementState.Warning ? startTorchFix : startInstall,
                        RequirementKind.Ffmpeg => startFfmpegInstall,
                        // probar / reintentar / volver a la cpu, segun donde estemos parados.
                        RequirementKind.Platform => runner.Config.RocmEnabled ? backToCpu : tryGpu,
                        _ => null,
                    },
                    OpenDownload = r.DownloadUrl == null ? null : () => game?.OpenUrlExternally(r.DownloadUrl),
                    // el reporte esta SIEMPRE que haya una placa amd y la tool instalada,
                    // incluso (sobre todo) despues de que algo falle: es lo que se manda
                    // para pedir ayuda.
                    Secondary = r.Kind == RequirementKind.Platform && r.AlwaysActionable ? (@"GPU report", (Action)gpuReport) : null,
                });
            }

            // con todo verde la lista estorba, PERO un warning con arreglo (el pytorch que
            // ignora la gpu) tiene que quedar a la vista: si no, el aviso dice "mira los
            // requisitos" y no hay requisitos que mirar.
            bool anythingToDo = results.Any(r => r.Actionable);
            bool showList = !ready || anythingToDo;

            requirementsCaption.Text = ready && anythingToDo
                ? @"generating works, but something here would make it a lot faster."
                : @"everything generating needs on this machine. sort out anything red, then press Check.";

            requirementsHeading.Alpha = showList ? 1 : 0;
            requirementsCaption.Alpha = showList ? 1 : 0;
            requirementsFlow.Alpha = showList ? 1 : 0;
            checkButton.Alpha = showList ? 1 : 0;
            installSelector.Alpha = showList ? 1 : 0;

            updateGenerateEnabled();

            detectedDevice = runner.DetectDevice();
            device = runner.EffectiveDevice(detectedDevice);
            updateIdleEta();
        }

        private void startFfmpegInstall()
        {
            if (installing) return;

            installing = true;
            generateButton.Enabled.Value = false;
            logFlow.Clear();
            logLines = 0;
            logScroll.FadeIn(200);
            installCancellation = new CancellationTokenSource();
            var token = installCancellation.Token;

            Task.Run(async () =>
            {
                try
                {
                    await runner.InstallFfmpegAsync(line => Schedule(() => appendLog(line)), token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Schedule(() => appendLog(@"ffmpeg install cancelled."));
                }
                catch (Exception e)
                {
                    Logger.Log($"[mapperatorinator] ffmpeg install failed: {e}");
                    Schedule(() => appendLog($"ffmpeg install failed: {e.Message}"));
                }
                finally
                {
                    Schedule(() =>
                    {
                        installing = false;
                        runChecks();
                    });
                }
            }, CancellationToken.None);
        }

        /// <summary>
        /// One requirement as a row: status icon, what it is, what was found, and the
        /// buttons that make sense for it (install for us, download page, instructions).
        /// </summary>
        private partial class RequirementRow : CompositeDrawable
        {
            public Action? AutoInstall;
            public Action? OpenDownload;

            /// <summary>An extra, always-available action for this row (the GPU report).</summary>
            public (string label, Action action)? Secondary;

            private readonly Requirement requirement;

            public RequirementRow(Requirement requirement)
            {
                this.requirement = requirement;
            }

            [BackgroundDependencyLoader]
            private void load(OsuColour colours)
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;

                (IconUsage icon, Colour4 colour) = requirement.State switch
                {
                    RequirementState.Ok => (FontAwesome.Solid.CheckCircle, colours.Lime0),
                    RequirementState.Warning => (FontAwesome.Solid.ExclamationCircle, colours.Orange1),
                    RequirementState.Missing => (FontAwesome.Solid.TimesCircle, colours.Red),
                    RequirementState.Unsupported => (FontAwesome.Solid.Ban, colours.Red),
                    _ => (FontAwesome.Regular.Circle, colours.Gray6),
                };

                var buttons = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(6, 0),
                    Margin = new MarginPadding { Top = 6 },
                };

                bool actionable = requirement.Actionable;

                if (actionable && requirement.CanAutoInstall && AutoInstall != null)
                    buttons.Add(new RoundedButton { Width = 190, Height = 30, Text = requirement.AutoInstallLabel, Action = AutoInstall });

                if (actionable && OpenDownload != null)
                    buttons.Add(new RoundedButton { Width = 150, Height = 30, Text = requirement.DownloadLabel, Action = OpenDownload });

                if (Secondary != null)
                    buttons.Add(new RoundedButton { Width = 150, Height = 30, Text = Secondary.Value.label, Action = Secondary.Value.action });

                var text = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding { Left = 30 },
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = requirement.Title,
                            Font = OsuFont.GetFont(size: 16, weight: FontWeight.SemiBold),
                        },
                        new OsuTextFlowContainer(s => s.Font = OsuFont.Default.With(size: 13))
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Colour = Colour4.White.Opacity(0.7f),
                            Text = requirement.Detail,
                        },
                    },
                };

                if (actionable && !string.IsNullOrEmpty(requirement.Instructions) || requirement.State == RequirementState.Unsupported)
                {
                    text.Add(new OsuTextFlowContainer(s => s.Font = OsuFont.Default.With(size: 13))
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Colour = colours.Orange1,
                        Text = requirement.Instructions,
                        Margin = new MarginPadding { Top = 2 },
                    });
                }

                if (buttons.Children.Count > 0)
                    text.Add(buttons);

                InternalChildren = new Drawable[]
                {
                    new SpriteIcon
                    {
                        Icon = icon,
                        Colour = colour,
                        Size = new Vector2(18),
                        Margin = new MarginPadding { Top = 1 },
                    },
                    text,
                };
            }
        }

        /// <summary>
        /// Turns AMD generation on, after saying out loud what can go wrong. The order
        /// matters: everything that can be answered by asking torch (which chip is this
        /// card, which chips does this build carry kernels for) happens before a single
        /// kernel is dispatched, because dispatching to a chip the build doesn't cover is
        /// exactly what faults the card and takes the display driver with it.
        /// </summary>
        private void tryGpu()
        {
            if (installing) return;

            string gpuName = MapperatorinatorRunner.DetectAmdGpu()?.Name ?? @"your AMD GPU";

            dialogOverlay?.Push(new RocmWarningDialog(gpuName, () => runInstallTask(@"gpu-test", async (log, token) =>
            {
                runner.EnableRocm();
                runner.SetRocmOverride(null);

                // desde aca hasta el final se toca la placa. si el cliente no llega a
                // borrar esta marca, es porque se murio con el driver: al proximo arranque
                // se da por fallada y nadie repite el paseo sin querer.
                runner.BeginRocmTrial();

                string? failure = null;

                foreach (var wheel in MapperatorinatorRunner.RocmIndexes())
                {
                    // lo que ya este instalado se prueba tal cual: sondear es gratis y son
                    // varios GB de bajada por intento.
                    if (runner.InstalledTorchDevice != @"rocm" || (runner.Config.RocmIndex != null && runner.Config.RocmIndex != wheel.index))
                        await runner.InstallRocmTorchAsync(wheel, log, token).ConfigureAwait(false);

                    var probe = await runner.ProbeGpuAsync(log, token).ConfigureAwait(false);

                    if (probe.Error != null || probe.DeviceCount == 0)
                    {
                        failure = probe.Error ?? @"pytorch can't see the card at all";
                        continue;
                    }

                    if (!probe.ArchSupported)
                    {
                        // el chip no esta entre los que trae la rueda: apuntarlo al pariente
                        // mas cercano de la misma generacion y volver a preguntar.
                        string? over = probe.Arch == null ? null : MapperatorinatorRunner.OverrideFor(probe.Arch, probe.ArchList);

                        if (over == null)
                        {
                            failure = $"this pytorch has no kernels for {probe.Arch ?? "your card"} (it carries: {string.Join(", ", probe.ArchList)})";
                            log(failure);
                            continue;
                        }

                        log($"{probe.Arch} isn't in this build; pointing it at {over} and asking again...");
                        runner.SetRocmOverride(over);
                        probe = await runner.ProbeGpuAsync(log, token).ConfigureAwait(false);

                        if (!probe.ArchSupported)
                        {
                            failure = $"even pointed at {over}, this build can't run on {probe.Arch ?? "your card"}";
                            log(failure);
                            runner.SetRocmOverride(null);
                            continue;
                        }
                    }

                    // recien aca se le manda trabajo de verdad a la placa.
                    if (await runner.SmokeTestGpuAsync(log, token).ConfigureAwait(false))
                    {
                        runner.EndRocmTrial();
                        Schedule(() => notifications?.Post(new SimpleNotification { Text = @"GPU generation is on: your card passed the test. The next map should be a lot faster." }));
                        return;
                    }

                    failure = @"the card faulted on a two-second test multiply";
                }

                runner.EndRocmTrial();
                runner.MarkRocmBlocked(failure);
                Schedule(() =>
                {
                    log(@"staying on the CPU. Nothing else changed, generating still works.");
                    notifications?.Post(new SimpleNotification { Text = $"Your GPU can't be used for generating ({failure}). Mapperatorinator keeps running on the CPU." });
                });
            }), () => { }));
        }

        /// <summary>Turn the GPU back off. No drama, no test: straight back to the CPU.</summary>
        private void backToCpu()
        {
            runner.DisableRocm();
            notifications?.Post(new SimpleNotification { Text = @"Generation is back on the CPU." });
            runChecks();
        }

        /// <summary>Everything torch knows about the card, printed to the log. Dispatches nothing.</summary>
        private void gpuReport() => runInstallTask(@"gpu-report", async (log, token) =>
        {
            // leer las propiedades igual inicializa HIP: si eso mata al cliente, que la
            // proxima sesion lo sepa en vez de intentar generar en la placa.
            runner.BeginRocmTrial();
            log($"torii {game?.Version}, {RuntimeInfo.OS}");

            var amd = MapperatorinatorRunner.DetectAmdGpu();
            if (amd != null)
                log($"card seen by the driver: {amd.Name} ({amd.Gfx}), /dev/kfd readable: {amd.KfdAccessible}");

            log($"generation currently runs on: {runner.EffectiveDevice()}");

            if (runner.Config.RocmLastError != null)
                log($"last GPU failure: {runner.Config.RocmLastError}");

            var probe = await runner.ProbeGpuAsync(log, token).ConfigureAwait(false);

            runner.EndRocmTrial();

            log(probe.ArchSupported
                ? @"this pytorch does carry kernels for your card."
                : @"this pytorch does NOT carry kernels for your card: that's the mismatch that faults the GPU.");
        });

        /// <summary>
        /// Shared plumbing for the long jobs: one at a time, cancellable, and everything
        /// they print ends up in a file. The panel below is for watching; the file is what
        /// you can actually read afterwards and send to someone.
        /// </summary>
        private void runInstallTask(string reportName, Func<Action<string>, CancellationToken, Task> work)
        {
            if (installing) return;

            installing = true;
            generateButton.Enabled.Value = false;
            logFlow.Clear();
            logLines = 0;
            logScroll.FadeIn(200);
            installCancellation = new CancellationTokenSource();
            var token = installCancellation.Token;

            var collected = new List<string>();

            void log(string line)
            {
                lock (collected)
                    collected.Add(line);

                Schedule(() => appendLog(line));
            }

            Task.Run(async () =>
            {
                try
                {
                    await work(log, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    log(@"cancelled.");
                }
                catch (Exception e)
                {
                    Logger.Log($"[mapperatorinator] gpu task failed: {e}");
                    log($"failed: {e.Message}");
                }
                finally
                {
                    string[] lines;
                    lock (collected)
                        lines = collected.ToArray();

                    string? path = runner.WriteReport(reportName, lines);

                    Schedule(() =>
                    {
                        installing = false;
                        lastReportPath = path;
                        openReportButton.Alpha = path != null ? 1 : 0;
                        runChecks();

                        if (path != null)
                            postReportNotification(path);
                    });
                }
            }, CancellationToken.None);
        }

        private string? lastReportPath;

        /// <summary>The report is only useful if it can be opened and sent; say where it is.</summary>
        private void postReportNotification(string path)
        {
            notifications?.Post(new SimpleNotification
            {
                Text = $"Saved to {Path.GetFileName(path)} — click to open it (attach it if you're asking for help).",
                Icon = FontAwesome.Solid.FileAlt,
                Activated = () =>
                {
                    openReport(path);
                    return true;
                },
            });
        }

        private void openReport(string path)
        {
            try
            {
                host.OpenFileExternally(path);
            }
            catch (Exception e)
            {
                Logger.Log($"[mapperatorinator] couldn't open the report: {e.Message}");
                host.PresentFileExternally(path);
            }
        }

        /// <summary>Only swaps pytorch for the GPU build: minutes instead of a full reinstall.</summary>
        private void startTorchFix() => runInstallTask(@"torch-fix", async (log, token) =>
        {
            await runner.ReinstallTorchAsync(log, token).ConfigureAwait(false);
            Schedule(() => notifications?.Post(new SimpleNotification { Text = @"Mapperatorinator now uses your GPU. The next generation should be a lot faster." }));
        });

        private void startInstall()
        {
            if (installing) return;

            installing = true;
            generateButton.Enabled.Value = false;
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
                        runChecks();
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

        /// <summary>
        /// Whose work this is. The model and the tool are OliBomby's, MIT licensed; we
        /// only drive them. Costs one line and it's the least the project deserves.
        /// </summary>
        private Drawable credits()
        {
            var flow = new LinkFlowContainer(s => s.Font = OsuFont.Default.With(size: 14))
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Colour = Colour4.White.Opacity(0.6f),
                Margin = new MarginPadding { Bottom = 6 },
            };

            flow.AddText(@"model and generator by ");
            flow.AddLink(@"OliBomby's Mapperatorinator", @"https://github.com/OliBomby/Mapperatorinator");
            flow.AddText(@" (MIT). it runs on your machine, nothing is sent anywhere.");

            return flow;
        }

        private OsuSpriteText caption(string text) => new OsuSpriteText
        {
            Text = text,
            Font = OsuFont.Default.With(size: 14),
            Colour = Colour4.White.Opacity(0.6f),
            Margin = new MarginPadding { Bottom = 6 },
        };

        private void updateIdleEta()
        {
            currentEstimate = runner.Estimate(audioLengthSeconds, device);
            string hardware = device switch
            {
                @"cuda" => @"NVIDIA GPU (CUDA)",
                @"rocm" => @"AMD GPU (ROCm)",
                @"mps" => @"Apple Silicon (MPS, slower than NVIDIA)",
                _ => detectedDevice != @"cpu"
                    ? @"CPU (the GPU can't be used until pytorch is reinstalled, see the requirements)"
                    // la placa amd existe pero la generacion no la usa: decirlo, no fingir
                    // que no hay ninguna.
                    : MapperatorinatorRunner.DetectAmdGpu() is AmdGpuInfo card
                        ? (card.WindowsWithoutRocm
                            ? $"CPU ({card.Name} can't be used: pytorch has no ROCm build for Windows)"
                            : @"CPU (your AMD GPU is off for generation, see the requirements)")
                        : @"CPU only (no supported GPU found, this will be slow)",
            };

            etaText.Text = $"{hardware} | estimated time: ~{format(currentEstimate)}";
        }

        private static string format(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : $"{(int)t.TotalMinutes}m {t.Seconds:00}s";

        /// <summary>Sin cancion no se puede generar nada, por mas que todo lo demas este.</summary>
        private void updateGenerateEnabled()
        {
            bool hasSong = sourceBeatmap != null || !string.IsNullOrEmpty(externalAudioPath);
            generateButton.Enabled.Value = ready && !installing && hasSong;
        }

        /// <summary>
        /// Arrastrar un mp3 encima de la pantalla es la forma natural de decir "usá esta
        /// cancion": el juego, si no, intentaria importarlo como si fuera un mapa.
        /// </summary>
        public bool HandleDroppedFile(string path)
        {
            if (!standalone)
                return false;

            string extension = Path.GetExtension(path).ToLowerInvariant();

            if (extension != @".mp3" && extension != @".ogg" && extension != @".wav" && extension != @".flac")
                return false;

            Schedule(() =>
            {
                audioSelector.Current.Value = new FileInfo(path);
                notifications?.Post(new SimpleNotification { Text = $"Using {Path.GetFileName(path)} as the song." });
            });

            return true;
        }

        private void startGeneration()
        {
            if (installing) return;

            if (!ready)
            {
                notifications?.Post(new SimpleErrorNotification
                {
                    Text = @"Something generation needs is still missing. Sort out the red items in the requirements list first.",
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

            var request = buildRequest(selectedModel, yearValue);

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

            // el feed de discord solo se entera si el mapa salio con identidad propia:
            // titulo puesto a mano, imagen elegida, y algo que lo describa (artista o
            // tags). el que genera por probar y no toca nada no aparece en el feed.
            // en modo add-to-set el manager reemplaza los overrides por los del set,
            // asi que esto queda en false solo, que es lo que corresponde.
            job.AnnounceToFeed = overrides.Title != null
                                 && overrides.BackgroundImagePath != null
                                 && (overrides.Artist != null || overrides.Tags != null);

            generationManager.Enqueue(job);

            // la pantalla ya no hace falta: el progreso vive en la notificacion
            // pinneada y al terminar el click te lleva al mapa.
            this.Exit();
        }

        /// <summary>
        /// Una entrada del desplegable de presets. La primera es "ninguno", que existe
        /// para poder volver atras sin tener que recargar la pantalla.
        /// </summary>
        public class PresetEntry
        {
            public static readonly PresetEntry None = new PresetEntry(0, @"(none)", string.Empty);

            public int Id { get; }
            public string Name { get; }
            public string Settings { get; }

            public PresetEntry(int id, string name, string settings)
            {
                Id = id;
                Name = name;
                Settings = settings;
            }

            public override string ToString() => Name;
        }

        private void loadPresets(int? selectId = null)
        {
            if (api.LocalUser.Value.Id <= 1)
            {
                // sin cuenta no hay donde guardarlos; la seccion no tiene sentido.
                setPresetSectionVisible(false);
                return;
            }

            var request = new GetMapperatorinatorPresetsRequest();

            request.Success += response => Schedule(() =>
            {
                var entries = new List<PresetEntry> { PresetEntry.None };
                entries.AddRange(response.Presets.Select(p => new PresetEntry(p.Id, p.Name, p.Settings)));

                applyingPreset = true;
                presetDropdown.Items = entries;
                presetDropdown.Current.Value = entries.FirstOrDefault(e => e.Id == selectId) ?? PresetEntry.None;
                applyingPreset = false;

                updateDeleteButton(presetDropdown.Current.Value);
            });

            request.Failure += e => Schedule(() => Logger.Log($"[mapperatorinator] couldn't load presets: {e.Message}"));

            api.Queue(request);
        }

        private void setPresetSectionVisible(bool visible)
        {
            float alpha = visible ? 1 : 0;
            presetHeading.Alpha = alpha;
            presetCaption.Alpha = alpha;
            presetDropdown.Alpha = alpha;
            presetName.Alpha = alpha;
            presetSaveButton.Alpha = alpha;
            presetManageButton.Alpha = alpha;

            if (!visible)
                presetDeleteButton.Alpha = 0;
        }

        private void updateDeleteButton(PresetEntry entry)
        {
            // sin preset elegido no hay nada que borrar: la fila directamente no esta,
            // en vez de quedar ahi apagada ocupando lugar.
            presetDeleteButton.Alpha = entry.Id > 0 ? 1 : 0;
        }

        private void onPresetSelected(PresetEntry entry)
        {
            updateDeleteButton(entry);

            if (applyingPreset || entry.Id <= 0 || string.IsNullOrEmpty(entry.Settings))
                return;

            var sidecar = MapperatorinatorSidecar.Deserialize(entry.Settings);

            if (sidecar == null)
            {
                notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't read the preset \"{entry.Name}\"." });
                return;
            }

            applyingPreset = true;
            prefillFromSidecar(sidecar);
            applyingPreset = false;

            notifications?.Post(new SimpleNotification { Text = $"Loaded preset \"{entry.Name}\"." });
        }

        private void savePreset()
        {
            // el campo arranca en null, no en vacio: esto era un crash al apretar guardar.
            string name = (presetName.Current.Value ?? string.Empty).Trim();

            // sin nombre no se hace nada y no se dice nada: el boton ya esta apagado, y
            // avisarle a alguien que no escribio un nombre cuando no pidio guardar es
            // molestar por molestar.
            if (name.Length == 0)
                return;

            // el mismo shape que guarda el sidecar de cada mapa generado: asi un preset
            // y "las opciones que uso este mapa" son la misma cosa y se pueden mezclar.
            var request = buildRequest(model.Current.Value, parseIntOrNull(year.Current.Value));
            string settings = MapperatorinatorSidecar.FromRequest(request, customized: true).Serialize();

            var save = new SaveMapperatorinatorPresetRequest(name, settings);

            save.Success += preset => Schedule(() =>
            {
                presetName.Current.Value = string.Empty;
                notifications?.Post(new SimpleNotification { Text = $"Saved preset \"{preset.Name}\"." });
                loadPresets(preset.Id);
            });

            save.Failure += e => Schedule(() => notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't save the preset: {e.Message}" }));

            api.Queue(save);
        }

        private void deletePreset()
        {
            var entry = presetDropdown.Current.Value;

            if (entry.Id <= 0)
                return;

            var request = new DeleteMapperatorinatorPresetRequest(entry.Id);

            request.Success += () => Schedule(() =>
            {
                notifications?.Post(new SimpleNotification { Text = $"Deleted preset \"{entry.Name}\"." });
                loadPresets();
            });

            request.Failure += e => Schedule(() => notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't delete the preset: {e.Message}" }));

            api.Queue(request);
        }

        /// <summary>
        /// Lo que el formulario dice ahora, como pedido de generacion. Sale aparte
        /// porque lo usan dos caminos: generar, y guardar un preset con lo puesto.
        /// </summary>
        private MapperatorinatorRequest buildRequest(MapperatorinatorModel selectedModel, int? yearValue)
        {
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
                CircleSize = circleSize.Alpha > 0 ? parseStatOrNull(circleSize.Current.Value) : null,
                ApproachRate = approachRate.Alpha > 0 ? parseStatOrNull(approachRate.Current.Value) : null,
                OverallDifficulty = parseStatOrNull(overallDifficulty.Current.Value),
                HpDrainRate = parseStatOrNull(hpDrain.Current.Value),
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

            return request;
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

            // si la persona subio a leer algo, no se le arrastra la vista de vuelta abajo
            // en cada linea; eso era lo que hacia imposible leer el final.
            bool follow = logScroll.IsScrolledToEnd(10);

            logFlow.AddParagraph(line);

            // el parrafo recien agregado se mide en el proximo frame; scrollear recien
            // despues de los hijos o el "end" queda una linea corto.
            if (follow)
                SchedulerAfterChildren.AddOnce(scrollLogToEnd);
        }

        private void scrollLogToEnd() => logScroll.ScrollToEnd();

        private static int? parseIntOrNull(string? s) => int.TryParse(s, out int v) ? v : null;

        /// <summary>An AR/CS/OD/HP value clamped to osu!'s legal range, or null if not set.</summary>
        private static double? parseStatOrNull(string? s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? Math.Clamp(v, 0, 10) : null;

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
