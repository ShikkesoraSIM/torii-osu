// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;

namespace osu.Game.Mapperatorinator
{
    /// <summary>
    /// torii: shown before AMD GPU generation is switched on. ROCm talks to the card at a
    /// level where a bad combination of card, kernel and driver doesn't just fail the
    /// program: it can take the whole display driver down with it. That happened to a
    /// tester on an RDNA4 card, so nobody gets sent through this without being told.
    /// </summary>
    public partial class RocmWarningDialog : PopupDialog
    {
        [Cached]
        private OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Orange);

        public RocmWarningDialog(string gpuName, Action confirm, Action cancel)
        {
            Icon = FontAwesome.Solid.ExclamationTriangle;
            HeaderText = @"Using the GPU here is experimental";
            BodyText =
                $"""
                 Generating on {gpuName} goes through ROCm, and on some setups (very new cards, or a distro without the ROCm stack) the card rejects the work and takes the display driver down with it: black screens, everything closes.

                 Save whatever you have open first. We'll run a two-second test before anything long, and if the card doesn't like it we go back to the CPU and never ask again.

                 The CPU path always works, it's just slower.
                 """;

            Buttons = new PopupDialogButton[]
            {
                new PopupDialogDangerousButton
                {
                    Text = @"I saved my stuff, try it",
                    Action = confirm,
                },
                new PopupDialogCancelButton
                {
                    Text = @"Stay on the CPU",
                    Action = cancel,
                },
            };
        }
    }
}
