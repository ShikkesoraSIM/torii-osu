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
        /// the "Grayscale by fsyori" palette.
        /// </summary>
        public static T Pick<T>(T torii, T grayscale)
            => OsuColour.IsGrayscaleTheme ? grayscale : torii;
    }
}
