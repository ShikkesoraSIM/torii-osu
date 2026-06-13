// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Fetches the current user's aura picker catalog: which auras they're
    /// entitled to, what they've currently picked (raw + resolved), and the
    /// sentinel string constants for "Default" / "None".
    ///
    /// Used by the settings picker UI. The server is the single source of
    /// truth for which auras a user owns — the client never decides this
    /// independently.
    /// </summary>
    public class GetAuraCatalogRequest : APIRequest<APIAuraCatalog>
    {
        protected override string Target => @"me/aura-catalog";
    }
}
