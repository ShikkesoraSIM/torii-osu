// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.IO.Network;
using osu.Game.Configuration;
using osu.Game.Online.API;

namespace osu.Game.Online.API.Requests
{
    internal static class ToriiRequestVariantExtensions
    {
        internal static bool IsPpDevVariantActive(IAPIProvider? api)
        {
            if (!ToriiPpVariantState.UsePpDevVariant)
                return false;

            if (api is not APIAccess apiAccess)
                return true;

            if (apiAccess.IsUnsafeOfficialEndpoint || !apiAccess.IsLikelyToriiEndpoint)
                return false;

            // Keep behaviour predictable: pp-dev is an online Torii variant mode.
            return apiAccess.State.Value != APIState.Offline;
        }

        internal static void AddToriiPpVariantIfEnabled(this WebRequest request, IAPIProvider? api)
        {
            if (api is not APIAccess apiAccess || !apiAccess.IsLikelyToriiEndpoint)
                return;

            request.AddParameter("pp_variant", IsPpDevVariantActive(api) ? "pp_dev" : "stable");
        }
    }
}
