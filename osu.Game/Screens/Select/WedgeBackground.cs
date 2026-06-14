// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Overlays;

namespace osu.Game.Screens.Select
{
    internal sealed partial class WedgeBackground : InputBlockingContainer
    {
        public float StartAlpha { get; init; } = 0.9f;

        public float FinalAlpha { get; init; } = 0.6f;

        public float WidthForGradient { get; init; } = 0.3f;

        private OverlayColourProvider colourProvider = null!;
        private Box additiveLayer = null!;
        private Box solidLayer = null!;
        private Box gradientLayer = null!;

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colourProvider)
        {
            this.colourProvider = colourProvider;
            RelativeSizeAxes = Axes.Both;

            InternalChildren = new Drawable[]
            {
                additiveLayer = new Box
                {
                    Blending = BlendingParameters.Additive,
                    RelativeSizeAxes = Axes.Both,
                    Width = 0.6f,
                    Alpha = 0.5f,
                    Colour = ColourInfo.GradientHorizontal(colourProvider.Background2, colourProvider.Background2.Opacity(0)),
                },
                solidLayer = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Width = 1 - WidthForGradient,
                    Colour = colourProvider.Background5.Opacity(StartAlpha),
                },
                gradientLayer = new Box
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    RelativeSizeAxes = Axes.Both,
                    Width = WidthForGradient,
                    Colour = ColourInfo.GradientHorizontal(colourProvider.Background5.Opacity(StartAlpha), colourProvider.Background5.Opacity(FinalAlpha)),
                },
            };

            colourProvider.ColoursChanged += updateTheme;
        }

        private void updateTheme()
        {
            if (additiveLayer != null)
                additiveLayer.Colour = ColourInfo.GradientHorizontal(colourProvider.Background2, colourProvider.Background2.Opacity(0));

            if (solidLayer != null)
                solidLayer.Colour = colourProvider.Background5.Opacity(StartAlpha);

            if (gradientLayer != null)
                gradientLayer.Colour = ColourInfo.GradientHorizontal(colourProvider.Background5.Opacity(StartAlpha), colourProvider.Background5.Opacity(FinalAlpha));
        }

        protected override void Dispose(bool isDisposing)
        {
            if (isDisposing && colourProvider != null)
                colourProvider.ColoursChanged -= updateTheme;

            base.Dispose(isDisposing);
        }
    }
}
