// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;

namespace osu.Game.Graphics.UserEffects
{
    /// <summary>
    /// A self-looping pulsing <see cref="Box"/> used as the persistent
    /// background layer for aura presets.
    ///
    /// Important: the alpha-pulse <c>Loop</c> transform MUST be set up inside
    /// <see cref="LoadComplete"/> rather than in the preset's
    /// <c>CreateBackground()</c>. <c>TransformSequence&lt;T&gt;.Loop()</c>
    /// reads <c>Transformable.Time</c> to compute its loop boundaries — and
    /// <c>Time</c> is null on a freshly-constructed drawable that hasn't been
    /// added to any parent / hasn't received a clock yet. Calling Loop pre-
    /// load throws NRE and crashes the entire game thread (this exact crash
    /// took down F9 / online users panel during testing).
    /// </summary>
    internal partial class PulsingHalo : Box
    {
        /// <summary>Alpha at the peak of the pulse.</summary>
        public float MaxAlpha { get; init; } = 0.18f;

        /// <summary>Alpha at the trough of the pulse.</summary>
        public float MinAlpha { get; init; } = 0.04f;

        /// <summary>Duration of one fade direction (so a full cycle is 2x this).</summary>
        public double DurationMs { get; init; } = 1400;

        public PulsingHalo()
        {
            // Start at the trough so the first visible breath is the fade-in.
            Alpha = 0;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            // Snap to the floor first so we never flash at full alpha for a
            // single frame before the loop starts.
            Alpha = MinAlpha;

            this.Loop(t => t
                .FadeTo(MaxAlpha, DurationMs, Easing.InOutSine)
                .Then()
                .FadeTo(MinAlpha, DurationMs, Easing.InOutSine));
        }
    }
}
