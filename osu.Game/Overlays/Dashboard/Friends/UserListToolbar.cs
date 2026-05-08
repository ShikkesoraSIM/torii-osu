// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osuTK;
using osu.Framework.Bindables;
using osu.Game.Configuration;

namespace osu.Game.Overlays.Dashboard.Friends
{
    public partial class UserListToolbar : CompositeDrawable
    {
        public Bindable<UserSortCriteria> SortCriteria => sortControl.Current;

        public Bindable<OverlayPanelDisplayStyle> DisplayStyle => styleControl.Current;

        private readonly Bindable<OverlayPanelDisplayStyle> configDisplayStyle = new Bindable<OverlayPanelDisplayStyle>();

        private readonly bool supportsBrickMode;
        private readonly UserSortTabControl sortControl;
        private readonly OverlayPanelDisplayStyleControl styleControl;

        public UserListToolbar(bool supportsBrickMode = true, bool supportsSort = true)
        {
            this.supportsBrickMode = supportsBrickMode;

            AutoSizeAxes = Axes.Both;

            AddInternal(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(10, 0),
                Children = new Drawable[]
                {
                    sortControl = new UserSortTabControl
                    {
                        // User Search reuses this toolbar but doesn't want a sort
                        // tab control — search results are already ordered by
                        // relevance to the query, sort criteria don't apply.
                        // Hide rather than skip-construct so SortCriteria still
                        // exposes a usable Bindable for any caller that asks.
                        Alpha = supportsSort ? 1 : 0,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    },
                    styleControl = new OverlayPanelDisplayStyleControl(supportsBrickMode)
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                    }
                }
            });
        }

        [BackgroundDependencyLoader]
        private void load(OsuConfigManager config)
        {
            config.BindWith(OsuSetting.DashboardSortMode, SortCriteria);
            config.BindWith(OsuSetting.DashboardDisplayStyle, configDisplayStyle);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            configDisplayStyle.BindValueChanged(style =>
            {
                if (style.NewValue == OverlayPanelDisplayStyle.Brick && !supportsBrickMode)
                    DisplayStyle.Value = OverlayPanelDisplayStyle.Card;
                else
                    DisplayStyle.Value = style.NewValue;
            }, true);

            DisplayStyle.BindValueChanged(style =>
            {
                configDisplayStyle.Value = style.NewValue;
            }, true);
        }
    }
}
