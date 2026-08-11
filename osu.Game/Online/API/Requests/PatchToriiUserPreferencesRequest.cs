// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using Newtonsoft.Json;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Updates a subset of the current user's preference bag on the
    /// Torii server. Partial-update semantics: only the fields actually
    /// populated on the passed-in <see cref="APIToriiUserPreferences"/>
    /// are sent — missing/null fields stay unchanged on the server
    /// side (Pydantic <c>None</c> fields are skipped by the patch
    /// handler).
    ///
    /// Hits <c>PATCH /api/private/user/preferences</c>. The endpoint
    /// returns <c>204 No Content</c> on success, so this request is
    /// untyped (<see cref="APIRequest"/> not <see cref="APIRequest{T}"/>).
    /// </summary>
    public class PatchToriiUserPreferencesRequest : APIRequest
    {
        private readonly APIToriiUserPreferences payload;

        /// <summary>
        /// torii: las mutaciones siguen siendo seriales. Ver APIRequest.AllowConcurrentExecution.
        ///
        /// La cola paraleliza para que las LECTURAS no hagan fila (abrir un perfil eran 15 GET en
        /// serie a 190 ms cada uno). Pero dos escrituras al mismo recurso encoladas juntas si
        /// dependen del orden: equipar tres auras seguidas mandaba tres PATCH a la vez y el server
        /// se quedaba con el que commiteara ultimo, que podia no ser el que el jugador eligio.
        /// </summary>
        public override bool AllowConcurrentExecution => false;

        public PatchToriiUserPreferencesRequest(APIToriiUserPreferences payload)
        {
            this.payload = payload;
        }

        // Convenience constructor for the common case of toggling just
        // the NSFW profile media flag — keeps the call site readable
        // (one line of code at the settings toggle) without forcing
        // every caller to construct the payload object inline.
        public static PatchToriiUserPreferencesRequest ProfileMediaShowNsfw(bool value) =>
            new PatchToriiUserPreferencesRequest(new APIToriiUserPreferences { ProfileMediaShowNsfw = value });

        protected override string Target => @"private/user/preferences";

        protected override string Uri => $@"{API!.Endpoints.APIUrl}/api/{Target}";

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Patch;
            req.ContentType = @"application/json";

            // NullValueHandling.Ignore so the JSON only carries fields
            // the caller actually set. Important — the server's PATCH
            // handler treats null as "do not modify" and missing keys
            // as the same. Without this, every flag we don't touch
            // would be serialised as `null` and we'd overwrite the
            // user's other preferences with nulls.
            string json = JsonConvert.SerializeObject(payload, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore,
            });
            req.AddRaw(json);
            return req;
        }
    }
}
