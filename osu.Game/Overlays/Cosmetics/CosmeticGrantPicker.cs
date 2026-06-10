// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>A scrollable, searchable multi-select list of grantable cosmetics
    /// (trails / name colours / auras) for the access-code grant picker.</summary>
    public partial class CosmeticGrantPicker : CompositeDrawable
    {
        [Resolved(canBeNull: true)]
        private IAPIProvider api { get; set; }

        private readonly HashSet<string> selected = new HashSet<string>();

        public IReadOnlyCollection<string> Selected => selected;

        private (string id, string label)[] all;
        private FillFlowContainer list;
        private SearchTextBox search;
        private OsuSpriteText countLabel;
        private string filter = string.Empty;

        public CosmeticGrantPicker()
        {
            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            all = CosmeticUnlock.AllGrantable(api?.LocalUser.Value).ToArray();

            InternalChild = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 6),
                Children = new Drawable[]
                {
                    search = new SearchTextBox
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 32,
                        PlaceholderText = "Search cosmetics…",
                        HoldFocus = false,
                    },
                    countLabel = new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                        Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.X,
                        Height = 200,
                        Masking = true,
                        CornerRadius = BriefingTheme.CornerSm,
                        Children = new Drawable[]
                        {
                            new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black.Opacity(0.25f) },
                            new OsuScrollContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                ScrollbarVisible = true,
                                Child = list = new FillFlowContainer
                                {
                                    RelativeSizeAxes = Axes.X,
                                    AutoSizeAxes = Axes.Y,
                                    Direction = FillDirection.Vertical,
                                    Spacing = new Vector2(0, 2),
                                    Padding = new MarginPadding(6),
                                },
                            },
                        },
                    },
                },
            };

            search.Current.BindValueChanged(e =>
            {
                filter = e.NewValue?.Trim() ?? string.Empty;
                rebuild();
            });

            rebuild();
        }

        private void rebuild()
        {
            list.Clear();

            foreach (var (id, label) in all)
            {
                if (!string.IsNullOrEmpty(filter) && !label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                list.Add(new Row(label, selected.Contains(id), on =>
                {
                    if (on)
                        selected.Add(id);
                    else
                        selected.Remove(id);
                    updateCount();
                }));
            }

            updateCount();
        }

        private void updateCount()
        {
            countLabel.Text = selected.Count == 0 ? "None selected" : $"{selected.Count} selected";
        }

        private partial class Row : OsuClickableContainer
        {
            private readonly string label;
            private readonly Action<bool> onToggle;
            private bool on;

            private Box bg;
            private SpriteIcon check;

            public Row(string label, bool on, Action<bool> onToggle)
            {
                this.label = label;
                this.on = on;
                this.onToggle = onToggle;
                RelativeSizeAxes = Axes.X;
                Height = 30;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Masking = true;
                CornerRadius = 4f;
                Children = new Drawable[]
                {
                    bg = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Transparent },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 10,
                        Text = label,
                        Font = OsuFont.GetFont(size: BriefingTheme.TypeBody),
                    },
                    check = new SpriteIcon
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        X = -10,
                        Icon = FontAwesome.Solid.Check,
                        Size = new Vector2(12),
                        Colour = BriefingTheme.AccentGain,
                    },
                };

                updateVisual();
            }

            private void updateVisual()
            {
                bg.Colour = on ? BriefingTheme.AccentGain.Opacity(0.18f) : Color4.Transparent;
                check.Alpha = on ? 1 : 0;
            }

            protected override bool OnClick(ClickEvent e)
            {
                on = !on;
                onToggle?.Invoke(on);
                updateVisual();
                return true;
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (!on)
                    bg.FadeColour(Color4.White.Opacity(0.08f), 100);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                if (!on)
                    bg.FadeColour(Color4.Transparent, 140);
                base.OnHoverLost(e);
            }
        }
    }
}
