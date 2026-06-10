// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Game.Online.API;
using osu.Game.Overlays.Cosmetics;
using osuTK;

namespace osu.Game.Overlays.Toolbar
{
    /// <summary>Toolbar entry for the staff hub. Only visible to server-confirmed
    /// admins — its width collapses (not just fades) for everyone else, so there
    /// is no gap in the toolbar for non-staff.</summary>
    public partial class ToolbarAdminButton : ToolbarOverlayToggleButton
    {
        protected override Anchor TooltipAnchor => Anchor.TopRight;

        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        [BackgroundDependencyLoader(true)]
        private void load(ToriiAdminOverlay admin)
        {
            StateContainer = admin;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            if (api == null)
            {
                collapse(false);
                return;
            }

            api.LocalUser.BindValueChanged(u => collapse(ToriiAdminOverlay.IsAdmin(u.NewValue)), true);
        }

        private void collapse(bool admin)
        {
            // Width 0 + alpha 0 removes the button from the toolbar layout for
            // non-admins; full size + visible for admins.
            this.ScaleTo(admin ? Vector2.One : new Vector2(0f, 1f));
            Alpha = admin ? 1 : 0;
        }
    }
}
