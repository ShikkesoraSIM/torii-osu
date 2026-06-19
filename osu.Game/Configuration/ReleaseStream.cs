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
        Nova
    }
}
