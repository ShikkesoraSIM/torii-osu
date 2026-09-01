// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Framework.Extensions.TypeExtensions;
using osu.Framework.Logging;
using osu.Framework.Utils;
using osu.Game.Online.API;

namespace osu.Game.Online
{
    public abstract class PersistentEndpointClientConnector : IDisposable
    {
        /// <summary>
        /// Whether the managed connection is currently connected. When <c>true</c> use <see cref="CurrentConnection"/> to access the connection.
        /// </summary>
        public IBindable<bool> IsConnected => isConnected;

        /// <summary>
        /// The current connection opened by this connector.
        /// </summary>
        public PersistentEndpointClient? CurrentConnection { get; private set; }

        protected readonly IAPIProvider API;

        private readonly IBindable<APIState> apiState = new Bindable<APIState>();
        private readonly Bindable<bool> isConnected = new Bindable<bool>();
        private readonly SemaphoreSlim connectionLock = new SemaphoreSlim(1);
        private CancellationTokenSource connectCancelSource = new CancellationTokenSource();
        private bool started;

        /// <summary>
        /// torii: cada cuanto se revisa que la conexion siga viva de verdad.
        /// </summary>
        /// <remarks>
        /// La reconexion depende del evento Closed, y ese evento NO SIEMPRE LLEGA. Se vio
        /// en produccion: al reiniciar el servidor, los hubs con trafico (metadata,
        /// spectator) detectaron el corte y volvieron en segundos, pero el de multijugador
        /// -que no manda ni recibe nada mientras no estas en una sala- se quedo con el
        /// socket muerto y NUNCA se entero. El connector seguia diciendo "conectado" y
        /// cualquier llamada moria con "Cannot access a disposed object".
        ///
        /// Lo peor era el sintoma: el juego se veia perfecto (chat, presencia, todo), pero
        /// no se podia entrar a una partida hasta reiniciarlo, y el unico aviso era un
        /// volcado de excepcion de .NET.
        ///
        /// Esto lo cierra preguntando. Es una comparacion de un enum en memoria cada 10
        /// segundos: no hay red, no hay dibujo, no hay asignaciones.
        /// </remarks>
        internal TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(10);

        private CancellationTokenSource? healthCheckCancelSource;

        /// <summary>
        /// How much to delay before attempting to connect again, in milliseconds.
        /// Subject to exponential back-off.
        /// </summary>
        private int retryDelay = 3000;

        /// <summary>
        /// Constructs a new <see cref="PersistentEndpointClientConnector"/>.
        /// </summary>
        /// <param name="api"> An API provider used to react to connection state changes.</param>
        protected PersistentEndpointClientConnector(IAPIProvider api)
        {
            API = api;
            apiState.BindTo(api.State);
        }

        /// <summary>
        /// Attempts to connect and begins processing messages from the remote endpoint.
        /// </summary>
        public void Start()
        {
            if (started)
                return;

            apiState.BindValueChanged(_ => Task.Run(connectIfPossible), true);
            started = true;

            healthCheckCancelSource = new CancellationTokenSource();
            _ = runHealthCheckLoop(healthCheckCancelSource.Token);
        }

