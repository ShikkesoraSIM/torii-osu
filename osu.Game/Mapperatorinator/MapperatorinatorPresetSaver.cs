// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Net;
using osu.Framework.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Mapperatorinator
{
    /// <summary>
    /// Guardar un preset preguntando antes de pisar uno que ya se llame igual.
    ///
    /// Los tres lugares desde donde se guarda abren un cuadro con el nombre ya escrito y
    /// sin la lista de presets a la vista, asi que la persona no puede ver el choque antes
    /// de apretar: el preset de al lado se iba sin que nadie se enterara, y encima con un
    /// cartel diciendo "guardado".
    /// </summary>
    public static class MapperatorinatorPresetSaver
    {
        public static void Save(IAPIProvider api, IDialogOverlay? dialogOverlay, INotificationOverlay? notifications,
                                string name, string settings, int? originPresetId, string? originUsername,
                                Action<APIMapperatorinatorPreset> onSaved, Action<Action> schedule)
        {
            send(false);

            void send(bool overwrite)
            {
                var request = new SaveMapperatorinatorPresetRequest(name, settings, originPresetId, originUsername) { Overwrite = overwrite };

                request.Success += preset => schedule(() => onSaved(preset));

                request.Failure += e => schedule(() =>
                {
                    // 409 es "ya tenes uno con ese nombre y no me dijiste que lo pise": la
                    // unica falla que se arregla desde aca, y se pregunta por el codigo y
                    // no por el texto del error, que cambia sin avisar.
                    if (!overwrite && e is APIException { StatusCode: HttpStatusCode.Conflict } && dialogOverlay != null)
                    {
                        dialogOverlay.Push(new OverwritePresetDialog(name, () => send(true)));
                        return;
                    }

                    notifications?.Post(new SimpleErrorNotification { Text = $"Couldn't save the preset: {e.Message}" });
                });

                api.Queue(request);
            }
        }

        private partial class OverwritePresetDialog : PopupDialog
        {
            public OverwritePresetDialog(string name, Action overwrite)
            {
                Icon = FontAwesome.Solid.ExclamationTriangle;
                HeaderText = @"That name is taken";
                BodyText = $"You already have a preset called \"{name}\". Saving over it replaces its settings, and there's no undo.";

                Buttons = new PopupDialogButton[]
                {
                    new PopupDialogDangerousButton
                    {
                        Text = @"Replace it",
                        Action = overwrite,
                    },
                    new PopupDialogCancelButton
                    {
                        Text = @"Keep both, I'll pick another name",
                    },
                };
            }
        }
    }
}
