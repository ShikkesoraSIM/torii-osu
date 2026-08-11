// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.IO.Network;
using System.Net.Http;

namespace osu.Game.Online.API.Requests
{
    public class PostBeatmapFavouriteRequest : APIRequest
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

        public readonly BeatmapFavouriteAction Action;

        private readonly int id;

        public PostBeatmapFavouriteRequest(int id, BeatmapFavouriteAction action)
        {
            this.id = id;
            Action = action;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;
            req.AddParameter(@"action", Action.ToString().ToLowerInvariant());
            return req;
        }

        protected override string Target => $@"beatmapsets/{id}/favourites";
    }

    public enum BeatmapFavouriteAction
    {
        Favourite,
        UnFavourite
    }
}
