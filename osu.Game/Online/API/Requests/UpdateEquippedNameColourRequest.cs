// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using JetBrains.Annotations;
using Newtonsoft.Json;
using osu.Framework.IO.Network;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Broadcasts the current user's equipped BOUGHT name colour to the server so
    /// every other client paints their username with it (mirrors
    /// <see cref="UpdateEquippedAuraRequest"/>). Body is JSON
    /// <c>{"name_colour": "name-crimson"}</c>, or <c>{"name_colour": null}</c> to
    /// clear it — role colours (<c>name-group-*</c>) and "none" clear the stored
    /// value so other clients fall back to the group/role colour.
    ///
    /// Fire-and-forget. 4xx responses surface via <see cref="APIRequest.Failure"/>:
    ///   400 — not a name-colour id.
    ///   403 — the user doesn't own that colour.
    /// </summary>
    public class UpdateEquippedNameColourRequest : APIRequest
    {
        /// <summary>The name-colour id to broadcast, or null to clear.</summary>
        [CanBeNull]
        /// <summary>
        /// torii: las mutaciones siguen siendo seriales. Ver APIRequest.AllowConcurrentExecution.
        ///
        /// La cola paraleliza para que las LECTURAS no hagan fila (abrir un perfil eran 15 GET en
        /// serie a 190 ms cada uno). Pero dos escrituras al mismo recurso encoladas juntas si
        /// dependen del orden: equipar tres auras seguidas mandaba tres PATCH a la vez y el server
        /// se quedaba con el que commiteara ultimo, que podia no ser el que el jugador eligio.
        /// </summary>
        public override bool AllowConcurrentExecution => false;

        public string NameColourId { get; }

        public UpdateEquippedNameColourRequest([CanBeNull] string nameColourId)
        {
            NameColourId = nameColourId;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Patch;
            req.ContentType = @"application/json";
            // Raw object so the server sees `{"name_colour": null}` (an explicit
            // clear) rather than a missing key.
            req.AddRaw(JsonConvert.SerializeObject(new { name_colour = NameColourId }));
            return req;
        }

        protected override string Target => @"me/equipped-name-colour";
    }
}