        /// <summary>
        /// Revisa cada tanto que la conexion que creemos tener siga viva, y la levanta si no.
        /// </summary>
        /// <remarks>
        /// Solo actua cuando el connector se cree conectado Y la conexion de abajo dice que
        /// no lo esta. Ese desacuerdo es exactamente el agujero que tapa: si los dos dicen
        /// desconectado, el camino normal de reconexion ya esta trabajando y meterse seria
        /// pelearle.
        /// </remarks>
        private async Task runHealthCheckLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(HealthCheckInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    if (apiState.Value != APIState.Online)
                        continue;

                    PersistentEndpointClient? current = CurrentConnection;

                    if (!isConnected.Value || current == null || current.IsAlive)
                        continue;

                    Logger.Log($"{ClientName} connection went stale without notice, reconnecting...", LoggingTarget.Network);

                    await Reconnect().ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    // Un latido que falla no puede tumbar el lazo: si se corta, se pierde
                    // para siempre la unica red de seguridad que tenemos aca.
                    Logger.Log($"{ClientName} health check failed: {e.Message}", LoggingTarget.Network);
                }
            }
        }

        public Task Reconnect()
        {
            Logger.Log($"{ClientName} reconnecting...", LoggingTarget.Network);
            return Task.Run(connectIfPossible);
        }

        private async Task connectIfPossible()
        {
            switch (apiState.Value)
            {
                case APIState.Failing:
                case APIState.Offline:
                    await disconnect(true).ConfigureAwait(true);
                    break;

                case APIState.Online:
                case APIState.RequiresSecondFactorAuth:
                    await connect().ConfigureAwait(true);
                    break;
            }
        }

        private async Task connect()
        {
            cancelExistingConnect();
            // reset retry delay to default.
            retryDelay = 3000;

            if (!await connectionLock.WaitAsync(10000).ConfigureAwait(false))
                throw new TimeoutException("Could not obtain a lock to connect. A previous attempt is likely stuck.");

            try
            {
                while (apiState.Value == APIState.RequiresSecondFactorAuth || apiState.Value == APIState.Online)
                {
                    // ensure any previous connection was disposed.
                    // this will also create a new cancellation token source.
                    await disconnect(false).ConfigureAwait(false);

                    // this token will be valid for the scope of this connection.
                    // if cancelled, we can be sure that a disconnect or reconnect is handled elsewhere.
                    var cancellationToken = connectCancelSource.Token;

                    cancellationToken.ThrowIfCancellationRequested();

                    Logger.Log($"{ClientName} connecting...", LoggingTarget.Network);

                    try
                    {
                        // importantly, rebuild the connection each attempt to get an updated access token.
                        CurrentConnection = await BuildConnectionAsync(cancellationToken).ConfigureAwait(false);
                        CurrentConnection.Closed += ex => onConnectionClosed(ex, cancellationToken);

                        cancellationToken.ThrowIfCancellationRequested();

                        await CurrentConnection.ConnectAsync(cancellationToken).ConfigureAwait(false);

                        Logger.Log($"{ClientName} connected!", LoggingTarget.Network);
                        isConnected.Value = true;
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        //connection process was cancelled.
                        throw;
                    }
                    catch (Exception e)
                    {
                        await handleErrorAndDelay(e, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                connectionLock.Release();
            }
        }

        /// <summary>
        /// Handles an exception and delays an async flow.
        /// </summary>
        private async Task handleErrorAndDelay(Exception exception, CancellationToken cancellationToken)
        {
            // random stagger factor to avoid mass incidental synchronisation
            // compare: https://github.com/peppy/osu-stable-reference/blob/013c3010a9d495e3471a9c59518de17006f9ad89/osu!/Online/BanchoClient.cs#L331
            int thisDelay = (int)(retryDelay * RNG.NextDouble(0.75, 1.25));
            // exponential backoff with upper limit
            // compare: https://github.com/peppy/osu-stable-reference/blob/013c3010a9d495e3471a9c59518de17006f9ad89/osu!/Online/BanchoClient.cs#L539
            retryDelay = Math.Min(120000, (int)(retryDelay * 1.5));

            Logger.Log($"{ClientName} connect attempt failed. Next attempt in {thisDelay / 1000:N0} seconds.\n{exception}", LoggingTarget.Network);
            await Task.Delay(thisDelay, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Creates a new <see cref="PersistentEndpointClient"/>.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to stop the process.</param>
        protected abstract Task<PersistentEndpointClient> BuildConnectionAsync(CancellationToken cancellationToken);

        private async Task onConnectionClosed(Exception? ex, CancellationToken cancellationToken)
        {
            bool hasBeenCancelled = cancellationToken.IsCancellationRequested;

            await disconnect(true).ConfigureAwait(false);

            if (ex != null)
                await handleErrorAndDelay(ex, CancellationToken.None).ConfigureAwait(false);
            else
                Logger.Log($"{ClientName} disconnected", LoggingTarget.Network);

            // make sure a disconnect wasn't triggered (and this is still the active connection).
            if (!hasBeenCancelled)
                await Task.Run(connect, CancellationToken.None).ConfigureAwait(false);
        }

        protected Task Disconnect() => disconnect(true);

        private async Task disconnect(bool takeLock)
        {
            cancelExistingConnect();

            if (takeLock)
            {
                if (!await connectionLock.WaitAsync(10000).ConfigureAwait(false))
                    throw new TimeoutException("Could not obtain a lock to disconnect. A previous attempt is likely stuck.");
            }

            try
            {
                if (CurrentConnection != null)
                    await CurrentConnection.DisposeAsync().ConfigureAwait(false);
            }
            finally
            {
                isConnected.Value = false;
                CurrentConnection = null;

                if (takeLock)
                    connectionLock.Release();
            }
        }

        private void cancelExistingConnect()
        {
            connectCancelSource.Cancel();
            connectCancelSource = new CancellationTokenSource();
        }

        protected virtual string ClientName => GetType().ReadableName();

        public override string ToString() => $"{ClientName} ({(IsConnected.Value ? "connected" : "not connected")})";

        private bool isDisposed;

        protected virtual void Dispose(bool isDisposing)
        {
            if (isDisposed)
                return;

            apiState.UnbindAll();
            cancelExistingConnect();

            isDisposed = true;
        }

        public void Dispose()
        {
            healthCheckCancelSource?.Cancel();
            healthCheckCancelSource?.Dispose();
            healthCheckCancelSource = null;

            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
