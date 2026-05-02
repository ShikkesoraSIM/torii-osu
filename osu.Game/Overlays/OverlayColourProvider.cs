// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Graphics;
using osuTK.Graphics;

namespace osu.Game.Overlays
{
    public class OverlayColourProvider
    {
        /// <summary>
        /// The hue degree used for chrome shades (Background*, Dark*, Foreground*, Content*).
        /// </summary>
        public int Hue { get; private set; }

        /// <summary>
        /// The hue degree used for accent shades (Highlight1, Colour0–4, Light1–4).
        /// </summary>
        /// <remarks>
        /// Tracks <see cref="Hue"/> by default; donator-tier users can pin
        /// it independently via <see cref="ChangeAccentColourScheme(int)"/>
        /// so the bright accents differ from the chrome tint.
        /// </remarks>
        public int AccentHue { get; private set; }

        // Pinned by ChangeAccentColourScheme so that subsequent
        // ChangeColourScheme calls don't drag AccentHue back to the base.
        private bool accentHueOverridden;

        /// <summary>
        /// Fired when <see cref="Hue"/> or <see cref="AccentHue"/> changes and colour shades should be reapplied.
        /// </summary>
        public event Action? ColoursChanged;

        public OverlayColourProvider(OverlayColourScheme colourScheme)
            : this(colourScheme.GetHue())
        {
        }

        public OverlayColourProvider(int hue)
        {
            Hue = normaliseHue(hue);
            AccentHue = Hue;
        }

        // Note that the following five colours are also defined in `OsuColour` as `{colourScheme}{0,1,2,3,4}`.
        // The difference as to which should be used where comes down to context.
        // If the colour in question is supposed to always match the view in which it is displayed theme-wise, use `OverlayColourProvider`.
        // If the colour usage is special and in general differs from the surrounding view in choice of hue, use the `OsuColour` constants.
        // ── Accent shades (saturated) — driven by AccentHue. ──
        public Color4 Colour0 => getAccentColour(1, 0.8f);
        public Color4 Colour1 => getAccentColour(1, 0.7f);
        public Color4 Colour2 => getAccentColour(0.8f, 0.6f);
        public Color4 Colour3 => getAccentColour(0.6f, 0.5f);
        public Color4 Colour4 => getAccentColour(0.4f, 0.3f);

        public Color4 Highlight1 => getAccentColour(1, 0.7f);
        public Color4 Light1 => getAccentColour(0.4f, 0.8f);
        public Color4 Light2 => getAccentColour(0.4f, 0.75f);
        public Color4 Light3 => getAccentColour(0.4f, 0.7f);
        public Color4 Light4 => getAccentColour(0.4f, 0.5f);

        // ── Chrome shades (low-saturation) — driven by base Hue. Content*
        //    are nominally text colours sitting on top of dark backgrounds,
        //    so they belong with the chrome family rather than competing
        //    with the accent. ──
        public Color4 Content1 => getColour(0.4f, 1);
        public Color4 Content2 => getColour(0.4f, 0.9f);
        public Color4 Dark1 => getColour(0.2f, 0.35f);
        public Color4 Dark2 => getColour(0.2f, 0.3f);
        public Color4 Dark3 => getColour(0.2f, 0.25f);
        public Color4 Dark4 => getColour(0.2f, 0.2f);
        public Color4 Dark5 => getColour(0.2f, 0.15f);
        public Color4 Dark6 => getColour(0.2f, 0.1f);
        public Color4 Foreground1 => getColour(0.1f, 0.6f);
        public Color4 Background1 => getColour(0.1f, 0.4f);
        public Color4 Background2 => getColour(0.1f, 0.3f);
        public Color4 Background3 => getColour(0.1f, 0.25f);
        public Color4 Background4 => getColour(0.1f, 0.2f);
        public Color4 Background5 => getColour(0.1f, 0.15f);
        public Color4 Background6 => getColour(0.1f, 0.1f);

        /// <summary>
        /// Changes the <see cref="Hue"/> to a different degree.
        /// </summary>
        /// <param name="colourScheme">The proposed colour scheme.</param>
        public void ChangeColourScheme(OverlayColourScheme colourScheme) => ChangeColourScheme(colourScheme.GetHue());

        /// <summary>
        /// Changes the chrome <see cref="Hue"/> to a different degree.
        /// </summary>
        /// <remarks>
        /// If no accent hue was set independently via <see cref="ChangeAccentColourScheme(int)"/>,
        /// the accent keeps tracking this hue (1:1 with the legacy single-hue behaviour).
        /// </remarks>
        /// <param name="hue">The proposed hue degree.</param>
        public void ChangeColourScheme(int hue)
        {
            int normalisedHue = normaliseHue(hue);
            int newAccent = accentHueOverridden ? AccentHue : normalisedHue;

            if (Hue == normalisedHue && AccentHue == newAccent)
                return;

            Hue = normalisedHue;
            AccentHue = newAccent;
            ColoursChanged?.Invoke();
        }

