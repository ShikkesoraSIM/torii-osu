// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.ComponentModel;

namespace osu.Game.Configuration
{
    /// <summary>
    /// User-facing release channels.
    /// </summary>
    /// <remarks>
    /// History:
    /// <list type="bullet">
    /// <item>
    /// Inherited from upstream osu! lazer as <c>{ Lazer, Tachyon }</c>.
    /// Torii published only the stable channel under the <c>-lazer</c>
    /// tag suffix; no Torii build ever shipped on <c>Tachyon</c>.
    /// </item>
    /// <item>
    /// May 2026: renamed <c>Lazer → Torii</c> (stable) and
    /// <c>Tachyon → Nova</c> (experimental). The Torii Nova channel is
    /// the home for .NET 10 / Deferred renderer / D3D12 work — see
    /// the <c>nova</c> branch and <c>vYYYY.MDD.N-nova</c> tags.
    /// </item>
    /// </list>
    /// Migration: there is no explicit migration code. <see cref="System.Enum.TryParse{TEnum}(string,bool,out TEnum)"/>
    /// returns <c>false</c> for the old <c>"Lazer"</c> / <c>"Tachyon"</c>
    /// strings, so the framework's bindable load falls back to
    /// <see cref="Torii"/> (the new default). Every user who had the
    /// default before continues on the default; the handful of users on
    /// Tachyon (if any) become Torii stable, which is the safe choice.
    /// Version-string suffix parsing in the mobile / no-action update
    /// managers takes the same fallback path — old <c>-lazer</c> tags
    /// parse as Torii after the rename.
    /// </remarks>
    public enum ReleaseStream
    {
        Torii,

        [Description("Nova (Experimental)")]
        Nova
    }
}
