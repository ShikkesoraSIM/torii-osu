// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Graphics
{
    /// <summary>
    /// Central switch between the default Torii palette and the
    /// "Grayscale by fsyori" reskin. Wraps a cheap static check
    /// against <see cref="OsuColour.IsGrayscaleTheme"/> so consumer
    /// call sites can read fluently:
    /// <code>
    /// CornerRadius = ThemeAware.Pick(toriiValue: 10f, grayscaleValue: 4f);
    /// </code>
    ///
    /// Why this exists as its own helper
    /// ---------------------------------
    /// fsyori's reskin branch (github.com/fsyori/osu reskin) is a 22-file
    /// diff: ~2 files of pure palette (OsuColour + OverlayColourProvider)
    /// and ~20 files of small +1/-1 changes to constants and inline
    /// colours (corner radii, button heights, hardcoded hex values).
    /// Pure colour fields already flow through <see cref="OsuColour"/>
    /// so the palette swap happens centrally there. The constant /
    /// inline-colour changes don't pass through any choke point, so we
    /// need a tiny per-call-site decision: "Torii value or fsyori
    /// value?". <see cref="Pick{T}"/> is that decision, named explicitly
    /// so a grep for "ThemeAware" finds every spot where the two
    /// themes intentionally differ.
    ///
    /// Why a generic <typeparamref name="T"/> instead of overloads per
    /// type: the helper is read at construction time on drawables, not
    /// in a hot loop. Generic dispatch costs nothing at runtime
    /// (resolved at JIT compile), but lets one method serve floats
    /// (corner radii), ints (heights), Vector2s (shear), Color4s
    /// (inline hex swaps), and any future type without an explosion
    /// of overloads.
    ///
    /// Restart-on-change invariant
    /// ---------------------------
    /// The grayscale flag is set ONCE at startup by
    /// <see cref="OsuColour.SetThemeFromConfig"/> and never mutated
    /// thereafter. The settings dropdown enforces a restart-confirm
    /// dialog precisely so that this remains true: every drawable
    /// captures the theme value at construction and a hot swap would
    /// leave the UI half-themed. Callers can rely on
    /// <see cref="Pick{T}"/> returning a stable answer for the
    /// lifetime of the process.
    /// </summary>
    public static class ThemeAware
    {
        /// <summary>
        /// Return <paramref name="torii"/> on the default theme,
        /// <paramref name="grayscale"/> when the user has opted into
        /// the "Grayscale by fsyori" palette. Midnight defaults to the
        /// grayscale-side value for structural constants (corner radii,
        /// layout switches) since it inherits the same reskin shape;
        /// call sites that need a distinct midnight value should use
        /// <see cref="PickAll{T}"/> instead.
        /// </summary>
        public static T Pick<T>(T torii, T grayscale)
            => OsuColour.UsesGrayscaleStructure ? grayscale : torii;

        /// <summary>
        /// Three-way picker for call sites where the midnight palette
        /// needs a different value from grayscale (typically inline
        /// colours, or a switch that depends on slanted-vs-flat).
        /// </summary>
        public static T PickAll<T>(T torii, T grayscale, T midnight)
        {
            if (OsuColour.IsMidnightTheme) return midnight;
            if (OsuColour.IsGrayscaleTheme) return grayscale;
            return torii;
        }

        /// <summary>
        /// Eje estructural del theme Dark Glass, ortogonal a <see cref="Pick{T}"/>: glass va
        /// para el lado OPUESTO de grayscale (esquinas mas redondas que vanilla, superficies
        /// translucidas) asi que no puede colgarse de UsesGrayscaleStructure. Devuelve
        /// <paramref name="glass"/> solo con Dark Glass activo; el resto de los themes
        /// (Torii, Grayscale, Midnight) recibe <paramref name="normal"/>, que en los call
        /// sites tipicos ya es a su vez un <see cref="Pick{T}"/>. Mismo invariante de
        /// restart-baked que el resto: leer solo en construccion.
        /// </summary>
        public static T PickGlass<T>(T normal, T glass)
            => OsuColour.IsGlassTheme ? glass : normal;

        /// <summary>
        /// Alpha del vidrio para FILLS de paneles: devuelve <paramref name="glassAlpha"/> con
        /// Dark Glass activo y 1 (opaco) en el resto. Uso tipico:
        /// <code>Colour = colourProvider.Background5.Opacity(ThemeAware.GlassAlpha(0.8f))</code>
        /// Se aplica POR SITIO (solo en fondos de panel) y nunca centralizado en
        /// OverlayColourProvider, porque los slots Background* tambien pintan foreground
        /// (iconos/texto) y esos deben quedar opacos. No usar en texto ni por debajo de ~0.7
        /// en superficies con texto encima (contraste sobre fondos brillantes).
        /// </summary>
        public static float GlassAlpha(float glassAlpha)
            => OsuColour.IsGlassTheme ? glassAlpha * LIQUID_CLARITY : 1f;

        /// <summary>
        /// LIQUID GLASS: escala GLOBAL de opacidad de todo el chrome de vidrio (menus, settings, chat,
        /// overlays, toolbar, footer, dialogs...). Mas bajo = MUCHO mas transparente/clear (se ve mas del
        /// blur/escena atras), menos "dark". Perilla unica: cambiala y todo el vidrio se re-tinta.
        /// 1.0 = como estaba; 0.5 = la mitad de tinte (mucho mas clear).
        /// </summary>
        public const float LIQUID_CLARITY = 0.5f;
    }
}
