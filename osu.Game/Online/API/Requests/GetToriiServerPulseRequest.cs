// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Polls the Torii server for a live activity snapshot — currently
    /// playing count, recent play rate, top map, sparkline, online users.
    ///
    /// Server endpoint: <c>GET /api/v2/torii/server-pulse</c>. Server caches
    /// the snapshot for 10s on Redis, so even with many concurrent clients
    /// polling at 60s intervals only ~one snapshot is computed per cache
    /// window. See <c>app/router/v2/torii_server_pulse.py</c> for the
    /// authoritative description of the response shape.
    /// </summary>
    public class GetToriiServerPulseRequest : APIRequest<APIToriiServerPulse>
    {
        protected override string Target => @"torii/server-pulse";
    }
}
