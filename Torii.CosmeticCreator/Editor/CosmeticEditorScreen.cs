// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using Newtonsoft.Json.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Platform;
using osu.Game.Cosmetics;
using osu.Game.Cosmetics.Definitions;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osuTK;
using osuTK.Graphics;

namespace Torii.CosmeticCreator.Editor
{
    /// <summary>
    /// torii: pantalla principal del editor. 3 columnas: izquierda = toolbox (elegir la familia del
    /// trail), centro = preview en vivo con controles, derecha = propiedades + export. al cambiar de
    /// familia se arma una definicion nueva con sus defaults y se re-crea el panel; cada edicion rearma
    /// el preview al instante.
    /// </summary>
    public partial class CosmeticEditorScreen : CompositeDrawable
    {
        [Resolved]
        private OverlayColourProvider colours { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

        private TrailPreviewStage preview = null!;
        private Container propsContent = null!;
        private OsuSpriteText statusText = null!;

        private CosmeticDefinition workingDefinition = null!;
        private bool previewPaused;

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
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 8),
                    Padding = new MarginPadding(14),
                    Children = new Drawable[]
                    {
                        sectionLabel("Cursor trail"),
                        familyButton("Dot / soft", CosmeticTrailFamily.Dot),
                        familyButton("Ribbon", CosmeticTrailFamily.Ribbon),
                        familyButton("Particle", CosmeticTrailFamily.Particle),
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
                    new Dimension(GridSizeMode.Absolute, 44),
                },
                Content = new[]
                {
                    new Drawable[] { preview = new TrailPreviewStage() },
                    new Drawable[] { previewControls() },
                },
            },
        };

        private Drawable previewControls() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(8, 0),
            Padding = new MarginPadding { Top = 8 },
            Children = new Drawable[]
            {
                new RoundedButton
                {
                    Width = 120,
                    Height = 30,
                    Text = "Pause",
                    Action = () =>
                    {
                        previewPaused = !previewPaused;
                        preview.SetPaused(previewPaused);
                    },
                },
                new RoundedButton
                {
                    Width = 120,
                    Height = 30,
                    Text = "Reset sweep",
                    Action = () => preview.ResetSweep(),
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
                        statusText = new OsuSpriteText
                        {
                            Font = OsuFont.Torus.With(size: 11),
                            Colour = colours.Content2,
                            Truncate = true,
                            RelativeSizeAxes = Axes.X,
                            Text = "Pick a family and start tweaking.",
                        },
                    },
                },
            },
        };

        // ─────────────────────────── logica ───────────────────────────

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

            var panel = new CosmeticPropertyPanel(workingDefinition) { RelativeSizeAxes = Axes.Both };
            panel.Changed += () => preview.SetDefinition(workingDefinition);
            propsContent.Child = panel;
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
            Margin = new MarginPadding { Bottom = 2, Left = 2 },
        };

        private Drawable familyButton(string label, CosmeticTrailFamily family) => new RoundedButton
        {
            RelativeSizeAxes = Axes.X,
            Height = 34,
            Text = label,
            Action = () => switchFamily(family),
        };
    }
}
