// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Events;

namespace osu.Game.Skinning.Select
{
    public partial class LegacyBackButton : CompositeDrawable
    {
        // Torii: the housing PR ships the legacy back button visual-only ("buttons
        // hooked up at a later stage"). We wire it so the legacy footer is actually
        // functional in song select. The child pieces handle the click sound/anim and
        // return unhandled, so the click bubbles up here to fire the action.
        public Action? Action { get; init; }

        [BackgroundDependencyLoader]
        private void load(ISkinSource skin)
        {
            AutoSizeAxes = Axes.Both;

            bool old = skin.GetAnimation("menu-back", true, false) != null;

            if (old)
            {
                InternalChild = new LegacyOldBackButtonPiece
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    // skinned menu-back renders 2px too low otherwise.
                    Y = -2,
                };
            }
            else
            {
                InternalChild = new LegacyNewBackButtonPiece
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                };
            }
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (Action == null)
                return false;

            Action.Invoke();
            return true;
        }
    }
}
