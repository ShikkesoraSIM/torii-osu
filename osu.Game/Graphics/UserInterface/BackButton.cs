// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Screens.Footer;

namespace osu.Game.Graphics.UserInterface
{
    // todo: remove this once all screens migrate to display the new game footer and back button.
    public partial class BackButton : VisibilityContainer
    {
        public Action? Action { get; init; }

        private readonly TwoLayerButton button;

        public BackButton(ScreenFooter.BackReceptor? receptor = null)
        {
            Size = TwoLayerButton.SIZE_EXTENDED;

            Child = button = new TwoLayerButton
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                Text = @"back",
                Icon = OsuIcon.LeftCircle,
                Action = () => Action?.Invoke()
            };

            if (receptor == null)
            {
                // if a receptor wasn't provided, create our own locally.
                Add(receptor = new ScreenFooter.BackReceptor());
            }

            receptor.OnBackPressed = () => button.TriggerClick();
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            // Torii: in grayscale theme, fsyori swaps the legacy pink
            // back-button accents for very-dark grays (Gray1/Gray0) to
            // anchor the corner with high contrast rather than the
            // candy-pink "go back" affordance the default lazer UI
            // uses. Keeping the call-site explicit (rather than
            // remapping Pink itself to Gray1 in OsuColour) lets the
            // rest of the UI's Pink-typed accents remain medium-gray
            // in grayscale mode without dragging this button along
            // with them.
            button.BackgroundColour = ThemeAware.Pick(colours.Pink, colours.Gray1);
            button.HoverColour = ThemeAware.Pick(colours.PinkDark, colours.Gray0);
        }

        protected override void PopIn()
        {
            button.MoveToX(0, 400, Easing.OutQuint);
            button.FadeIn(150, Easing.OutQuint);
        }

        protected override void PopOut()
        {
            button.MoveToX(-TwoLayerButton.SIZE_EXTENDED.X / 2, 400, Easing.OutQuint);
            button.FadeOut(400, Easing.OutQuint);
        }
    }
}
