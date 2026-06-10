// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>A selectable store tile (cursor trail, name colour, ...), so the
    /// overlay can track selection and refresh badges generically.</summary>
    public interface IStoreCard
    {
        /// <summary>Stable id of the cosmetic this card represents.</summary>
        string ItemId { get; }

        /// <summary>Highlight / clear the selection border.</summary>
        void SetSelected(bool selected);

        /// <summary>Refresh owned / equipped badges in place (no rebuild).</summary>
        void RefreshState();
    }
}
