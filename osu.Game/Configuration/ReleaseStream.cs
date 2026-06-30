// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.ComponentModel;

namespace osu.Game.Configuration
{
    /// <summary>
    /// torii: canales de release. heredado de lazer como { Lazer, Tachyon }; renombrado a Torii
    /// (estable) y Nova (experimental, .NET 10 / Deferred / D3D12, tags vYYYY.MDD.N-nova). sin
    /// migracion explicita: Enum.TryParse falla con los strings viejos y cae al default (Torii).
    /// </summary>
    public enum ReleaseStream
    {
        Torii,

        [Description("Nova (Experimental)")]
        Nova,

        /// <summary>
        /// Upstream lazer wired to Torii servers only, with no Torii features.
        /// Aimed at compatibility / low-spec / Linux-Wayland users. Ships under
        /// the <c>-vanilla</c> tag suffix as a GitHub prerelease, like Nova.
        /// </summary>
        Vanilla
    }
}
