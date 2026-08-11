// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Game.Online.API.Requests;
using osu.Framework.Bindables;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.API;
using System.Collections.Generic;
using osuTK;
using osu.Framework.Allocation;
using osu.Game.Resources.Localisation.Web;

namespace osu.Game.Overlays.Profile.Sections.Recent
{
    public partial class PaginatedRecentActivityContainer : PaginatedProfileSubsection<APIRecentActivity>
    {
        public PaginatedRecentActivityContainer(Bindable<UserProfileData?> user)
            : base(user, missingText: EventsStrings.Empty)
        {
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            ItemsContainer.Spacing = new Vector2(0, 8);
        }

        /// <summary>
        /// torii: esta carga de una aunque sea un request mas, porque es la PRIMERA seccion del
        /// perfil (About y Medals estan comentadas en UserProfileOverlay) y ademas no tiene ni
        /// contador ni encabezado: se construye sin headerText, y ProfileSubsection le pone
        /// Alpha 0 al header cuando no hay texto. Diferida se veia la tarjeta "Recent" vacia,
        /// sin items, sin mensaje y sin spinner, hasta que el jugador scrolleara.
        /// </summary>
        protected override bool LoadWithoutScrolling => true;

        protected override APIRequest<List<APIRecentActivity>> CreateRequest(UserProfileData user, PaginationParameters pagination) =>
            new GetUserRecentActivitiesRequest(user.User.Id, pagination);

        protected override Drawable CreateDrawableItem(APIRecentActivity model) => new DrawableRecentActivity(model);
    }
}
