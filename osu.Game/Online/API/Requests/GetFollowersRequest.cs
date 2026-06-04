// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Fetches the users who follow the local user (incoming relationships).
    /// Mirrors <see cref="GetFriendsRequest"/> but hits the server's
    /// followers endpoint; each <see cref="APIRelation"/> carries the follower
    /// in <c>TargetUser</c> and a <c>Mutual</c> flag.
    /// </summary>
    public class GetFollowersRequest : APIRequest<List<APIRelation>>
    {
        protected override string Target => @"followers";
    }
}
