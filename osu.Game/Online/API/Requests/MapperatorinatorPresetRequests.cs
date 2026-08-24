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
        private readonly string? originUsername;

        /// <summary>Pisar el que ya exista con ese nombre. Sin esto el server contesta 409.</summary>
        public bool Overwrite { get; init; }

        public SaveMapperatorinatorPresetRequest(string name, string settings, int? originPresetId = null, string? originUsername = null)
        {
            this.name = name;
            this.settings = settings;
            this.originPresetId = originPresetId;
            this.originUsername = originUsername;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Put;
            req.ContentType = @"application/json";

            // el nombre del dueño va junto con el id porque el server no lo busca por su
            // cuenta: si lo buscara, cualquiera podria preguntarle de quien es el preset N.
            req.AddRaw(JsonConvert.SerializeObject(new { name, settings, origin_preset_id = originPresetId, origin_username = originUsername, overwrite = Overwrite }));
            return req;
        }

        protected override string Target => @"torii/mapperatorinator/presets";
    }

    /// <summary>torii: cambia nombre y/o settings de un preset propio, por id.
    /// Renombrar es esto, NO guardar-y-borrar.</summary>
    public class UpdateMapperatorinatorPresetRequest : APIRequest<APIMapperatorinatorPreset>
    {
        private readonly int id;
        private readonly string? name;
        private readonly string? settings;

        private UpdateMapperatorinatorPresetRequest(int id, string? name, string? settings)
        {
            this.id = id;
            this.name = name;
            this.settings = settings;
        }

        /// <summary>Solo el nombre. Cambiar solo mayusculas o acentos tambien cuenta.</summary>
        public static UpdateMapperatorinatorPresetRequest Rename(int id, string name)
            => new UpdateMapperatorinatorPresetRequest(id, name, null);

        /// <summary>Solo los settings, el nombre queda como esta.</summary>
        public static UpdateMapperatorinatorPresetRequest EditSettings(int id, string settings)
            => new UpdateMapperatorinatorPresetRequest(id, null, settings);

        /// <summary>Los dos de una: el editor puede cambiar el nombre y las opciones juntos.</summary>
        public static UpdateMapperatorinatorPresetRequest Update(int id, string name, string settings)
            => new UpdateMapperatorinatorPresetRequest(id, name, settings);

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Patch;
            req.ContentType = @"application/json";

            // NullValueHandling.Ignore: el server lee "campo ausente" como "no lo
            // toques". Sin esto, un rename mandaria settings: null y lo pisaria.
            req.AddRaw(JsonConvert.SerializeObject(new { name, settings },
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore }));
            return req;
        }

        protected override string Target => $@"torii/mapperatorinator/presets/{id}";
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
