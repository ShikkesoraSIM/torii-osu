// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osuTK;

namespace osu.Game.Mapperatorinator
{
    /// <summary>
    /// torii: pide el nombre y guarda. Nada mas: guardarse las opciones de un mapa que
    /// salio bien tiene que ser un click y escribir, no abrir una pantalla.
    /// </summary>
    public partial class PresetNameDialog : PopupDialog
    {
        [Cached]
        private OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Blue);

        private readonly string defaultName;
        private readonly Action<string> save;

        private OsuTextBox nameBox = null!;

        public PresetNameDialog(string defaultName, Action<string> save)
        {
            this.defaultName = defaultName;
            this.save = save;

            Icon = FontAwesome.Solid.Save;
            HeaderText = @"Save these settings as a preset";
            BodyText = @"You'll find it in the preset list next time you generate.";
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            MainContent.Add(new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Padding = new MarginPadding { Horizontal = 40, Top = 10, Bottom = 5 },
                Spacing = new Vector2(0, 5),
                Child = nameBox = new OsuTextBox
                {
                    RelativeSizeAxes = Axes.X,
                    // el mismo limite que acepta el server: pasarse hacia que la respuesta
                    // fuera el json crudo del validador, que no le dice nada a nadie.
                    LengthLimit = 60,
                    PlaceholderText = @"preset name",
                    Text = defaultName,
                    SelectAllOnFocus = true,
                },
            });

            // enter guarda, que es lo que todo el mundo va a apretar.
            nameBox.OnCommit += (_, _) => confirm();

            Buttons = new PopupDialogButton[]
            {
                new PopupDialogOkButton
                {
                    Text = @"Save it",
                    Action = confirm,
                },
                new PopupDialogCancelButton
                {
                    Text = @"Never mind",
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // el nombre sugerido queda seleccionado: escribis encima y listo.
            Schedule(() =>
            {
                GetContainingFocusManager()?.ChangeFocus(nameBox);
                nameBox.SelectAll();
            });
        }

        private void confirm()
        {
            string name = nameBox.Text.Trim();

            if (name.Length == 0)
                return;

            save(name);
            Hide();
        }
    }
}
