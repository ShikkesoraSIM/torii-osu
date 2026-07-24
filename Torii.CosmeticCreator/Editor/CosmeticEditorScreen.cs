// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Platform;
using osu.Game.Cosmetics;
using osu.Game.Cosmetics.Definitions;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Settings;
using osuTK;
using osuTK.Graphics;

namespace Torii.CosmeticCreator.Editor
{
    /// <summary>
    /// torii: pantalla principal del editor. 3 columnas: izquierda = toolbox (elegir qué crear — trail o
    /// aura, o importar uno existente), centro = preview en vivo, derecha = propiedades + export. cada
    /// edición rearma el preview al instante. soporta los DOS tipos de cosmético (trail y aura) con su
    /// preview y su panel; el import detecta el tipo del archivo y monta el modo correcto.
    /// </summary>
    public partial class CosmeticEditorScreen : CompositeDrawable
    {
        [Resolved]
        private OverlayColourProvider colours { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        private Container previewHost = null!;
        private Container controlsHost = null!;
        private Container propsContent = null!;
        private OsuSpriteText statusText = null!;

        private CosmeticDefinition workingDefinition = null!;

        private TrailPreviewStage? trailPreview;
        private AuraPreviewStage? auraPreview;
        private bool previewPaused;

        private readonly Dictionary<TrailPreviewStage.PreviewMode, RoundedButton> modeButtons = new Dictionary<TrailPreviewStage.PreviewMode, RoundedButton>();

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                RowDimensions = new[]
                {
                    new Dimension(GridSizeMode.Absolute, 56),
                    new Dimension(),
                },
                Content = new[]
                {
                    new Drawable[] { titleBar() },
                    new Drawable[] { body() },
                },
            };

            switchFamily(CosmeticTrailFamily.Dot);
        }

        // ─────────────────────────── layout ───────────────────────────

