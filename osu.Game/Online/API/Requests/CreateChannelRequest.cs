// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Net.Http;
using osu.Framework.IO.Network;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;

namespace osu.Game.Online.API.Requests
{
    public class CreateChannelRequest : APIRequest<APIChatChannel>
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

        public readonly Channel Channel;

        public CreateChannelRequest(Channel channel)
        {
            Channel = channel;
        }

        protected override WebRequest CreateWebRequest()
        {
            var req = base.CreateWebRequest();
            req.Method = HttpMethod.Post;

            req.AddParameter("type", $"{ChannelType.PM}");
            req.AddParameter("target_id", $"{Channel.Users.First().Id}");

            return req;
        }

        protected override string Target => @"chat/channels";
    }
}
