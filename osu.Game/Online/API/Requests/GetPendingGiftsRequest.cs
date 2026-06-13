// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>Fetches the local user's unclaimed Torii gifts. Checked after a
    /// play; 404s gracefully on a server without the gift endpoints.</summary>
    public class GetPendingGiftsRequest : APIRequest<APIPendingGifts>
    {
        protected override string Target => @"torii/gifts/pending";
    }
}
