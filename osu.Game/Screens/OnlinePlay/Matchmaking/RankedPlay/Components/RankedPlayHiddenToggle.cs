// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Screens.OnlinePlay.Matchmaking.RankedPlay.Components
{
    /// <summary>
    /// torii: toggle simple de HD para ranked play (en vez del mod-select completo). Gris cuando esta
    /// off, verde cuando esta on, con el icono + "HD". El estado visual lo maneja el padre (server-
    /// authoritative via <see cref="SetActive"/>); el click solo pide el cambio via <see cref="OnToggle"/>.
    /// </summary>
    public partial class RankedPlayHiddenToggle : OsuClickableContainer
    {
        /// <summary>Invocado al clickear con el estado DESEADO (true = prender HD, false = apagar).</summary>
        public Action<bool>? OnToggle;

        private static readonly Color4 off_colour = new Color4(0.22f, 0.22f, 0.26f, 1f);
        private static readonly Color4 on_colour = new Color4(0.20f, 0.72f, 0.38f, 1f);

        private Box background = null!;
        private SpriteIcon icon = null!;
        private OsuSpriteText label = null!;

        private bool active;

        public RankedPlayHiddenToggle()
        {
            AutoSizeAxes = Axes.None;
            Size = new Vector2(150, 42);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Masking = true;
            CornerRadius = 8;
            BorderThickness = 2;

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = off_colour,
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(7, 0),
                    Children = new Drawable[]
                    {
                        icon = new SpriteIcon
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Icon = FontAwesome.Solid.LowVision,
                            Size = new Vector2(18),
                        },
                        label = new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = "HD",
                            Font = OsuFont.Torus.With(size: 20, weight: FontWeight.Bold),
                            UseFullGlyphHeight = false,
                        }
                    }
                }
            };

            Action = () => OnToggle?.Invoke(!active);

            updateVisual();
        }

        /// <summary>Setea el estado visual (server-authoritative), sin disparar <see cref="OnToggle"/>.</summary>
        public void SetActive(bool value)
        {
            if (active == value)
                return;

            active = value;
            updateVisual();
        }

        private void updateVisual()
        {
            background.FadeColour(active ? on_colour : off_colour, 150, Easing.OutQuint);

            Color4 fg = active ? Color4.White : new Color4(0.62f, 0.62f, 0.68f, 1f);
            icon.FadeColour(fg, 150);
            label.FadeColour(fg, 150);
            BorderColour = active ? on_colour.Lighten(0.2f) : new Color4(0.35f, 0.35f, 0.4f, 1f);
        }
    }
}
