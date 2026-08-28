// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Bindables;
using osu.Game.Overlays.Dashboard;
using osu.Game.Configuration;
using osu.Framework.Allocation;
using osu.Game.Overlays.Dashboard.CurrentlyOnline;
using osu.Game.Overlays.Dashboard.Friends;
using osu.Game.Overlays.Dashboard.UserSearch;

namespace osu.Game.Overlays
{
    public partial class DashboardOverlay : TabbableOnlineOverlay<DashboardOverlayHeader, DashboardOverlayTabs>
    {
        private readonly BindableBool loading = new BindableBool();

        public DashboardOverlay()
            : base(OverlayColourScheme.Purple)
        {
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            loading.BindValueChanged(loading =>
            {
                if (loading.NewValue)
                    Loading.Show();
                else
                    Loading.Hide();
            }, true);
        }

        protected override void PopIn()
        {
            // Torii: el dashboard abre SIEMPRE en "currently online" y ordenado por rank.
            //
            // Va antes del base.PopIn() a proposito: ahi adentro se dispara el
            // TriggerChange que arma el display de la pestaña, asi que cambiar la
            // pestaña despues cargaria primero la de amigos y la reemplazaria enseguida,
            // que se ve como un parpadeo y ademas pide la lista de amigos al pedo.
            //
            // La pestaña se pisa en cada apertura y no una sola vez porque el enum
            // arranca en Friends: sin esto, cerrar y volver a abrir te devuelve ahi.
            header.Current.Value = DashboardOverlayTabs.CurrentlyPlaying;

            // El orden esta atado a un setting que se guarda, asi que asignarlo aca
            // tambien lo deja elegido para la proxima. Se puede cambiar a mano mientras
            // el panel esta abierto; lo que se garantiza es como ARRANCA.
            config?.SetValue(OsuSetting.DashboardSortMode, UserSortCriteria.Rank);

            base.PopIn();
        }

        [Resolved]
        private OsuConfigManager config { get; set; }

        private DashboardOverlayHeader header;

        protected override DashboardOverlayHeader CreateHeader() => header = new DashboardOverlayHeader();

        public override bool AcceptsFocus => false;

        protected override void CreateDisplayToLoad(DashboardOverlayTabs tab)
        {
            switch (tab)
            {
                case DashboardOverlayTabs.Friends:
                    LoadDisplay(new FriendDisplay
                    {
                        Loading = { BindTarget = loading },
                    });
                    break;

                case DashboardOverlayTabs.CurrentlyPlaying:
                    LoadDisplay(new CurrentlyOnlineDisplay
                    {
                        Loading = { BindTarget = loading },
                        OverlayState = { BindTarget = State }
                    });
                    break;

                case DashboardOverlayTabs.UserSearch:
                    LoadDisplay(new UserSearchDisplay
                    {
                        Loading = { BindTarget = loading },
                    });
                    break;

                default:
                    throw new NotImplementedException($"Display for {tab} tab is not implemented");
            }
        }
    }
}
