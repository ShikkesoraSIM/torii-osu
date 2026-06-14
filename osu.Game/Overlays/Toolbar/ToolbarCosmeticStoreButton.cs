// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Overlays.Cosmetics;
using osu.Game.Performance;

namespace osu.Game.Overlays.Toolbar
{
    public partial class ToolbarCosmeticStoreButton : ToolbarOverlayToggleButton
    {
        protected override Anchor TooltipAnchor => Anchor.TopRight;

        [BackgroundDependencyLoader(true)]
        private void load(CosmeticStoreOverlay store)
        {
            // Potato mode: remove the store button entirely (minimal toolbar).
            if (PotatoMode.Active)
            {
                Expire();
                return;
            }

            SetIcon(FontAwesome.Solid.Store);
            StateContainer = store;
        }
    }
}
