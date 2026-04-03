// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Localisation;
using osu.Game.Resources.Localisation.Web;

namespace osu.Game.Overlays.Toolbar
{
    public partial class ToolbarChangelogButton : ToolbarButton
    {
        protected override Anchor TooltipAnchor => Anchor.TopRight;

        [BackgroundDependencyLoader]
        private void load(OsuGame game)
        {
            // Keep the toolbar entry visibly stable even if the custom changelog glyph
            // is missing or renders too faint in this context.
            SetIcon(FontAwesome.Solid.Code);
            TooltipMain = PageTitleStrings.MainChangelogControllerDefault;
            TooltipSub = NamedOverlayComponentStrings.ChangelogDescription;
            Action = game.ShowChangelogListing;
        }
    }
}
