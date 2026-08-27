// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace osu.Game.Rulesets
{
    /// <summary>
    /// Lleva la cuenta de los rulesets que no son los nuestros.
    /// </summary>
    /// <remarks>
    /// Un ruleset es un dll que el juego carga y ejecuta con todos los permisos: puede leer
    /// el mapa entero antes de tiempo, mover el cursor, decidir cuando se acierta. O sea que
    /// es relax, aim assist y lo que se te ocurra, sin necesidad de tocar nada del juego. Por
    /// eso Torii no deja conectarse con uno puesto.
    ///
    /// La deteccion va por NOMBRE DE ASSEMBLY y no por carpeta a proposito: dejar el dll al
    /// lado del ejecutable en vez de en "rulesets" lo cargaba igual, asi que mirar la carpeta
    /// se esquiva moviendo un archivo.
    ///
    /// Ojo con lo obvio: esto corre en la maquina del jugador, asi que alguien que compile su
    /// propio cliente lo saca y listo. No es la barrera, es el aviso. La barrera de verdad es
    /// que el server solo acepta hashes de clientes nuestros: tocar esto cambia el hash de
    /// osu.Game.dll y el login se cae del otro lado. Las dos piezas juntas cierran; cada una
    /// sola no sirve para nada.
    /// </remarks>
    public static class CustomRulesetGuard
    {
        /// <summary>
        /// Los unicos rulesets que Torii reconoce como propios.
        /// </summary>
        private static readonly ImmutableHashSet<string> official_rulesets = ImmutableHashSet.Create(
            StringComparer.OrdinalIgnoreCase,
            @"osu.Game.Rulesets.Osu",
            @"osu.Game.Rulesets.Taiko",
            @"osu.Game.Rulesets.Catch",
            @"osu.Game.Rulesets.Mania"
        );

        private static ImmutableArray<string> detected = ImmutableArray<string>.Empty;

        /// <summary>
        /// Los rulesets ajenos que se encontraron, por nombre de assembly. Vacio si esta todo limpio.
        /// </summary>
        public static ImmutableArray<string> Detected => detected;

        public static bool Any => !detected.IsEmpty;

        /// <summary>
        /// Registra los assemblies de ruleset cargados y se queda con los que no son nuestros.
        /// </summary>
        public static void Record(IEnumerable<Assembly> loaded)
            => detected = Filter(loaded.Select(a => a.GetName().Name));

        /// <summary>
        /// Se queda con los nombres que no son de un ruleset nuestro.
        /// </summary>
        /// <remarks>
        /// Separado de <see cref="Record"/> para poder probarlo sin tener que cargar
        /// assemblies de verdad, que es lo que hace que este tipo de guarda no se pruebe
        /// nunca y despues no funcione el dia que importa.
        /// </remarks>
        public static ImmutableArray<string> Filter(IEnumerable<string?> assemblyNames)
            => assemblyNames
               .Where(n => !string.IsNullOrEmpty(n) && !official_rulesets.Contains(n!))
               .Select(n => n!)
               .Distinct(StringComparer.OrdinalIgnoreCase)
               .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
               .ToImmutableArray();
    }
}
