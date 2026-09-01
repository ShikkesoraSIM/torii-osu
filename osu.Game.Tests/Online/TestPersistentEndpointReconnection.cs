// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Tests.Visual;

namespace osu.Game.Tests.Online
{
    /// <summary>
    /// torii: que una conexion que se muere SIN AVISAR se recupere sola.
    /// </summary>
    /// <remarks>
    /// El caso real: al reiniciar el servidor, los hubs con trafico (metadata, spectator)
    /// detectaron el corte por el evento Closed y volvieron en segundos. El de
    /// multijugador, que no manda ni recibe nada mientras no estas en una sala, se quedo
    /// con el socket muerto y ese evento NUNCA llego. El connector siguio diciendo
    /// "conectado" y cualquier llamada moria con "Cannot access a disposed object", con el
    /// juego viendose perfecto y sin poder entrar a una partida hasta reiniciarlo.
    ///
    /// El test reproduce justamente eso: se mata la conexion sin disparar Closed, que es
    /// lo que hace que el camino normal de reconexion no se entere.
    /// </remarks>
    [TestFixture]
    public class TestPersistentEndpointReconnection
    {
        [Test]
        public async Task ConnectionDyingSilentlyIsRecovered()
        {
            var api = new DummyAPIAccess();
            var connector = new TestConnector(api) { HealthCheckInterval = TimeSpan.FromMilliseconds(50) };

            connector.Start();

            await waitFor(() => connector.IsConnected.Value, "la conexion inicial nunca se establecio").ConfigureAwait(false);

            int connectionsBefore = connector.ConnectionsBuilt;

            // La conexion se muere pero NO se dispara Closed: es el agujero exacto que
            // dejaba al hub de multijugador creyendose conectado para siempre.
            connector.CurrentTestClient!.Alive = false;

            await waitFor(() => connector.ConnectionsBuilt > connectionsBefore,
                "la conexion muerta nunca se detecto: el connector siguio creyendose conectado").ConfigureAwait(false);

            Assert.That(connector.IsConnected.Value, Is.True, "quedo desconectado en vez de recuperarse");
            Assert.That(connector.CurrentTestClient!.Alive, Is.True, "la conexion nueva tampoco esta viva");

            connector.Dispose();
        }

        [Test]
        public async Task HealthyConnectionIsLeftAlone()
        {
            var api = new DummyAPIAccess();
            var connector = new TestConnector(api) { HealthCheckInterval = TimeSpan.FromMilliseconds(50) };

            connector.Start();

            await waitFor(() => connector.IsConnected.Value, "la conexion inicial nunca se establecio").ConfigureAwait(false);

            int connectionsBefore = connector.ConnectionsBuilt;

            // Con la conexion sana, el latido no tiene que hacer NADA. Un chequeo que
            // reconecta de mas es peor que no tenerlo: cortaria conexiones buenas.
            await Task.Delay(500).ConfigureAwait(false);

            Assert.That(connector.ConnectionsBuilt, Is.EqualTo(connectionsBefore),
                "el chequeo reconecto una conexion que estaba sana");

            connector.Dispose();
        }

        private static async Task waitFor(Func<bool> condition, string message)
        {
            for (int i = 0; i < 100; i++)
            {
                if (condition())
                    return;

                await Task.Delay(50).ConfigureAwait(false);
            }

            Assert.Fail(message);
        }

        private class TestConnector : PersistentEndpointClientConnector
        {
            public int ConnectionsBuilt { get; private set; }
            public TestClient? CurrentTestClient { get; private set; }

            public TestConnector(IAPIProvider api)
                : base(api)
            {
            }

            protected override Task<PersistentEndpointClient> BuildConnectionAsync(CancellationToken cancellationToken)
            {
                ConnectionsBuilt++;
                return Task.FromResult((PersistentEndpointClient)(CurrentTestClient = new TestClient()));
            }

            protected override string ClientName => nameof(TestConnector);
        }

        private class TestClient : PersistentEndpointClient
        {
            public bool Alive = true;

            public override bool IsAlive => Alive;

            public override Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
