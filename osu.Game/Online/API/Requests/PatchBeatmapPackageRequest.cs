// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Net.Http;
using osu.Framework.IO.Network;

namespace osu.Game.Online.API.Requests
{
    public class PatchBeatmapPackageRequest : APIUploadRequest
    {
        /// <summary>
        /// torii: las mutaciones siguen siendo seriales. Ver APIRequest.AllowConcurrentExecution.
        ///
        /// La cola paraleliza para que las LECTURAS no hagan fila (abrir un perfil eran 15 GET en
        /// serie a 190 ms cada uno). Pero dos escrituras al mismo recurso encoladas juntas si
        /// dependen del orden: equipar tres auras seguidas mandaba tres PATCH a la vez y el server
        /// se quedaba con el que commiteara ultimo, que podia no ser el que el jugador eligio.
        /// </summary>
        public override bool AllowConcurrentExecution => false;

        protected override string Uri
        {
            get
            {
                // can be removed once the service has been successfully deployed to production
                if (API!.Endpoints.BeatmapSubmissionServiceUrl == null)
                    throw new NotSupportedException("Beatmap submission not supported in this configuration!");

                return $@"{API!.Endpoints.BeatmapSubmissionServiceUrl!}/beatmapsets/{BeatmapSetID}";
            }
        }

        protected override string Target => throw new NotSupportedException();

        public uint BeatmapSetID { get; }

        public Dictionary<string, byte[]> FilesChanged { get; } = new Dictionary<string, byte[]>();

        public HashSet<string> FilesDeleted { get; } = new HashSet<string>();

        public PatchBeatmapPackageRequest(uint beatmapSetId)
        {
            BeatmapSetID = beatmapSetId;
        }

        protected override WebRequest CreateWebRequest()
        {
            var request = base.CreateWebRequest();
            request.Method = HttpMethod.Patch;

            foreach ((string filename, byte[] content) in FilesChanged)
                request.AddFile(@"filesChanged", content, filename);

            foreach (string filename in FilesDeleted)
                request.AddParameter(@"filesDeleted", filename, RequestParameterType.Form);

            request.Timeout = 600_000;
            return request;
        }
    }
}
