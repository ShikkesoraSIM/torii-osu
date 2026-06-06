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

        /// <summary>Scale the trail's tail length / particle lifetime.</summary>
        void SetLengthMultiplier(float multiplier);

        /// <summary>Scale how densely parts / particles are emitted.</summary>
        void SetDensityMultiplier(float multiplier);

        /// <summary>Drop the current path so the next Drive starts fresh (used
        /// when handing control between a synthetic driver and the real cursor,
        /// to avoid a streak across the gap).</summary>
        void Reset();
    }
}
