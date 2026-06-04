// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Profile.Header.Components
{
    /// <summary>
    /// Small circular button shown only on the local user's own profile,
    /// tucked between the follower-count and mapping-subscribers buttons.
    /// Opens the Torii followers list (who follows you). Other users' profiles
    /// don't show it — you can only browse your own followers.
    /// </summary>
    public partial class ViewFollowersButton : OsuClickableContainer, IHasTooltip
    {
        public readonly Bindable<UserProfileData?> User = new Bindable<UserProfileData?>();

        public LocalisableString TooltipText => "See who follows you";

        private readonly IBindable<APIUser> localUser = new Bindable<APIUser>();

        private Box background = null!;
        private SpriteIcon icon = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        [Resolved(canBeNull: true)]
        private FollowersListOverlay? followersOverlay { get; set; }

        public ViewFollowersButton()
        {
            Size = new Vector2(32);
            Anchor = Anchor.CentreLeft;
            Origin = Anchor.CentreLeft;
            Masking = true;
            CornerRadius = 16;
            // Hidden by default; faded in only on the local user's own profile.
            // Alpha 0 also drops it from the FillFlow layout so other profiles
            // don't get an empty gap between the neighbouring buttons.
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            localUser.BindTo(api.LocalUser);

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = colourProvider.Background6,
                },
                icon = new SpriteIcon
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Size = new Vector2(13),
                    Icon = FontAwesome.Solid.Users,
                    Colour = colourProvider.Content2,
                },
            };

            Action = () => followersOverlay?.ShowFollowers();
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            User.BindValueChanged(_ => updateVisibility());
            localUser.BindValueChanged(_ => updateVisibility(), true);
        }

        private void updateVisibility()
        {
            bool ownProfile = User.Value != null
                              && localUser.Value.OnlineID > 1
                              && localUser.Value.OnlineID == User.Value.User.OnlineID;

            this.FadeTo(ownProfile ? 1 : 0, 150, Easing.OutQuint);
        }

        protected override bool OnHover(HoverEvent e)
        {
            background.FadeColour(colourProvider.Background5, 150, Easing.OutQuint);
            icon.FadeColour(Color4.White, 150, Easing.OutQuint);
            return base.OnHover(e);
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            background.FadeColour(colourProvider.Background6, 150, Easing.OutQuint);
            icon.FadeColour(colourProvider.Content2, 150, Easing.OutQuint);
            base.OnHoverLost(e);
        }
    }
}
