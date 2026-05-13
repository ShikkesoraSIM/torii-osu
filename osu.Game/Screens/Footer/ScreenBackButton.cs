// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Localisation;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Footer
{
    public partial class ScreenBackButton : ShearedButton
    {
        // Torii: const → static readonly so ScreenFooter's two layout
        // padding calculations that reference this value (left margin
        // of the buttons flow + the hidden buttons container) shrink
        // alongside the button itself when the grayscale theme is on.
        public static readonly float BUTTON_WIDTH = ThemeAware.Pick(240f, 180f);

        public sealed override bool ReceivePositionalInputAt(Vector2 screenSpacePos)
        {
            // Ensure clicks in the corner of the screen still trigger the back button.
            // Need to apply more than 1x inflation due to shear.
            var inputRectangle = DrawRectangle.Inflate(new MarginPadding
            {
                Left = OsuGame.SCREEN_EDGE_MARGIN * 2,
                Bottom = OsuGame.SCREEN_EDGE_MARGIN * 2,
            });

            return inputRectangle.Contains(ToLocalSpace(screenSpacePos));
        }

        public ScreenBackButton()
            : base(BUTTON_WIDTH)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            ButtonContent.Child = new FillFlowContainer
            {
                X = -10f,
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(20f, 0f),
                Children = new Drawable[]
                {
                    new SpriteIcon
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Size = new Vector2(17f),
                        Icon = FontAwesome.Solid.ChevronLeft,
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = OsuFont.TorusAlternate.With(size: 17),
                        Text = CommonStrings.Back,
                        UseFullGlyphHeight = false,
                    }
                }
            };

            // Torii: fsyori's reskin swaps the magenta/pink accent
            // for near-black so the back button reads as a dark
            // chrome anchor rather than a candy-pink call-to-action.
            // Default Torii keeps the magenta colours (the upstream
            // lazer look).
            DarkerColour = Color4Extensions.FromHex(ThemeAware.Pick("#DE31AE", "#000000"));
            LighterColour = Color4Extensions.FromHex(ThemeAware.Pick("#FF86DD", "#101010"));
            TextColour = Color4.White;
        }
    }
}
