// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Threading.Tasks;
using Velopack.Sources;

namespace osu.Desktop.Updater
{
    /// <summary>
    /// torii: envuelve <see cref="GithubSource"/> con un filtro por sufijo de tag asi cada stream solo
    /// ve sus propios builds. el stream Nova pide prereleases (los tags -nova se publican como
    /// prerelease), pero sin filtro tambien veria el ultimo -torii estable; si una estable sale despues
    /// con version numerica mas alta, Velopack "actualizaria" al usuario Nova hacia la estable (un
    /// downgrade cruzado que no pidio). por eso filtramos por el sufijo despues del ultimo guion.
    /// </summary>
    public class ToriiUpdateSource : GithubSource
    {
        private readonly string requiredTagSuffix;

        public ToriiUpdateSource(string repoUrl, bool prerelease, string? requiredTagSuffix)
            : base(repoUrl, null, prerelease, null)
        {
            this.requiredTagSuffix = requiredTagSuffix ?? string.Empty;
        }

        protected override async Task<GithubRelease[]> GetReleases(bool includePrereleases)
        {
            GithubRelease[] all = await base.GetReleases(includePrereleases).ConfigureAwait(false);

            if (string.IsNullOrEmpty(requiredTagSuffix))
                return all;

            // los titulos de release siguen el contrato de build-gu.yml y terminan en
            // vYYYY.MDD.N-{torii|nova}; el sufijo que queremos es lo que va despues del ultimo guion.
            // si un titulo editado a mano no sigue la convencion lo excluimos en vez de crashear.
            return all.Where(release =>
            {
                if (release?.Name == null)
                    return false;

                int lastDash = release.Name.LastIndexOf('-');
                if (lastDash < 0 || lastDash >= release.Name.Length - 1)
                    return false;

                ReadOnlySpan<char> suffix = release.Name.AsSpan(lastDash + 1);
                return suffix.Equals(requiredTagSuffix.AsSpan(), StringComparison.OrdinalIgnoreCase);
            }).ToArray();
        }
    }
}
