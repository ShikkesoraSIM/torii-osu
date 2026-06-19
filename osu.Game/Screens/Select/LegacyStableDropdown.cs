// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using FrameworkMenu = osu.Framework.Graphics.UserInterface.Menu;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// torii: dropdown estilo osu!stable para la barra de arriba del song-select legacy. caja negra
    /// redondeada con borde de 1px de color, un label y un chevron hacia abajo, mas una lista de
    /// opciones negra y plana que se ilumina con el accent al hacer hover. todo el comportamiento
    /// (abrir/cerrar/seleccionar/teclado) viene del <see cref="Dropdown{T}"/> del framework; aca solo
    /// piso el look.
    /// </summary>
    public partial class LegacyStableDropdown<T> : Dropdown<T>
    {
        /// <summary>
        /// el "highlight colour" de stable para este dropdown (azul para Group, verde para Sort). define
        /// el borde de la caja y el color de hover de las opciones.
        /// </summary>
        public Color4 AccentColour { get; init; } = Color4.White;

        /// <summary>el tinte de hover de las opciones, por si tiene que ser distinto al borde de la caja
        /// (el dropdown de ranking de stable tiene caja cyan pero hover rosa). por defecto usa <see cref="AccentColour"/>.</summary>
        public Color4? HoverColour { get; init; }

        /// <summary>override opcional para como se arma el texto de cada item (ej "Global Ranking").</summary>
        public Func<T, LocalisableString>? ItemText { get; init; }

        protected override DropdownHeader CreateHeader() => new LegacyDropdownHeader();

        protected override DropdownMenu CreateMenu() => new LegacyDropdownMenu();

        [BackgroundDependencyLoader]
        private void load()
        {
            // AccentColour todavia no esta seteado cuando CreateHeader()/CreateMenu() corren adentro
            // del ctor base, asi que el borde de color y el tinte de hover hay que aplicarlos aca (si no
            // se quedan en el blanco por defecto).
            Header.BorderColour = AccentColour;
            ((LegacyDropdownMenu)Menu).AccentColour = HoverColour ?? AccentColour;
        }

        protected override LocalisableString GenerateItemText(T item)
        {
            if (ItemText != null)
                return ItemText(item);

            if (item is Enum e)
                return e.GetLocalisableDescription();

            return base.GenerateItemText(item);
        }

        private partial class LegacyDropdownHeader : DropdownHeader
        {
            private readonly TruncatingSpriteText label;

            protected override LocalisableString Label
            {
                get => label.Text;
                set => label.Text = value;
            }

            public LegacyDropdownHeader()
            {
                RelativeSizeAxes = Axes.X;
                AutoSizeAxes = Axes.None;
                Height = 24;

                Masking = true;
                CornerRadius = 5;
                BorderThickness = 1;

                // semitransparente asi el color de la decoracion skinnable de arriba se transluce por la
                // caja (las cajas Group/Sort/ranking de stable estan tinteadas, no negro solido).
                BackgroundColour = new Color4(0f, 0f, 0f, 0.45f);
                BackgroundColourHover = new Color4(0.16f, 0.16f, 0.16f, 0.6f);

                Foreground.Padding = new MarginPadding { Horizontal = 8, Vertical = 2 };

                Children = new Drawable[]
                {
                    label = new TruncatingSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        RelativeSizeAxes = Axes.X,
                        Padding = new MarginPadding { Right = 16 },
                        Font = LegacyFonts.Get(19, FontWeight.Regular),
                    },
                    new SpriteIcon
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Icon = FontAwesome.Solid.ChevronDown,
                        Size = new Vector2(9),
                    },
                };
            }

            protected override DropdownSearchBar CreateSearchBar() => new LegacyDropdownSearchBar();

            private partial class LegacyDropdownSearchBar : DropdownSearchBar
            {
                protected override void PopIn() => this.FadeIn();

                protected override void PopOut() => this.FadeOut();

                protected override TextBox CreateTextBox() => new BasicTextBox
                {
                    PlaceholderText = @"type to search",
                    FontSize = 14,
                };
            }
        }

        private partial class LegacyDropdownMenu : DropdownMenu
        {
            private Color4 accent = Color4.White;

            /// <summary>el tinte de hover de las opciones (azul/verde/cyan). tambien se aplica a los items ya existentes.</summary>
            public Color4 AccentColour
            {
                get => accent;
                set
                {
                    accent = value;
                    foreach (var item in Children.OfType<DrawableLegacyDropdownMenuItem>())
                        item.HoverColour = value;
                }
            }

            public LegacyDropdownMenu()
            {
                MaskingContainer.CornerRadius = 5;
                Alpha = 0;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                // negro oscuro medio transparente (las opciones de stable son ~negro alpha 240/255).
                BackgroundColour = new Color4(0f, 0f, 0f, 0.9f);
            }

            protected override void AnimateOpen() => this.FadeIn(300, Easing.OutQuint);

            protected override void AnimateClose() => this.FadeOut(300, Easing.OutQuint);

            private Vector2? targetSize;

            protected override void UpdateSize(Vector2 newSize)
            {
                if (newSize == targetSize)
                    return;

                targetSize = newSize;
                Width = newSize.X;
                this.ResizeHeightTo(newSize.Y, 300, Easing.OutQuint);
            }

            protected override FrameworkMenu CreateSubMenu() => new BasicMenu(Direction.Vertical);

            protected override DrawableDropdownMenuItem CreateDrawableDropdownMenuItem(MenuItem item) => new DrawableLegacyDropdownMenuItem(item) { HoverColour = accent };

            protected override ScrollContainer<Drawable> CreateScrollContainer(Direction direction) => new OsuScrollContainer(direction);

            private partial class DrawableLegacyDropdownMenuItem : DrawableDropdownMenuItem
            {
                public Color4 HoverColour
                {
                    set => BackgroundColourHover = value;
                }

                private OsuSpriteText content = null!;

                public DrawableLegacyDropdownMenuItem(MenuItem item)
                    : base(item)
                {
                    Foreground.Padding = new MarginPadding { Horizontal = 8, Vertical = 4 };
                    BackgroundColour = new Color4(0f, 0f, 0f, 0f);
                    BackgroundColourSelected = new Color4(1f, 1f, 1f, 0.12f);
                }

                protected override Drawable CreateContent() => content = new OsuSpriteText
                {
                    Font = LegacyFonts.Get(17),
                };

                protected override void UpdateForegroundColour()
                {
                    base.UpdateForegroundColour();
                    // la opcion seleccionada se ve en bold en la lista abierta (como stable).
                    content.Font = LegacyFonts.Get(17, IsSelected ? FontWeight.Bold : FontWeight.Regular);
                }
            }
        }
    }
}
