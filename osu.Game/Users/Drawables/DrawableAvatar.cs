// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Users.Drawables
{
    [LongRunningLoad]
    public partial class DrawableAvatar : Sprite
    {
        private readonly IUser user;

        /// <summary>
        /// A simple, non-interactable avatar sprite for the specified user.
        /// </summary>
        /// <param name="user">The user. A null value will get a placeholder avatar.</param>
        public DrawableAvatar(IUser user = null)
        {
            this.user = user;

            RelativeSizeAxes = Axes.Both;
            FillMode = FillMode.Fit;
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
        }

        [BackgroundDependencyLoader]
        private void load(LargeTextureStore textures, UserLookupCache userLookupCache, OnlineAssetCachingStore onlineTextures)
        {
            string avatarUrl = (user as APIUser)?.AvatarUrl;

            // torii: a locally-stored score carries a RealmUser with no avatar_url, so
            // resolve the full user by id through the API (UserLookupCache) to get their
            // real Torii avatar. This is [LongRunningLoad], so we can block on the lookup
            // here. Replaces the old hand-built `{APIUrl}/users/{id}/avatar` fallback,
            // which missed the /api/v2 prefix and 301'd to the website (blank avatar), and
            // never risks the osu!-id collision the old a.ppy.sh fallback had. Ids that
            // aren't Torii users just resolve to null -> the guest placeholder.
            //
            // Upstream fetches through OnlineAssetCachingStore (disk cache, #38454) but
            // falls back to a.ppy.sh/{id}, which is exactly the collision we removed: an
            // id that means one player here means someone else there. Se toma el store
            // nuevo (asi el avatar queda cacheado en disco) y se deja NUESTRA resolucion.
            if (string.IsNullOrEmpty(avatarUrl) && user != null && user.OnlineID > 1)
                avatarUrl = userLookupCache.GetUserAsync(user.OnlineID).GetResultSafely()?.AvatarUrl;

            if (!string.IsNullOrEmpty(avatarUrl))
                Texture = onlineTextures.Get(avatarUrl);

            Texture ??= textures.Get(@"Online/avatar-guest");
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            this.FadeInFromZero(300, Easing.OutQuint);
        }
    }
}
