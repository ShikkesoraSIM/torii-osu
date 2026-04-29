// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using JetBrains.Annotations;
using Newtonsoft.Json;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>
    /// Updates the current user's equipped aura. Body is JSON
    /// <c>{"aura_id": "..."}</c> where the value is a sentinel
    /// (<c>"default"</c> / <c>"none"</c>), null, or any aura id from the
    /// catalog the user is entitled to. Server returns the refreshed
    /// catalog so a single round-trip updates the picker UI.
    ///
    /// 4xx responses surface via <see cref="APIRequest.Failure"/>:
    ///   400 — unknown aura id (typo).
    ///   403 — the user doesn't own a group that grants this aura.
    /// </summary>
    public class UpdateEquippedAuraRequest : APIRequest<APIAuraCatalog>
    {
        /// <summary>The aura id (or sentinel) to equip. Null is treated as
        /// the "default" sentinel by the server.</summary>
        [CanBeNull]
        public string AuraId { get; }

        public UpdateEquippedAuraRequest([CanBeNull] string auraId)
        {
            AuraId = auraId;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Patch;
            req.ContentType = @"application/json";
            // JSON body — matches the FastAPI Pydantic model on the server.
            // We send the raw object so the server sees `{"aura_id": null}`
            // rather than a missing key when the user picks "default".
            req.AddRaw(JsonConvert.SerializeObject(new { aura_id = AuraId }));
            return req;
        }

        protected override string Target => @"me/equipped-aura";
    }
}
