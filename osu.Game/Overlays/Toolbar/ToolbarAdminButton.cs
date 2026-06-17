// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.Cosmetics;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>
    /// Torii: toolbar toggle for the staff hub (<see cref="ToriiAdminOverlay"/>).
    /// Hidden for everyone except confirmed admins — it starts at Alpha 0 so the
    /// toolbar's FillFlow skips it entirely (no gap) until the local user is
    /// verified as staff, then fades in.
    /// </summary>
    public partial class ToolbarAdminButton : ToolbarOverlayToggleButton
    {
        protected override Anchor TooltipAnchor => Anchor.TopRight;

        private readonly IBindable<APIUser> localUser = new Bindable<APIUser>();

        [BackgroundDependencyLoader(true)]
        private void load(ToriiAdminOverlay adminOverlay, IAPIProvider api)
        {
            // Stay collapsed until we confirm staff — IsPresent is false while Alpha
            // is 0 (no AlwaysPresent), so the FillFlow gives it zero width meanwhile.
            Alpha = 0;

            SetIcon(FontAwesome.Solid.UserShield);

            if (adminOverlay != null)
                StateContainer = adminOverlay;

            if (api != null)
                localUser.BindTo(api.LocalUser);

            localUser.BindValueChanged(u =>
            {
                bool admin = adminOverlay != null && ToriiAdminOverlay.IsAdmin(u.NewValue);
                this.FadeTo(admin ? 1 : 0, 200, Easing.OutQuint);
            }, true);
        }
    }
}
