// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework;
using osu.Framework.Platform;

namespace Torii.CosmeticCreator
{
    public static class Program
    {
        public static void Main()
        {
            // app osu-flavored standalone, mismo arranque que el tournament client: un host de escritorio
            // con su propia data dir ("torii-cosmetic-creator" en %APPDATA%), y corremos el game.
            using (DesktopGameHost host = Host.GetSuitableDesktopHost(@"torii-cosmetic-creator", new HostOptions()))
                host.Run(new CosmeticCreatorGame());
        }
    }
}
