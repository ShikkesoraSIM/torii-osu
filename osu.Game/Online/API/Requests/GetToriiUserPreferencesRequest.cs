// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Fetches the current logged-in user's preference bag from the
    /// Torii server. Backs the in-game settings toggles that need to
    /// read whatever the user last set on the web (so the in-game
    /// checkbox reflects the same state the website's User Preferences
    /// page shows).
    ///
    /// Hits <c>/api/private/user/preferences</c> — note the
    /// <c>/api/private/</c> prefix instead of the usual <c>/api/v2/</c>;
    /// torii's preference endpoints live under the private namespace,
    /// so we override <see cref="Uri"/> directly rather than letting
    /// the base class prepend <c>/api/v2/{Target}</c>.
    /// </summary>
    public class GetToriiUserPreferencesRequest : APIRequest<APIToriiUserPreferences>
    {
        // Target is required by the abstract base. We override Uri
        // below, but the breadcrumb logging in Perform() reads Target
        // for the dashboard's per-endpoint stall attribution — give
        // it a readable value rather than NotSupportedException, since
        // we have a clean string for it anyway.
        protected override string Target => @"private/user/preferences";

        protected override string Uri => $@"{API!.Endpoints.APIUrl}/api/{Target}";
    }
}
