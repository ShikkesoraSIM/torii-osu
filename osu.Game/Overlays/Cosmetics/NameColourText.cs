// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>Shows the local player's username painted in a given name colour,
    /// using the SAME component the profile header uses
    /// (<see cref="GlowingFreeWidthSpriteText"/>). Role colours therefore get the exact
    /// profile look (colour on the text + matching additive glow); buyable colours
    /// render flat. The particle aura (hearts) is a separate cosmetic, not part of
    /// the name colour.</summary>
    public partial class NameColourText : CompositeDrawable
    {
        private readonly CosmeticNameColour colour;
        private readonly float fontSize;
        private GlowingFreeWidthSpriteText text;

        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        public NameColourText(CosmeticNameColour colour, float fontSize = 20f)
        {
            this.colour = colour;
            this.fontSize = fontSize;
            AutoSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChild = text = new GlowingFreeWidthSpriteText
            {
                Font = OsuFont.GetFont(size: fontSize, weight: FontWeight.SemiBold),
                Text = api?.LocalUser.Value?.Username ?? "Player",
            };

            colour?.Apply(text, Time.Current);
        }

        protected override void Update()
        {
            base.Update();

            // Animated styles AND the pulsing role glow need per-frame repaint;
            // plain solids/gradients are set once at load.
            if (text != null && colour != null
                && (colour.Style == NameColourStyle.Rainbow
                    || colour.Style == NameColourStyle.Pulse
                    || colour.Style == NameColourStyle.Halo))
                colour.Apply(text, Time.Current);
        }
    }
}
