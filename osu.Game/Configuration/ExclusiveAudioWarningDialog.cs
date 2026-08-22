// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;

namespace osu.Game.Configuration
{
    /// <summary>
    /// torii: shown before exclusive audio mode is switched on, because it does something
    /// people don't expect: it takes the output device away from every other application.
    /// </summary>
    public partial class ExclusiveAudioWarningDialog : PopupDialog
    {
        [Cached]
        private OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Orange);

        public ExclusiveAudioWarningDialog(Action confirm, Action cancel)
        {
            Icon = FontAwesome.Solid.ExclamationTriangle;
            HeaderText = @"Exclusive mode takes over your audio device";
            BodyText =
                """
                While the game is open, nothing else can play sound through that device: Discord, your browser, music, all of it goes quiet.

                Your microphone keeps working, so people will still hear you, you just won't hear them.

                In exchange you get the lowest latency your hardware can do. Turn it back off any time.
                """;

            Buttons = new PopupDialogButton[]
            {
                new PopupDialogDangerousButton
                {
                    Text = @"Got it, turn it on",
                    Action = confirm,
                },
                new PopupDialogCancelButton
                {
                    Text = @"Leave it off",
                    Action = cancel,
                },
            };
        }
    }
}