        /// <summary>
        /// Sets the donator-only accent hue independently from the base <see cref="Hue"/>.
        /// </summary>
        /// <remarks>
        /// Subsequent calls to <see cref="ChangeColourScheme(int)"/> keep the accent
        /// pinned at <paramref name="hue"/> until <see cref="ResetAccentToBase"/> is called.
        /// </remarks>
        public void ChangeAccentColourScheme(int hue)
        {
            int normalisedHue = normaliseHue(hue);

            if (accentHueOverridden && AccentHue == normalisedHue)
                return;

            accentHueOverridden = true;
            AccentHue = normalisedHue;
            ColoursChanged?.Invoke();
        }

        /// <summary>
        /// Stops tracking an independent accent hue; the accent re-syncs to
        /// <see cref="Hue"/> immediately and follows future
        /// <see cref="ChangeColourScheme(int)"/> calls.
        /// </summary>
        public void ResetAccentToBase()
        {
            bool wasOverridden = accentHueOverridden;
            accentHueOverridden = false;

            if (!wasOverridden && AccentHue == Hue)
                return;

            if (AccentHue == Hue)
                return;

            AccentHue = Hue;
            ColoursChanged?.Invoke();
        }

        private static int normaliseHue(int hue)
        {
            int normalised = hue % 360;

            if (normalised < 0)
                normalised += 360;

            return normalised;
        }

        private Color4 getColour(float saturation, float lightness) => Framework.Graphics.Colour4.FromHSL(Hue / 360f, saturation, lightness);
        private Color4 getAccentColour(float saturation, float lightness) => Framework.Graphics.Colour4.FromHSL(AccentHue / 360f, saturation, lightness);
    }

    /// <summary>
    /// Helpers that wire arbitrary <see cref="Drawable"/> properties to live-update
    /// when the active <see cref="OverlayColourProvider.Hue"/> changes.
    /// </summary>
    /// <remarks>
    /// Background: <see cref="OverlayColourProvider"/> exposes plain <see cref="Color4"/>
    /// getters that recompute from <see cref="OverlayColourProvider.Hue"/>. When a
    /// component reads (say) <c>provider.Background4</c> at construction and
    /// assigns it to <c>Box.Colour</c>, that <see cref="Color4"/> snapshot is
    /// frozen — subsequent <see cref="OverlayColourProvider.ChangeColourScheme(int)"/>
    /// calls only mutate the provider, not the box. Re-applying every leaf
    /// colour by hand inside every BindHue callback is tedious and easy to miss.
    /// <para/>
    /// <see cref="BindThemeColour{T}(T, OverlayColourProvider, Func{OverlayColourProvider, Color4})"/>
    /// turns the wiring into a one-liner: it applies the colour selector
    /// immediately, re-applies it whenever <see cref="OverlayColourProvider.ColoursChanged"/>
    /// fires, and returns an <see cref="IDisposable"/> the caller stores in a
    /// field and disposes from its own <c>Dispose</c> override (matching the
    /// pre-existing <c>customUiHueBinding</c> ownership pattern).
    /// <para/>
    /// Performance: <see cref="OverlayColourProvider.ChangeColourScheme(int)"/>
    /// already early-outs on identical hues; the <c>CustomUIHue</c> bindable
    /// is an integer-precision float, so a full picker drag fires at most
    /// once per degree (≤360 ticks). The handler list is just delegate
    /// invocation, so even a few hundred subscribers stay well under a frame.
    /// </remarks>
    public static class OverlayColourProviderExtensions
    {
        /// <summary>
        /// Apply a colour from <paramref name="provider"/> to <paramref name="drawable"/>'s
        /// <see cref="Drawable.Colour"/> immediately, then re-apply on every theme change.
        /// Returns an <see cref="IDisposable"/>; store it and dispose from the consumer's
        /// own <c>Dispose</c> override so the subscription doesn't outlive the drawable.
        /// </summary>
        public static IDisposable BindThemeColour<T>(this T drawable, OverlayColourProvider provider, Func<OverlayColourProvider, Color4> selector)
            where T : Drawable
        {
            void apply() => drawable.Colour = selector(provider);
            apply();
            provider.ColoursChanged += apply;
            return new ColourSubscription(() => provider.ColoursChanged -= apply);
        }

        /// <summary>
        /// Generic variant for non-<see cref="Drawable.Colour"/> properties (gradients,
        /// child colour, text colour, wave colours, etc.). The <paramref name="apply"/>
        /// callback is invoked once now and again whenever the theme changes.
        /// </summary>
        public static IDisposable BindThemeColour<T>(this T drawable, OverlayColourProvider provider, Action<T, OverlayColourProvider> apply)
            where T : Drawable
        {
            void run() => apply(drawable, provider);
            run();
            provider.ColoursChanged += run;
            return new ColourSubscription(() => provider.ColoursChanged -= run);
        }

        private sealed class ColourSubscription : IDisposable
        {
            private Action? unsubscribe;

            public ColourSubscription(Action unsubscribe)
            {
                this.unsubscribe = unsubscribe;
            }

            public void Dispose()
            {
                unsubscribe?.Invoke();
                unsubscribe = null;
            }
        }
    }
}
