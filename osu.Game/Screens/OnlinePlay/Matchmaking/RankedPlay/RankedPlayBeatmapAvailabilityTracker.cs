// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Multiplayer;
using osu.Game.Online.Rooms;
using Realms;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay
{
    public partial class RankedPlayBeatmapAvailabilityTracker : OnlinePlayBeatmapAvailabilityTracker
    {
        [Resolved]
        private MultiplayerClient client { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private BeatmapLookupCache beatmapLookupCache { get; set; } = null!;

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        private BeatmapModelDownloader beatmapDownloader { get; set; } = null!;

        private CancellationTokenSource? downloadCheckCancellation;
        private int? lastDownloadCheckedBeatmapId;

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            var dependencies = new DependencyContainer(base.CreateChildDependencies(parent));
            dependencies.CacheAs(beatmapDownloader = new BeatmapModelDownloader(parent.Get<BeatmapManager>(), parent.Get<IAPIProvider>()));
            return dependencies;
        }

        /// <summary>
        /// Acepta la copia local tambien cuando el md5 guardado quedo viejo.
        /// </summary>
        /// <remarks>
        /// El chequeo de arriba compara el archivo local contra <c>OnlineMD5Hash</c>, que
        /// es una FOTO del hash tomada cuando el mapa se importo. Si esa foto quedo vieja
        /// (el mapa se actualizo despues) o nunca se lleno (el server no tenia el
        /// checksum), el mapa figura como faltante aunque este perfecto.
        ///
        /// En una sala eso no es un cartel molesto y nada mas: la etapa espera a que
        /// todos tengan el mapa, y como nadie lo va a "conseguir" nunca, los dos se comen
        /// el reloj entero mirando la pantalla y encima se llevan el castigo por no haber
        /// cargado. Es exactamente el sintoma de "dice missing beatmap y lo tengo".
        ///
        /// Aca se compara contra el md5 que el servidor acaba de mandar en ESTA respuesta,
        /// que es el dato fresco, y se sigue aceptando el camino de siempre. La proteccion
        /// contra jugar un mapa editado se mantiene: si el server dice un hash y el archivo
        /// local dice otro, no pasa. Lo unico que cambia es que un hash que el server no
        /// conoce deja de contar como "no lo tenes".
        /// </remarks>
        protected override IQueryable<BeatmapInfo> QueryUsableCopies(APIBeatmap onlineBeatmap)
        {
            var locales = Realm.Realm.All<BeatmapInfo>()
                               .NotDeleted()
                               .Filter($@"{nameof(BeatmapInfo.OnlineID)} == $0", onlineBeatmap.OnlineID);

            string hashDelServer = onlineBeatmap.MD5Hash;

            // Sin hash del lado del server no hay contra que verificar. Antes esto
            // significaba "nadie tiene el mapa"; ahora significa "no puedo verificar",
            // que es lo que realmente pasa.
            if (string.IsNullOrEmpty(hashDelServer))
                return locales;

            return locales.Filter(
                $@"{nameof(BeatmapInfo.MD5Hash)} == $0 OR {nameof(BeatmapInfo.MD5Hash)} == {nameof(BeatmapInfo.OnlineMD5Hash)}",
                hashDelServer);
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Availability.BindValueChanged(onBeatmapAvailabilityChanged);

            client.SettingsChanged += onSettingsChanged;
            onSettingsChanged(client.Room!.Settings);
        }

        private void onSettingsChanged(MultiplayerRoomSettings settings)
        {
            PlaylistItem.Value = new PlaylistItem(client.Room!.CurrentPlaylistItem);
            checkForAutomaticDownload(client.Room!.CurrentPlaylistItem);
        }

        private void onBeatmapAvailabilityChanged(ValueChangedEvent<BeatmapAvailability> availability)
        {
            client.ChangeBeatmapAvailability(availability.NewValue).FireAndForget();
        }

        private void checkForAutomaticDownload(MultiplayerPlaylistItem item)
        {
            // This method is called every time anything changes in the room.
            // This could result in download requests firing far too often, when we only expect them to fire once per beatmap.
            //
            // Without this check, we would see especially egregious behaviour when a user has hit the download rate limit.
            if (lastDownloadCheckedBeatmapId == item.BeatmapID)
                return;

            lastDownloadCheckedBeatmapId = item.BeatmapID;

            downloadCheckCancellation?.Cancel();

            if (beatmapManager.IsAvailableLocally(new APIBeatmap { OnlineID = item.BeatmapID }))
                return;

            // In a perfect world we'd use BeatmapAvailability, but there's no event-driven flow for when a selection changes.
            // ie. if selection changes from "not downloaded" to another "not downloaded" we wouldn't get a value changed raised.
            beatmapLookupCache
                .GetBeatmapAsync(item.BeatmapID, (downloadCheckCancellation = new CancellationTokenSource()).Token)
                .ContinueWith(resolved => Schedule(() =>
                {
                    APIBeatmapSet? beatmapSet = resolved.GetResultSafely()?.BeatmapSet;

                    if (beatmapSet == null)
                        return;

                    beatmapDownloader.Download(beatmapSet, config.Get<bool>(OsuSetting.PreferNoVideo));
                }));
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);

            if (client.IsNotNull())
                client.SettingsChanged -= onSettingsChanged;
        }
    }
}
