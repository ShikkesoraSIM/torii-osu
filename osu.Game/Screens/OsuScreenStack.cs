// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Screens;
using osu.Game.Graphics.Containers;

namespace osu.Game.Screens
{
    public partial class OsuScreenStack : ScreenStack
    {
        [Cached]
        private BackgroundScreenStack backgroundScreenStack;

        private readonly ParallaxContainer parallaxContainer;

        protected float ParallaxAmount => parallaxContainer.ParallaxAmount;

        public new MarginPadding Padding
        {
            get => base.Padding;
            set => base.Padding = value;
        }

        private float backgroundTopExtension;

        /// <summary>
        /// Cuanto se estira el FONDO por encima del borde de arriba del stack, sin mover el
        /// contenido.
        /// </summary>
        /// <remarks>
        /// Es para la toolbar. Sin auto-hide, OsuGame le mete padding al ScreenOffsetContainer
        /// para que la barra no tape nada, y eso empuja la escena ENTERA hacia abajo, fondo
        /// incluido: detras de la barra no queda un solo pixel dibujado. Un frost ahi no tiene
        /// nada que desenfocar y sale negro.
        ///
        /// Con esto el fondo llega hasta arriba de todo y el contenido conserva su inset, que
        /// es como se comportan las barras translucidas del sistema operativo. Nada de UI se
        /// tapa y el frost siempre tiene material.
        /// </remarks>
        public float BackgroundTopExtension
        {
            get => backgroundTopExtension;
            set => backgroundTopExtension = value;
        }

        public OsuScreenStack()
        {
            InternalChild = parallaxContainer = new ParallaxContainer
            {
                // El alto lo maneja Update para poder sumarle BackgroundTopExtension.
                // Con la extension en 0 esto es igual que RelativeSizeAxes.Both.
                RelativeSizeAxes = Axes.X,
                Child = backgroundScreenStack = new BackgroundScreenStack { RelativeSizeAxes = Axes.Both },
            };

            ScreenPushed += screenPushed;
            ScreenExited += ScreenChanged;
        }

        protected override void Update()
        {
            base.Update();

            parallaxContainer.Y = -backgroundTopExtension;
            parallaxContainer.Height = DrawHeight + backgroundTopExtension;
        }

        public void PushSynchronously(OsuScreen screen)
        {
            LoadComponent(screen);

            Push(screen);
        }

        private void screenPushed(IScreen prev, IScreen next)
        {
            if (LoadState < LoadState.Ready)
            {
                // dependencies must be present to stay in a sane state.
                // this is generally only ever hit by test scenes.
                Schedule(() => screenPushed(prev, next));
                return;
            }

            // create dependencies synchronously to ensure leases are in a sane state.
            ((OsuScreen)next).CreateLeasedDependencies((prev as OsuScreen)?.Dependencies ?? Dependencies);

            ScreenChanged(prev, next);
        }

        protected virtual void ScreenChanged(IScreen prev, IScreen? next)
        {
            setParallax(next);
        }

        private void setParallax(IScreen? next) =>
            parallaxContainer.ParallaxAmount = ParallaxContainer.DEFAULT_PARALLAX_AMOUNT * ((next as IOsuScreen)?.BackgroundParallaxAmount ?? 1.0f);
    }
}
