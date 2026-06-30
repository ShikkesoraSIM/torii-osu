// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using osu.Framework.Graphics.Sprites;

namespace osu.Game.Overlays.Dialog
{
    /// <summary>
    /// One-shot notice shown the first time the Vanilla binary reaches the main
    /// menu, explaining what the Vanilla stream is (for people who installed it
    /// directly without going through the in-client stream picker).
    /// </summary>
    public partial class VanillaWelcomeDialog : PopupDialog
    {
        public VanillaWelcomeDialog()
        {
            HeaderText = "You're on Torii Vanilla";
            BodyText = "Vanilla is basically Lazer but wired to work on Torii. It won't have any Torii features and the PP gotten from maps will deviate from the one stored in the server, but this is good for people with compatibility problems, lag issues, poor pcs, or weird systems like Wayland, Linux, etc.";

            Icon = FontAwesome.Solid.InfoCircle;

            Buttons = new PopupDialogButton[]
            {
                new PopupDialogOkButton { Text = "Got it" },
            };
        }
    }
}
