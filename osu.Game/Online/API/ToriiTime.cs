// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Globalization;

namespace osu.Game.Online.API
{
    /// <summary>
    /// Torii: leer timestamps del server asumiendo UTC cuando no viene la zona.
    /// </summary>
    /// <remarks>
    /// El server escribe en UTC y MySQL guarda DATETIME sin zona, asi que si un
    /// endpoint se olvida de marcarla el JSON sale con la hora pelada. Parseado de
    /// la forma normal, un timestamp sin offset se toma como hora LOCAL de quien
    /// juega, y termina corrido por el huso de cada persona.
    ///
    /// Falla distinto segun donde vivis, que es lo que lo hace dificil de ver. Nos
    /// paso con el cartel de puntos: filtra lo ganado hace menos de 10 minutos, y a
    /// alguien en UTC+8 un evento de hace 30 segundos le daba 8 horas y media, asi
    /// que no aparecia NUNCA. Al oeste de UTC daba negativo y pasaba de casualidad
    /// por un guard de reloj adelantado. Estuvo roto para medio mundo una semana
    /// sin que nadie lo atara a la zona horaria.
    ///
    /// El server ya manda la zona; esto es el cinturon de seguridad para que el
    /// proximo endpoint que se la olvide no vuelva a romper lo mismo en silencio.
    ///
    /// Se parsea el STRING crudo a proposito, en vez de un JsonConverter sobre
    /// DateTimeOffset: Newtonsoft puede convertir el token a fecha antes de que un
    /// converter llegue a verlo, y para entonces ya le aplico el huso local y la
    /// informacion de si venia o no con zona se perdio.
    /// </remarks>
    public static class ToriiTime
    {
        /// <summary>
        /// Parsea un timestamp ISO. Si trae offset se respeta; si no, se asume UTC.
        /// Devuelve <c>default</c> si el texto no es una fecha.
        /// </summary>
        public static DateTimeOffset ParseAssumingUtc(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            // AssumeUniversal es lo que cambia el comportamiento: sin offset se toma
            // como UTC en lugar de local. AdjustToUniversal normaliza para comparar.
            return DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed)
                ? parsed
                : default;
        }
    }
}
