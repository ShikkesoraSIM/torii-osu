// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

namespace osu.Game.Skinning
{
    /// <summary>
    /// Marker interface for skin components added by Torii (i.e. not
    /// inherited from upstream lazer).
    ///
    /// The in-game skin layout editor's component toolbox checks for
    /// this and visually flags matching entries — small torii-gate
    /// glyph next to the name + brand-pink colour — so users can tell
    /// at a glance which components are bonus Torii additions versus
    /// the standard lazer set. Saves having to read 20+ class names
    /// to find "the new one we just shipped" after each release.
    ///
    /// Apply by inheriting on any component that also implements
    /// <see cref="ISerialisableDrawable"/>; no other wiring needed.
    /// </summary>
    public interface IToriiSkinComponent
    {
    }
}
