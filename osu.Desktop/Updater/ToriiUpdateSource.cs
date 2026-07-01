// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
            GithubRelease[] all = await fetchAllReleases(includePrereleases).ConfigureAwait(false);

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

        /// <summary>
        /// trae los releases del repo paginando mas hondo que el GithubSource base.
        /// el base solo pide los 10 releases mas nuevos (per_page=10, page=1). como
        /// publicamos tres streams (torii / nova / vanilla) en un mismo repo, el ultimo
        /// release de un stream se va facil mas alla de los primeros 10 empujado por los
        /// releases de los otros dos. cuando pasa eso el filtro por sufijo de arriba no
        /// matchea nada, el feed de Velopack vuelve vacio y el check dice "estas en la
        /// ultima version" - asi el switch de stream no baja ni sube al stream elegido.
        /// pedimos varias paginas grandes para que el ultimo de cada stream siempre este
        /// a la vista. mantiene el orden del base (mas nuevo primero) y el filtro de prerelease.
        /// </summary>
        private async Task<GithubRelease[]> fetchAllReleases(bool includePrereleases)
        {
            const int per_page = 100;
            const int max_pages = 5;

            Uri apiBase = GetApiBaseUrl(RepoUri);
            var collected = new List<GithubRelease>();

            for (int page = 1; page <= max_pages; page++)
            {
                var pageUri = new Uri(apiBase, $"repos{RepoUri.AbsolutePath}/releases?per_page={per_page}&page={page}");
                string json = await Downloader.DownloadString(pageUri.ToString(), GetRequestHeaders("application/vnd.github.v3+json")).ConfigureAwait(false);

                GithubRelease[]? batch = JsonSerializer.Deserialize<GithubRelease[]>(json);
                if (batch == null || batch.Length == 0)
                    break;

                collected.AddRange(batch);

                // una pagina corta = llegamos al final del historial de releases.
                if (batch.Length < per_page)
                    break;
            }

            return collected
                   .OrderByDescending(r => r.PublishedAt)
                   .Where(r => includePrereleases || !r.Prerelease)
                   .ToArray();
        }
    }
}
