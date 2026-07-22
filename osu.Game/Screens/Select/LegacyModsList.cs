// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Mods;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.Select
{
    /// <summary>
    /// el readout de mods seleccionados de osu!stable en el song select: los nombres de los mods
    /// activos en texto blanco semi-transparente arriba del footer. stable: pText size 30 en
    /// (60, 397) en espacio 480, Color(255, 255, 255, 128), con borde de texto.
    /// </summary>
    public partial class LegacyModsList : CompositeDrawable
    {
        [Resolved]
        private Bindable<IReadOnlyList<Mod>> mods { get; set; } = null!;

        private OsuSpriteText text = null!;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.Both;

            InternalChild = text = new OsuSpriteText
            {
                Position = new Vector2(60, 397) * 1.6f,
                Font = LegacyFonts.Get(30 * 1.6f),
                Colour = new Color4(255, 255, 255, 128),
                Shadow = true,
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            mods.BindValueChanged(m => text.Text = string.Join(@",", m.NewValue.Select(mod => mod.Name)), true);
        }
    }
}
