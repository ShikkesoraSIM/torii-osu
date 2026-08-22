// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Screens;
using osuTK;

namespace osu.Game.Mapperatorinator
{
    /// <summary>
    /// The "use this song for Mapperatorinator" flow: generator options up top, the
    /// metadata you want on the finished map below (held aside and applied to the
    /// .osz after generation), then a progress view with a device-aware ETA.
    /// </summary>
    public partial class MapperatorinatorScreen : OsuScreen
    {
        public override bool AllowUserExit => !running;

        private readonly BeatmapInfo? sourceBeatmap;
        private readonly string? externalAudioPath;

        [Resolved]
        private BeatmapManager beatmaps { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved(canBeNull: true)]
        private INotificationOverlay? notifications { get; set; }

        private MapperatorinatorRunner runner = null!;

        // paso 1: opciones del generador
        private FormDropdown<int> gamemode = null!;
        private FormSliderBar<double> difficulty = null!;
        private FormNumberBox year = null!;
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
        private FillFlowContainer formFlow = null!;

        private bool running;
        private CancellationTokenSource? cancellation;
        private string device = @"cpu";

        public MapperatorinatorScreen(BeatmapInfo beatmap)
        {
            sourceBeatmap = beatmap;
        }

        public MapperatorinatorScreen(string audioPath)
        {
            externalAudioPath = audioPath;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours, Storage storage)
        {
            runner = new MapperatorinatorRunner(storage.GetFullPath(string.Empty));
            device = runner.DetectDevice();

            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(colours.GreySeaFoamDark, colours.GreySeaFoamDarker),
                },
                new OsuScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Padding = new MarginPadding { Horizontal = 60, Top = 40, Bottom = 90 },
                    Child = formFlow = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 8),
                        Children = new Drawable[]
                        {
                            heading(@"Mapperatorinator", 32),
                            caption(sourceBeatmap != null
                                ? $"song: {sourceBeatmap.Metadata.Artist} - {sourceBeatmap.Metadata.Title}"
                                : $"audio: {Path.GetFileName(externalAudioPath)}"),

                            heading(@"Generator", 22),
                            gamemode = new FormDropdown<int>
                            {
                                Caption = @"Game mode",
                                Items = new[] { 0, 1, 2, 3 },
                            },
                            difficulty = new FormSliderBar<double>
                            {
                                Caption = @"Target star rating",
                                Current = new BindableDouble(5) { MinValue = 1, MaxValue = 10, Precision = 0.1 },
                            },
                            year = new FormNumberBox
                            {
                                Caption = @"Mapping style year (optional, 2007-2024)",
                                PlaceholderText = @"leave empty for the model's default style",
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

                            heading(@"Map info (applied after generation)", 22),
                            caption(@"these don't affect generation; the finished map comes back with them set."),
                            titleBox = new FormTextBox { Caption = @"Title", PlaceholderText = @"keep generated" },
                            artistBox = new FormTextBox { Caption = @"Artist", PlaceholderText = @"keep generated" },
                            creatorBox = new FormTextBox { Caption = @"Creator", PlaceholderText = @"Mapperatorinator" },
                            tagsBox = new FormTextBox { Caption = @"Tags", PlaceholderText = @"ai generated mapperatorinator" },
                            backgroundSelector = new FormFileSelector(@".jpg", @".jpeg", @".png")
                            {
                                Caption = @"Background image",
                            },

                            heading(@"Setup", 22),
                            installSelector = new FormFileSelector
                            {
                                Caption = @"Mapperatorinator folder (the checkout containing inference.py)",
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
                                Child = logFlow = new OsuTextFlowContainer(t => t.Font = OsuFont.GetFont(Typeface.Inter, 13))
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                },
                            },
                        },
                    },
                },
            };

            if (!string.IsNullOrEmpty(runner.Config.InstallPath))
                installSelector.Current.Value = new FileInfo(runner.Config.InstallPath);

            updateEta();
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

        private double audioLengthSeconds =>
            (sourceBeatmap?.Length ?? 180_000) / 1000.0;

        private void updateEta()
        {
            var eta = runner.Estimate(audioLengthSeconds, device);
            etaText.Text = $"device: {device} | estimated time: ~{(int)eta.TotalMinutes}m {eta.Seconds}s"
                           + (device == @"cpu" ? @"  (no CUDA gpu found; this will be slow)" : string.Empty);
        }

        private void startGeneration()
        {
            if (running) return;

            // el folder elegido queda persistido para la proxima
            if (installSelector.Current.Value != null)
            {
                string chosen = installSelector.Current.Value.FullName;
                // FormFileSelector da archivos; si eligieron inference.py directamente, tomamos el folder.
                runner.Config.InstallPath = Directory.Exists(chosen) ? chosen : Path.GetDirectoryName(chosen);
                runner.Save();
            }

            if (!runner.InstallLooksValid)
            {
                notifications?.Post(new Overlays.Notifications.SimpleErrorNotification
                {
                    Text = @"Point the setup at your Mapperatorinator folder first (it needs inference.py inside).",
                });
                return;
            }

            running = true;
            generateButton.Enabled.Value = false;
            generateButton.Text = @"Generating...";
            logScroll.FadeIn(200);

            var stopwatch = Stopwatch.StartNew();
            cancellation = new CancellationTokenSource();

            var request = new MapperatorinatorRequest
            {
                Gamemode = gamemode.Current.Value,
                Difficulty = difficulty.Current.Value,
                Year = int.TryParse(year.Current.Value, out int y) ? y : null,
                Hitsounded = hitsounds.Current.Value,
                SuperTiming = superTiming.Current.Value,
            };

            var overrides = new OszPostProcessor.MetadataOverrides
            {
                Title = emptyToNull(titleBox.Current.Value),
                Artist = emptyToNull(artistBox.Current.Value),
                Creator = emptyToNull(creatorBox.Current.Value),
                Tags = emptyToNull(tagsBox.Current.Value),
                BackgroundImagePath = backgroundSelector.Current.Value?.FullName,
            };

            Task.Run(async () =>
            {
                try
                {
                    request.AudioPath = resolveAudioPath();

                    string osz = await runner.GenerateAsync(request, line => Schedule(() => appendLog(line)), cancellation.Token).ConfigureAwait(false);

                    // aca entran tus settings "en segundo plano": se aplican al osz recien generado
                    OszPostProcessor.Apply(osz, overrides);

                    await beatmaps.Import(new ImportTask(osz)).ConfigureAwait(false);

                    runner.RecordObservedSpeed(audioLengthSeconds, stopwatch.Elapsed, device);

                    Schedule(() =>
                    {
                        appendLog($"done in {stopwatch.Elapsed:mm\\:ss}. imported!");
                        notifications?.Post(new Overlays.Notifications.SimpleNotification
                        {
                            Text = @"Mapperatorinator finished! The new map is in your song select.",
                            Icon = osu.Framework.Graphics.Sprites.FontAwesome.Solid.Magic,
                        });
                        finishRun();
                    });
                }
                catch (OperationCanceledException)
                {
                    Schedule(() =>
                    {
                        appendLog(@"cancelled.");
                        finishRun();
                    });
                }
                catch (Exception e)
                {
                    Logger.Log($"[mapperatorinator] generation failed: {e}");
                    Schedule(() =>
                    {
                        appendLog($"failed: {e.Message}");
                        finishRun();
                    });
                }
            });
        }

        private void finishRun()
        {
            running = false;
            generateButton.Enabled.Value = true;
            generateButton.Text = @"Generate";
            updateEta();
        }

        private void appendLog(string line)
        {
            logFlow.AddParagraph(line);
            logScroll.ScrollToEnd();
        }

        /// <summary>
        /// inference.py wants a real audio file on disk. Files inside the game's storage
        /// are hashed and extensionless, so the track gets copied out to temp with its
        /// original extension before handing it over.
        /// </summary>
        private string resolveAudioPath()
        {
            if (externalAudioPath != null)
                return externalAudioPath;

            Debug.Assert(sourceBeatmap != null);

            string audioFilename = sourceBeatmap.Metadata.AudioFile;
            if (string.IsNullOrEmpty(audioFilename))
                throw new InvalidOperationException(@"This beatmap has no audio file.");

            string? storagePath = sourceBeatmap.BeatmapSet?.GetPathForFile(audioFilename);
            if (storagePath == null)
                throw new InvalidOperationException(@"Couldn't locate the audio file in storage.");

            // los archivos del storage estan hasheados y sin extension; se copia el
            // stream a temp con la extension original, que es lo que inference.py espera.
            var working = beatmaps.GetWorkingBeatmap(sourceBeatmap);
            string dest = Path.Combine(Path.GetTempPath(), @"torii-mapperatorinator", $"audio{Path.GetExtension(audioFilename)}");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            using (var source = working.GetStream(storagePath))
            using (var output = File.Create(dest))
            {
                if (source == null)
                    throw new InvalidOperationException(@"Couldn't open the audio file from storage.");

                source.CopyTo(output);
            }

            return dest;
        }

        private static string? emptyToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        public override bool OnExiting(ScreenExitEvent e)
        {
            cancellation?.Cancel();
            return base.OnExiting(e);
        }
    }
}
