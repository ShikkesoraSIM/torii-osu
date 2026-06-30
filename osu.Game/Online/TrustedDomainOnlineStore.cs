// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.IO.Stores;
using osu.Framework.Logging;

namespace osu.Game.Online
{
    public sealed class TrustedDomainOnlineStore : OnlineStore
    {
        protected override string GetLookupUrl(string url)
        {
            // Torii: also trust the Torii servers so avatars, covers and website
            // assets served from *.shikkesora.com load instead of being blocked.
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
                || !(uri.Host.EndsWith(@".ppy.sh", StringComparison.OrdinalIgnoreCase)
                     || uri.Host.EndsWith(@".shikkesora.com", StringComparison.OrdinalIgnoreCase)))
            {
                Logger.Log($@"Blocking resource lookup from external website: {url}", LoggingTarget.Network, LogLevel.Important);
                return string.Empty;
            }

            return url;
        }
    }
}
