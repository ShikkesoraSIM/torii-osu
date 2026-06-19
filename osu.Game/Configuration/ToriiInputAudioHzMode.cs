// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.ComponentModel;

namespace osu.Game.Configuration
{
    /// <summary>
    /// Torii: selectable rate (Hz) for the input / audio / update thread pipeline.
    /// The enum values ARE the Hz, so the chosen value can be fed straight into
    /// <c>GameHost.ToriiInputAudioHz</c> via a plain cast.
    /// </summary>
    public enum ToriiInputAudioHzMode
    {
        [Description("500 Hz")]
        Hz500 = 500,

        [Description("1000 Hz")]
        Hz1000 = 1000,

        [Description("2000 Hz (recommended)")]
        Hz2000 = 2000,

        [Description("4000 Hz")]
        Hz4000 = 4000,

        [Description("8000 Hz")]
        Hz8000 = 8000,
    }

    /// <summary>
    /// torii: auto-tune del primer arranque para <see cref="ToriiInputAudioHzMode"/>. una pc vieja o
    /// floja no tiene por que abrir a los 2000 hz del default competitivo y empezar a tironear. el
    /// numero de cores es la senial principal (correr input/audio/update todo a rate alto pelea por
    /// cores); un piso de ram agarra las maquinas genuinamente viejas. a proposito conservador: solo
    /// baja a las claramente flojas, un desktop moderno normal queda en 2000. se usa una sola vez para
    /// sembrar el default; despues de eso gana siempre la eleccion del usuario en el dropdown.
    /// </summary>
    public static class ToriiInputAudioHzDefaults
    {
        public static ToriiInputAudioHzMode ForThisMachine()
        {
            int cores = Environment.ProcessorCount;
            double ramGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024d * 1024 * 1024);

            // dual-core o casi nada de ram: genuinamente vieja / muy floja.
            if (cores <= 2 || ramGb < 4)
                return ToriiInputAudioHzMode.Hz500;

            // quad-core economico viejo (pocos cores Y poca ram): a la segura en el rate de lazer.
            if (cores <= 4 && ramGb < 8)
                return ToriiInputAudioHzMode.Hz1000;

            return ToriiInputAudioHzMode.Hz2000;
        }
    }
}
