// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>Fetches the remaining per-user render cooldown so the UI can show a countdown before submitting.</summary>
    public class GetReplayRenderCooldownRequest : APIRequest<APIReplayRenderCooldown>
    {
        protected override string Target => @"torii/replay-render/cooldown";
    }
}
