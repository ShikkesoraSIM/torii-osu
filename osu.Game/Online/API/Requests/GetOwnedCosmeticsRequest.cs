// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>Fetches the catalog ids the local user owns server-side, so the client
    /// can mirror them into its local owned set. 404s gracefully on an older server.</summary>
    public class GetOwnedCosmeticsRequest : APIRequest<APIOwnedCosmetics>
    {
        protected override string Target => @"torii/store/owned";
    }
}
