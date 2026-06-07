// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>Shows the local player's username painted in a given name colour
    /// (animated for the rainbow style). Used as the store preview for a colour.</summary>
    public partial class NameColourText : CompositeDrawable
    {
        private readonly CosmeticNameColour colour;
        private readonly float fontSize;
        private OsuSpriteText text;

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
            text = new OsuSpriteText
            {
                Text = api?.LocalUser.Value?.Username ?? "Player",
                Font = OsuFont.GetFont(size: fontSize, weight: FontWeight.SemiBold),
            };

            // Role (Halo) colours get a soft white glow so they read as special.
            if (colour != null && colour.Style == NameColourStyle.Halo)
            {
                InternalChild = new Container
                {
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 4f,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Glow,
                        Colour = Color4.White.Opacity(0.55f),
                        Radius = 11f,
                    },
                    Child = text,
                };
            }
            else
                InternalChild = text;

            colour?.Apply(text, Time.Current);
        }

        protected override void Update()
        {
            base.Update();

            // Static styles are set once at load; only the rainbow needs ticking.
            if (colour != null && colour.Style == NameColourStyle.Rainbow)
                colour.Apply(text, Time.Current);
        }
    }
}
