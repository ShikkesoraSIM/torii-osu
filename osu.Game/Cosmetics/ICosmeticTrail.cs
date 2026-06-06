// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osuTK;

namespace osu.Game.Cosmetics
{
    /// <summary>
    /// Common surface for every cursor-trail cosmetic, whatever it renders with
    /// (the smooth ribbon trail or the shaped particle trail). Lets the shop /
    /// preview drive and tune any trail without caring about its kind.
    /// </summary>
    public interface ICosmeticTrail
    {
        /// <summary>Push the trail to a screen-space position. Call every frame
        /// from real mouse input or a synthetic driver.</summary>
        void Drive(Vector2 screenSpacePosition);

        /// <summary>Set the trail's length as a 0..1 scale, where 1 is the
        /// trail's catalog default and 0 is a short, FIXED floor that is the
        /// same wall-clock duration for every trail. Length is time-based (like
        /// osu!'s trail), so the minimum feels identical whatever the trail.</summary>
        void SetLengthScale(float scale01);

        /// <summary>Scale how densely parts / particles are emitted (count per
        /// unit of cursor travel). Not all trails support this (a continuous
        /// ribbon has no meaningful density) — see <see cref="CosmeticTrailFamily"/>.</summary>
        void SetDensityMultiplier(float multiplier);

        /// <summary>Scale the visual SIZE / thickness of the trail (dot size,
        /// ribbon width, particle scale).</summary>
        void SetSizeMultiplier(float multiplier);

        /// <summary>Drop the current path so the next Drive starts fresh (used
        /// when handing control between a synthetic driver and the real cursor,
        /// to avoid a streak across the gap).</summary>
        void Reset();

        /// <summary>Enable/disable reacting to the REAL mouse. The equipped
        /// cursor trail leaves this on (it follows the cursor); shop previews
        /// turn it OFF so they only follow their synthetic <see cref="Drive"/>
        /// and don't go haywire when you move over them.</summary>
        void SetInputActive(bool active);

        /// <summary>Freeze all per-frame work (path rebuilds, fade, emission).
        /// Shop previews pause while their card is scrolling/off-screen so a
        /// fast scroll through the grid doesn't rebuild a dozen trails a frame.</summary>
        void SetPaused(bool paused);
    }

    /// <summary>Which render family a trail belongs to, so the shop can show only
    /// the customisation sliders that make sense for it.</summary>
    public enum CosmeticTrailFamily
    {
        /// <summary>Soft dot trail (osu!-style). Length + density + size.</summary>
        Dot,

        /// <summary>Connected smooth ribbon. Length + size (no meaningful density).</summary>
        Ribbon,

        /// <summary>Shaped particles. Length + density + size.</summary>
        Particle,
    }
}
