// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Fetches the cosmetic store pool config (the admin-curated disabled-id
    /// list) so the store filters out items pulled from sale. Read by every
    /// client; the server is the single source of truth. 404s gracefully on a
    /// server that hasn't shipped the endpoint (store then falls back to local).
    /// </summary>
    public class GetStoreConfigRequest : APIRequest<APIStoreConfig>
    {
        protected override string Target => @"torii/store/config";
    }
}
