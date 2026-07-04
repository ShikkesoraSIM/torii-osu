// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>Fetches the local user's recent replay renders so they can reopen a video even if they didn't share it.</summary>
    public class GetMyReplayRendersRequest : APIRequest<APIMyReplayRenders>
    {
        protected override string Target => @"torii/replay-render/mine";
    }
}
