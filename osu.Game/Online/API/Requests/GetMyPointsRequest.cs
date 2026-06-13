// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>Fetches the local user's authoritative points balance. 404s gracefully
    /// on a server without the points endpoints.</summary>
    public class GetMyPointsRequest : APIRequest<APIMyPoints>
    {
        protected override string Target => @"torii/points/me";
    }
}
