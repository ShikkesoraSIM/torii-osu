// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Net.Http;
using Newtonsoft.Json;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Online.API.Requests
{
    /// <summary>torii: los presets de Mapperatorinator del usuario logueado.</summary>
    public class GetMapperatorinatorPresetsRequest : APIRequest<APIMapperatorinatorPresetList>
    {
        protected override string Target => @"torii/mapperatorinator/presets";
    }

    /// <summary>torii: guarda un preset. Repetir el nombre pisa el anterior, como una coleccion.</summary>
    public class SaveMapperatorinatorPresetRequest : APIRequest<APIMapperatorinatorPreset>
    {
        private readonly string name;
        private readonly string settings;
        private readonly int? originPresetId;

        public SaveMapperatorinatorPresetRequest(string name, string settings, int? originPresetId = null)
        {
            this.name = name;
            this.settings = settings;
            this.originPresetId = originPresetId;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Put;
            req.ContentType = @"application/json";
            req.AddRaw(JsonConvert.SerializeObject(new { name, settings, origin_preset_id = originPresetId }));
            return req;
        }

        protected override string Target => @"torii/mapperatorinator/presets";
    }

    /// <summary>torii: borra un preset propio.</summary>
    public class DeleteMapperatorinatorPresetRequest : APIRequest
    {
        private readonly int id;

        public DeleteMapperatorinatorPresetRequest(int id)
        {
            this.id = id;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Delete;
            return req;
        }

        protected override string Target => $@"torii/mapperatorinator/presets/{id}";
    }
}