        private Drawable titleBar() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Background4 },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(10, 0),
                    Margin = new MarginPadding { Left = 20 },
                    Children = new Drawable[]
                    {
                        new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = FontAwesome.Solid.Palette,
                            Size = new Vector2(22),
                            Colour = colours.Colour1,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = "Torii Cosmetic Creator",
                            Font = OsuFont.TorusAlternate.With(size: 24, weight: FontWeight.SemiBold),
                            Colour = colours.Content1,
                        },
                    },
                },
            },
        };

        private Drawable body() => new GridContainer
        {
            RelativeSizeAxes = Axes.Both,
            ColumnDimensions = new[]
            {
                new Dimension(GridSizeMode.Absolute, 232),
                new Dimension(),
                new Dimension(GridSizeMode.Absolute, 340),
            },
            Content = new[]
            {
                new[]
                {
                    toolboxColumn(),
                    previewColumn(),
                    propertiesColumn(),
                },
            },
        };

        private Drawable toolboxColumn() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Background4 },
                new OsuScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ScrollbarOverlapsContent = false,
                    Child = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(0, 8),
                        Padding = new MarginPadding(14),
                        Children = new Drawable[]
                        {
                            sectionLabel("Start a trail"),
                            familyButton("Dot / soft", CosmeticTrailFamily.Dot),
                            familyButton("Ribbon", CosmeticTrailFamily.Ribbon),
                            familyButton("Particle", CosmeticTrailFamily.Particle),

                            sectionLabel("Start an aura"),
                            auraButton("Aura — blank", () => blankAura()),
                            auraButton("Aura — Stardust base", () => sampleAura("aura-stardust")),
                            auraButton("Aura — Summer base", () => sampleAura("aura-summer")),

                            sectionLabel("Open / import"),
                            importControls(),
                        },
                    },
                },
            },
        };

        private Drawable previewColumn() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Padding = new MarginPadding(14),
            Child = new GridContainer
            {
                RelativeSizeAxes = Axes.Both,
                RowDimensions = new[]
                {
                    new Dimension(),
                    new Dimension(GridSizeMode.Absolute, 84),
                },
                Content = new[]
                {
                    new Drawable[] { previewHost = new Container { RelativeSizeAxes = Axes.Both } },
                    new Drawable[] { controlsHost = new Container { RelativeSizeAxes = Axes.Both } },
                },
            },
        };

        private Drawable propertiesColumn() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Background4 },
                new GridContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    RowDimensions = new[]
                    {
                        new Dimension(),
                        new Dimension(GridSizeMode.Absolute, 84),
                    },
                    Content = new[]
                    {
                        new Drawable[] { propsContent = new Container { RelativeSizeAxes = Axes.Both } },
                        new Drawable[] { exportBar() },
                    },
                },
            },
        };

        private Drawable exportBar() => new Container
        {
            RelativeSizeAxes = Axes.Both,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colours.Background5 },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 6),
                    Padding = new MarginPadding { Horizontal = 14, Vertical = 12 },
                    Children = new Drawable[]
                    {
                        new RoundedButton
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 34,
                            Text = "Export .toriicosmetic",
                            BackgroundColour = colours.Colour2,
                            Action = export,
                        },
                        statusText = new TruncatingSpriteText
                        {
                            Font = OsuFont.Torus.With(size: 11),
                            Colour = colours.Content2,
                            RelativeSizeAxes = Axes.X,
                            Text = "Pick a base, make it yours, then export.",
                        },
                    },
                },
            },
        };

        // ─────────────────────────── preview controls ───────────────────────────

        private Drawable trailPreviewControls() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 6),
            Padding = new MarginPadding { Top = 8 },
            Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(8, 0),
                    Children = new Drawable[]
                    {
                        modeButton("Auto sweep", TrailPreviewStage.PreviewMode.Sweep),
                        modeButton("Follow mouse", TrailPreviewStage.PreviewMode.FollowMouse),
                        modeButton("Simulate gameplay", TrailPreviewStage.PreviewMode.Gameplay),
                    },
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(8, 0),
                    Children = new Drawable[]
                    {
                        new RoundedButton { Width = 110, Height = 28, Text = "Pause", Action = () => { previewPaused = !previewPaused; trailPreview?.SetPaused(previewPaused); } },
                        new RoundedButton { Width = 110, Height = 28, Text = "Reset", Action = () => trailPreview?.ResetSweep() },
                    },
                },
            },
        };

        private Drawable auraPreviewControls()
        {
            var nameBox = new Bindable<string>("Torii");
            nameBox.BindValueChanged(v => auraPreview?.SetSampleName(v.NewValue));

            return new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 6),
                Padding = new MarginPadding { Top = 8 },
                Children = new Drawable[]
                {
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            new RoundedButton { Width = 150, Height = 28, Text = "Toggle background", Action = () => auraPreview?.ToggleBackground() },
                        },
                    },
                    new OsuTextBox { Width = 240, Height = 30, PlaceholderText = "sample name", Current = nameBox },
                },
            };
        }

        private RoundedButton modeButton(string label, TrailPreviewStage.PreviewMode m)
        {
            var btn = new RoundedButton { Width = 138, Height = 30, Text = label, Action = () => setMode(m) };
            modeButtons[m] = btn;
            return btn;
        }

        private void setMode(TrailPreviewStage.PreviewMode m)
        {
            trailPreview?.SetMode(m);
            foreach (var kvp in modeButtons)
                kvp.Value.BackgroundColour = kvp.Key == m ? colours.Colour1 : colours.Background3;
        }

        // ─────────────────────────── modo TRAIL ───────────────────────────

        private void switchFamily(CosmeticTrailFamily family)
        {
            workingDefinition = new CosmeticDefinition
            {
                Type = CosmeticType.Trail,
                Family = family,
                Name = "Untitled",
                Tier = CosmeticTier.Basic,
                Settings = new JObject(),
            };

            mountTrail();
        }

        private void mountTrail()
        {
            modeButtons.Clear();
            trailPreview = new TrailPreviewStage();
            auraPreview = null;
            previewHost.Child = trailPreview;
            controlsHost.Child = trailPreviewControls();

            var panel = new CosmeticPropertyPanel(workingDefinition) { RelativeSizeAxes = Axes.Both };
            panel.Changed += () => trailPreview?.SetDefinition(workingDefinition);
            propsContent.Child = panel;

            setMode(TrailPreviewStage.PreviewMode.Sweep);
            trailPreview.SetDefinition(workingDefinition);
        }

        // ─────────────────────────── modo AURA ───────────────────────────

        private void blankAura()
        {
            workingDefinition = new CosmeticDefinition
            {
                Type = CosmeticType.Aura,
                AuraKind = AuraKind.Particles,
                Name = "Untitled aura",
                Tier = CosmeticTier.Basic,
                Settings = new JObject(),
            };
            mountAura();
        }

        private void sampleAura(string id)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "SampleAuras", id + CosmeticExporter.EXTENSION);
            try
            {
                workingDefinition = CosmeticExporter.Import(path);
                mountAura();
                statusText.Text = $"Loaded base: {id}";
            }
            catch (Exception e)
            {
                statusText.Text = $"Couldn't load {id}: {e.Message}";
            }
        }

        private void mountAura()
        {
            trailPreview = null;
            auraPreview = new AuraPreviewStage();
            previewHost.Child = auraPreview;
            controlsHost.Child = auraPreviewControls();

            var panel = new AuraPropertyPanel(workingDefinition) { RelativeSizeAxes = Axes.Both };
            panel.Changed += () => auraPreview?.SetDefinition(workingDefinition);
            propsContent.Child = panel;

            auraPreview.SetDefinition(workingDefinition);
        }

        // ─────────────────────────── import / dispatch ───────────────────────────

        private Drawable importControls()
        {
            var picker = new SettingsDropdown<string>
            {
                LabelText = "Available",
                Items = availableLabels(),
            };

            return new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 6),
                Children = new Drawable[]
                {
                    picker,
                    new RoundedButton
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 30,
                        Text = "Load selected",
                        Action = () => loadByLabel(picker.Current.Value),
                    },
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(6, 0),
                        Children = new Drawable[]
                        {
                            new RoundedButton { Width = 118, Height = 28, Text = "Imports folder", Action = () => { try { CosmeticExporter.ImportsStorage(storage).PresentExternally(); } catch { } } },
                            new RoundedButton { Width = 78, Height = 28, Text = "Refresh", Action = () => picker.Items = availableLabels() },
                        },
                    },
                },
            };
        }

        private string[] availableLabels()
        {
            var labels = CosmeticExporter.ListAvailable(storage).Select(x => x.label).ToArray();
            return labels.Length > 0 ? labels : new[] { "(drop files in imports)" };
        }

        private void loadByLabel(string label)
        {
            var match = CosmeticExporter.ListAvailable(storage).FirstOrDefault(x => x.label == label);
            if (match.path == null)
            {
                statusText.Text = "Nothing selected.";
                return;
            }

            try
            {
                workingDefinition = CosmeticExporter.Import(match.path);
                if (workingDefinition.Type == CosmeticType.Aura)
                    mountAura();
                else
                    mountTrail();
                statusText.Text = $"Loaded {Path.GetFileNameWithoutExtension(match.path)} ({workingDefinition.Type})";
            }
            catch (Exception e)
            {
                statusText.Text = $"Load failed: {e.Message}";
            }
        }

        private void export()
        {
            try
            {
                string path = CosmeticExporter.Export(storage, workingDefinition);
                statusText.Text = $"Exported to {path}";
            }
            catch (Exception e)
            {
                statusText.Text = $"Export failed: {e.Message}";
            }
        }

        // ─────────────────────────── helpers ───────────────────────────

        private Drawable sectionLabel(string text) => new OsuSpriteText
        {
            Text = text.ToUpperInvariant(),
            Font = OsuFont.Torus.With(size: 12, weight: FontWeight.Bold),
            Alpha = 0.7f,
            Margin = new MarginPadding { Top = 6, Bottom = 2, Left = 2 },
        };

        private Drawable familyButton(string label, CosmeticTrailFamily family) => new RoundedButton
        {
            RelativeSizeAxes = Axes.X,
            Height = 34,
            Text = label,
            Action = () => switchFamily(family),
        };

        private Drawable auraButton(string label, Action action) => new RoundedButton
        {
            RelativeSizeAxes = Axes.X,
            Height = 34,
            Text = label,
            Action = action,
        };
    }
}
