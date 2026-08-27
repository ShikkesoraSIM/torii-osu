// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Platform;
using osu.Game.Overlays.Dialog;
using osu.Game.Rulesets;

namespace osu.Game.Screens.Menu
{
    /// <summary>
    /// Le explica al jugador por que no se puede conectar teniendo rulesets ajenos.
    /// </summary>
    /// <remarks>
    /// Va en el medio de la pantalla y no como notificacion de la esquina a proposito: el
    /// juego queda offline hasta que los saque, asi que no alcanza con un aviso que se va
    /// solo a los cinco segundos.
    /// </remarks>
    public partial class CustomRulesetsBlockedDialog : PopupDialog
    {
        [Resolved]
        private Storage storage { get; set; } = null!;

        public CustomRulesetsBlockedDialog()
        {
            HeaderText = @"Custom rulesets are not allowed";
            Icon = FontAwesome.Solid.Ban;

            string found = string.Join(@", ", CustomRulesetGuard.Detected.Select(shortName));

            BodyText =
                "You can't connect to Torii with custom rulesets installed, sorry.\n\n"
                + $"Found: {found}\n\n"
                + "Delete them from the \"rulesets\" folder in your Torii folder and restart.";

            Buttons = new List<PopupDialogButton>
            {
                new PopupDialogOkButton
                {
                    Text = @"Open the rulesets folder",
                    // Se abre la carpeta en vez de solo nombrarla: el que llego hasta aca ya
                    // esta confundido, y "buscala vos" es la peor respuesta posible.
                    Action = () => storage.GetStorageForDirectory(@"rulesets").PresentExternally(),
                },
                new PopupDialogCancelButton
                {
                    Text = @"Got it",
                },
            };
        }

        /// <summary>
        /// "osu.Game.Rulesets.Mosu" queda en "Mosu", que es lo que el jugador ve como archivo.
        /// </summary>
        private static string shortName(string assemblyName)
        {
            const string prefix = @"osu.Game.Rulesets.";

            return assemblyName.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)
                ? assemblyName[prefix.Length..]
                : assemblyName;
        }
    }
}
