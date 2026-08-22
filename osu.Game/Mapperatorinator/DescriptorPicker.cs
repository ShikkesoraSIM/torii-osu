// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osuTK;

namespace osu.Game.Mapperatorinator
{
    /// <summary>
    /// Grouped descriptor chips so nobody has to know what to type: every tag the model
    /// understands, split by category (style, jumps, streams, tech...). Clicking a chip
    /// cycles off -> want (green) -> avoid (red), covering positive and negative
    /// descriptors in one control. Each chip tooltips the official description.
    /// </summary>
    public partial class DescriptorPicker : CompositeDrawable
    {
        public enum ChipState
        {
            Off,
            Want,
            Avoid,
        }

        private readonly Dictionary<string, ChipState> states = new Dictionary<string, ChipState>();
        private readonly List<DescriptorChip> chips = new List<DescriptorChip>();
        private FillFlowContainer content = null!;

        public IEnumerable<string> Wanted => states.Where(kv => kv.Value == ChipState.Want).Select(kv => kv.Key);
        public IEnumerable<string> Avoided => states.Where(kv => kv.Value == ChipState.Avoid).Select(kv => kv.Key);

        /// <summary>Only tags for this gamemode (plus mode-agnostic ones) are shown.</summary>
        public int GamemodeId
        {
            get => gamemodeId;
            set
            {
                if (gamemodeId == value) return;

                gamemodeId = value;
                if (IsLoaded)
                    rebuild();
            }
        }

        private int gamemodeId;

        [BackgroundDependencyLoader]
        private void load()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChild = content = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 6),
            };

            rebuild();
        }

        private void rebuild()
        {
            content.Clear();
            chips.Clear();

            var visible = MapperatorinatorDescriptors.ALL
                                                     .Where(d => d.RulesetId == null || d.RulesetId == gamemodeId)
                                                     .GroupBy(d => d.Name.Split('/')[0])
                                                     .OrderBy(g => g.Key);

            foreach (var group in visible)
            {
                content.Add(new OsuSpriteText
                {
                    Text = group.Key,
                    Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                    Colour = Colour4.White.Opacity(0.5f),
                    Margin = new MarginPadding { Top = 4 },
                });

                var flow = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Full,
                    Spacing = new Vector2(5, 5),
                };

                foreach (var descriptor in group)
                {
                    var chip = new DescriptorChip(descriptor, states.GetValueOrDefault(descriptor.Name))
                    {
                        StateChanged = s =>
                        {
                            if (s == ChipState.Off)
                                states.Remove(descriptor.Name);
                            else
                                states[descriptor.Name] = s;
                        },
                    };
                    chips.Add(chip);
                    flow.Add(chip);
                }

                content.Add(flow);
            }
        }

        private partial class DescriptorChip : CompositeDrawable, IHasTooltip
        {
            public Action<ChipState>? StateChanged;

            public LocalisableString TooltipText { get; }

            private readonly MapperatorinatorDescriptors.Descriptor descriptor;
            private ChipState state;
            private Box background = null!;
            private OsuSpriteText label = null!;

            public DescriptorChip(MapperatorinatorDescriptors.Descriptor descriptor, ChipState initialState)
            {
                this.descriptor = descriptor;
                state = initialState;
                TooltipText = descriptor.Description;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                AutoSizeAxes = Axes.Both;
                Masking = true;
                CornerRadius = 10;

                InternalChildren = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both },
                    label = new OsuSpriteText
                    {
                        // la categoria ya es el encabezado del grupo; el chip muestra solo el nombre
                        Text = descriptor.Name.Split('/').Last(),
                        Font = OsuFont.GetFont(size: 14),
                        Margin = new MarginPadding { Horizontal = 9, Vertical = 4 },
                    },
                };

                updateVisuals();
            }

            protected override bool OnClick(ClickEvent e)
            {
                state = state switch
                {
                    ChipState.Off => ChipState.Want,
                    ChipState.Want => ChipState.Avoid,
                    _ => ChipState.Off,
                };

                StateChanged?.Invoke(state);
                updateVisuals();
                return true;
            }

            protected override bool OnHover(HoverEvent e)
            {
                this.FadeTo(0.85f, 80);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                this.FadeTo(1, 80);
                base.OnHoverLost(e);
            }

            private void updateVisuals()
            {
                switch (state)
                {
                    case ChipState.Want:
                        background.Colour = Colour4.FromHex(@"3fb950");
                        label.Colour = Colour4.Black;
                        break;

                    case ChipState.Avoid:
                        background.Colour = Colour4.FromHex(@"e5534b");
                        label.Colour = Colour4.White;
                        break;

                    default:
                        background.Colour = Colour4.White.Opacity(0.08f);
                        label.Colour = Colour4.White.Opacity(0.75f);
                        break;
                }
            }
        }
    }
}
