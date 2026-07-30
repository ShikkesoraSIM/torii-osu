// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Localisation;
using osu.Game.Overlays.Settings;
using osuTK;

namespace osu.Game.Overlays.ToriiWelcome
{
    /// <summary>
    /// torii: los atajos que agrega torii sobre los de ppy. hoy es uno solo (subir/bajar el tamano del
    /// cursor, ver GlobalActionContainer), pero es de lo que mas gusta y casi nadie lo descubre solo,
    /// asi que va grande y con las teclas dibujadas en vez de escritas en una linea de texto.
    ///
    /// El slider de abajo es EL MISMO valor que mueve el atajo (GameplayCursorSize), no una copia: sirve
    /// para ver el numero moverse mientras prueban las teclas.
    /// </summary>
    [LocalisableDescription(typeof(ToriiWelcomeStrings), nameof(ToriiWelcomeStrings.ShortcutsTitle))]
    public partial class ScreenToriiShortcuts : WizardScreen
    {
        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            Content.Children = new Drawable[]
            {
                new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    Text = ToriiWelcomeStrings.ShortcutsDescription,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                },
                new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(8),
                    Children = new Drawable[]
                    {
                        new ShortcutRow(ToriiWelcomeStrings.ShortcutsIncreaseCursorSize, @"Ctrl", @"Shift", @"+"),
                        new ShortcutRow(ToriiWelcomeStrings.ShortcutsDecreaseCursorSize, @"Ctrl", @"Shift", @"-"),
                    },
                },
                new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    Text = ToriiWelcomeStrings.ShortcutsAnywhere,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                },
                new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE - 2))
                {
                    Text = ToriiWelcomeStrings.ShortcutsWheelHint,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Colour = OverlayColourProvider.Content2,
                },
                new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: CONTENT_FONT_SIZE))
                {
                    Text = ToriiWelcomeStrings.ShortcutsMenuCursorHint,
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                },
                new SettingsItemV2(new FormSliderBar<float>
                {
                    Caption = SkinSettingsStrings.GameplayCursorSize,
                    Current = config.GetBindable<float>(OsuSetting.GameplayCursorSize),
                    KeyboardStep = 0.01f,
                    LabelFormat = v => $"{v:0.##}x",
                }),
                // mismo caption que en Settings (UI > General y Torii > Menus) asi lo reconocen despues.
                new SettingsItemV2(new FormEnumDropdown<osu.Game.Graphics.Cursor.MenuCursorStyle>
                {
                    Caption = @"Menu cursor style",
                    Current = config.GetBindable<osu.Game.Graphics.Cursor.MenuCursorStyle>(OsuSetting.MenuCursorStyle),
                }),
            };
        }

        /// <summary>
        /// Una fila "teclas -> que hace". Las teclas van dibujadas como keycaps porque escritas inline se
        /// leen como cualquier otra frase y el atajo se pierde; la etiqueta va en flow porque se traduce.
        /// </summary>
        private partial class ShortcutRow : CompositeDrawable
        {
            private readonly LocalisableString label;
            private readonly string[] keys;

            public ShortcutRow(LocalisableString label, params string[] keys)
            {
                this.label = label;
                this.keys = keys;

                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.Y;
                Masking = true;
                CornerRadius = 10;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                var keyFlow = new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(6),
                };

                // sin separador entre keycaps a proposito: uno de los atajos ES la tecla "+", y un "+" de
                // union al lado de un "+" de tecla se lee como un solo simbolo repetido.
                foreach (string key in keys)
                    keyFlow.Add(new KeyCap(key));

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colourProvider.Background5,
                    },
                    new GridContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding { Horizontal = 15, Vertical = 12 },
                        ColumnDimensions = new[]
                        {
                            new Dimension(GridSizeMode.AutoSize),
                            new Dimension(GridSizeMode.Absolute, 20),
                            new Dimension(),
                        },
                        RowDimensions = new[]
                        {
                            new Dimension(GridSizeMode.AutoSize),
                        },
                        Content = new[]
                        {
                            new[]
                            {
                                keyFlow,
                                Empty(),
                                new OsuTextFlowContainer(cp => cp.Font = OsuFont.Default.With(size: 18, weight: FontWeight.SemiBold))
                                {
                                    Anchor = Anchor.CentreLeft,
                                    Origin = Anchor.CentreLeft,
                                    Text = label,
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Colour = colourProvider.Content1,
                                },
                            },
                        },
                    },
                };
            }
        }

        /// <summary>Tecla dibujada: caja redondeada con borde, del ancho que pida el texto.</summary>
        private partial class KeyCap : CompositeDrawable
        {
            private readonly string key;

            public KeyCap(string key)
            {
                this.key = key;

                Anchor = Anchor.CentreLeft;
                Origin = Anchor.CentreLeft;
                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 6;
                BorderThickness = 2;
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colourProvider)
            {
                BorderColour = colourProvider.Highlight1.Opacity(0.6f);

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = colourProvider.Background3,
                    },
                    new Container
                    {
                        AutoSizeAxes = Axes.Both,
                        // padding horizontal generoso: "+" y "-" son un solo caracter y sin esto quedan
                        // como fichitas al lado de "Shift", que es lo que rompe la lectura del atajo.
                        Padding = new MarginPadding { Horizontal = 14, Vertical = 8 },
                        Child = new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = key,
                            Font = OsuFont.Default.With(size: 18, weight: FontWeight.Bold),
                            Colour = colourProvider.Content1,
                        },
                    },
                };
            }
        }
    }
}
