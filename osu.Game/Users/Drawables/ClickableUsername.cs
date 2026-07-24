// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Users.Drawables
{
    internal partial class ClickableUsername : OsuHoverContainer, IHasCustomTooltip<APIUser>
    {
        public ITooltip<APIUser?> GetCustomTooltip() => new ClickableAvatar.NoCardTooltip();

        public APIUser? TooltipContent { get; }

        private readonly APIUser user;

        [Resolved]
        private OsuGame? game { get; set; }

        public ClickableUsername(APIUser? user, FontUsage? font = null)
        {
            TooltipContent = this.user = user ?? new GuestUser();

            AutoSizeAxes = Axes.Both;

            // Torii: wrap the name text with the aura so a user's aura + name/role
            // colour render behind their name wherever a ClickableUsername appears
            // (results panels, scoreboards, rankings). We wrap the inner SpriteText —
            // not this container — because UserAuraContainer only paints the colour +
            // glow onto a SpriteText target; potato mode / the aura setting suppress
            // it inside. Call-sites that live in a LinkFlowContainer pass their own
            // font so the name keeps the surrounding column's size/style.
            Child = UserAuraContainer.Wrap(user, new OsuSpriteText
            {
                Text = user!.Username,
                Font = font ?? OsuFont.Torus.With(size: 16, weight: FontWeight.SemiBold),
            });

            if (user.Id != APIUser.SYSTEM_USER_ID)
                Action = openProfile;
        }

        private void openProfile()
        {
            if (user.Id > 1 || !string.IsNullOrEmpty(user.Username))
                game?.ShowUser(user);
        }
    }
}
