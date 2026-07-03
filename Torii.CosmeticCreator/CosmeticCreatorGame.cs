// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Game;
using osu.Game.Overlays;
using Torii.CosmeticCreator.Editor;

namespace Torii.CosmeticCreator
{
    /// <summary>
    /// torii: la app de la Cosmetic Creator. hereda OsuGameBase (igual que el tournament client) para
    /// tener fonts + OsuColour + el toolkit UI de osu + un host standalone, sin ser el juego completo.
    /// cachea un OverlayColourProvider (theming coherente para los Form/Settings controls) y monta el
    /// editor. NO necesita online/gameplay: es una herramienta de autor de cosmeticos.
    /// </summary>
    [Cached(typeof(CosmeticCreatorGame))]
    public partial class CosmeticCreatorGame : OsuGameBase
    {
        private DependencyContainer dependencies = null!;

        // el rosa de la paleta de cosmeticos torii; da el tono a todos los controles.
        private readonly OverlayColourProvider colourProvider = new OverlayColourProvider(OverlayColourScheme.Pink);

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
            => dependencies = new DependencyContainer(base.CreateChildDependencies(parent));

        [BackgroundDependencyLoader]
        private void load()
        {
            dependencies.CacheAs(colourProvider);

            Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colourProvider.Background6,
            });

            Add(new CosmeticEditorScreen
            {
                RelativeSizeAxes = Axes.Both,
            });
        }
    }
}
