// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Input.Bindings;
using osu.Game.Overlays.ToriiBriefing;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Overlays.Cosmetics
{
    /// <summary>
    /// Admin-only panel for curating the cosmetic store: toggle which catalog
    /// items are in the store pool (eligible to appear for sale), with search,
    /// per-section counts and bulk show/hide. Visibility is gated by the caller
    /// on <c>APIUser.IsAdmin</c> (the server's authoritative flag).
    ///
    /// Persistence is LOCAL for now (via <see cref="ToriiCosmeticsManager"/>), so
    /// it only curates this client. When the server owns the store config this
    /// swaps to a server round-trip so the curation applies for everyone.
    /// </summary>
    public partial class CosmeticAdminOverlay : OsuFocusedOverlayContainer
    {
        protected override string PopInSampleName => @"UI/overlay-big-pop-in";
        protected override string PopOutSampleName => @"UI/overlay-big-pop-out";
        public override bool BlockScreenWideMouse => true;

        [Resolved(canBeNull: true)]
        private ToriiCosmeticsManager cosmetics { get; set; }

        private BriefingGlass mainPanel;
        private FillFlowContainer sectionsFlow;
        private SearchTextBox search;
        private OsuSpriteText emptyHint;

        private readonly List<Section> sections = new List<Section>();
        private string filter = string.Empty;

        public CosmeticAdminOverlay()
        {
            RelativeSizeAxes = Axes.Both;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            InternalChildren = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = ColourInfo.GradientVertical(Color4.Black.Opacity(0.55f), Color4.Black.Opacity(0.7f)),
                },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    Size = new Vector2(0.6f, 0.84f),
                    Children = new Drawable[]
                    {
                        mainPanel = new BriefingGlass
                        {
                            RelativeSizeAxes = Axes.Both,
                            RelativeContentSize = Axes.Both,
                            CornerSize = BriefingTheme.CornerLg,
                            ShadowOpacity = 0.4f,
                            ShadowRadius = 30,
                            Child = new GridContainer
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding(BriefingTheme.SpacingLg),
                                RowDimensions = new[]
                                {
                                    new Dimension(GridSizeMode.AutoSize),
                                    new Dimension(GridSizeMode.Absolute, BriefingTheme.SpacingMd),
                                    new Dimension(),
                                },
                                Content = new[]
                                {
                                    new Drawable[] { createHeader() },
                                    new Drawable[] { Empty() },
                                    new Drawable[]
                                    {
                                        new OsuScrollContainer
                                        {
                                            RelativeSizeAxes = Axes.Both,
                                            ScrollbarVisible = false,
                                            Child = new Container
                                            {
                                                RelativeSizeAxes = Axes.X,
                                                AutoSizeAxes = Axes.Y,
                                                Children = new Drawable[]
                                                {
                                                    sectionsFlow = new FillFlowContainer
                                                    {
                                                        RelativeSizeAxes = Axes.X,
                                                        AutoSizeAxes = Axes.Y,
                                                        Direction = FillDirection.Vertical,
                                                        Spacing = new Vector2(0, 5),
                                                    },
                                                    emptyHint = new OsuSpriteText
                                                    {
                                                        Anchor = Anchor.TopCentre,
                                                        Origin = Anchor.TopCentre,
                                                        Margin = new MarginPadding { Top = 30 },
                                                        Text = "No items match your search.",
                                                        Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, italics: true),
                                                        Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                                                        Alpha = 0,
                                                    },
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                        new CloseButton
                        {
                            Anchor = Anchor.TopRight,
                            Origin = Anchor.TopRight,
                            Margin = new MarginPadding(12),
                            Action = Hide,
                        },
                    },
                },
            };

            search.Current.BindValueChanged(e =>
            {
                filter = e.NewValue?.Trim() ?? string.Empty;
                rebuildSections();
            });

            rebuildSections();
        }

        private Drawable createHeader() => new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 6),
            Children = new Drawable[]
            {
                new OsuSpriteText
                {
                    Text = "Store Admin",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeDisplay, weight: FontWeight.SemiBold),
                },
                new OsuSpriteText
                {
                    Text = "Choose which items can appear in the store. Local only for now.",
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption),
                    Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                },
                search = new SearchTextBox
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 34,
                    Margin = new MarginPadding { Top = 4 },
                    PlaceholderText = "Search items…",
                    HoldFocus = false,
                },
            },
        };

        private void rebuildSections()
        {
            if (sectionsFlow == null)
                return;

            sections.Clear();
            sectionsFlow.Clear();

            addSection("Cursor Trails", FontAwesome.Solid.PaintBrush, BriefingTheme.AccentPink,
                CosmeticCatalog.Trails.Select(t => (t.Id, t.Name)));

            addSection("Name Colours", FontAwesome.Solid.Palette, BriefingTheme.AccentSky,
                CosmeticNameColourCatalog.Buyable.Select(c => (c.Id, c.Name)));

            addSection("Auras", FontAwesome.Solid.Sun, BriefingTheme.AccentAmber,
                BuyableAuraCatalog.All.Where(e => e.Preset != null).Select(e => (e.Id, AuraCard.DisplayNameFor(e.Id))));

            bool anyVisible = sections.Any(s => s.Block.Parent != null);
            emptyHint.Alpha = anyVisible ? 0 : 1;
        }

        private void addSection(string title, IconUsage icon, Color4 accent, IEnumerable<(string id, string label)> items)
        {
            var all = items.ToList();

            // Apply the search filter to this section's items.
            var shown = string.IsNullOrEmpty(filter)
                ? all
                : all.Where(i => i.label.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            if (shown.Count == 0)
                return;

            var section = new Section { Ids = all.Select(i => i.id).ToList() };

            var rows = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
            };

            foreach (var (id, label) in shown)
            {
                bool enabled = cosmetics?.IsStoreEnabled(id) ?? true;
                rows.Add(new ToggleRow(label, icon, accent, enabled, v =>
                {
                    cosmetics?.SetStoreEnabled(id, v);
                    updateCount(section);
                }));
            }

            section.Block = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(0, 4),
                Margin = new MarginPadding { Top = 8 },
                Children = new Drawable[]
                {
                    sectionHeader(section, title, icon, accent),
                    rows,
                },
            };

            sections.Add(section);
            sectionsFlow.Add(section.Block);
            updateCount(section);
        }

        private Drawable sectionHeader(Section section, string title, IconUsage icon, Color4 accent) => new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children = new Drawable[]
            {
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreLeft,
                    Origin = Anchor.CentreLeft,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(8, 0),
                    Children = new Drawable[]
                    {
                        new SpriteIcon
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Icon = icon,
                            Size = new Vector2(13),
                            Colour = accent,
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Text = title,
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeTitle, weight: FontWeight.SemiBold),
                        },
                        section.CountLabel = new OsuSpriteText
                        {
                            Anchor = Anchor.CentreLeft,
                            Origin = Anchor.CentreLeft,
                            Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.SemiBold),
                            Colour = Color4.White.Opacity(BriefingTheme.InkSecondary),
                        },
                    },
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.CentreRight,
                    Origin = Anchor.CentreRight,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Horizontal,
                    Spacing = new Vector2(12, 0),
                    Children = new Drawable[]
                    {
                        new TextButton("Show all", () => bulk(section, true)),
                        new TextButton("Hide all", () => bulk(section, false)),
                    },
                },
            },
        };

        private void bulk(Section section, bool enabled)
        {
            // One batched change (single store rebuild) instead of one per item.
            cosmetics?.SetStoreEnabledBatch(section.Ids, enabled);

            // Rebuild so every row's pill reflects the new state (keeps the
            // current search filter applied).
            rebuildSections();
        }

        private void updateCount(Section section)
        {
            if (section.CountLabel == null)
                return;

            int total = section.Ids.Count;
            int live = section.Ids.Count(id => cosmetics?.IsStoreEnabled(id) ?? true);

            section.CountLabel.Text = $"{live}/{total} in store";
            section.CountLabel.Colour = (live == total ? BriefingTheme.AccentGain
                : live == 0 ? new Color4(150, 155, 165, 255)
                : BriefingTheme.AccentAmber).Opacity(0.9f);
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (mainPanel != null && !mainPanel.ReceivePositionalInputAt(e.ScreenSpaceMousePosition))
            {
                Hide();
                return true;
            }

            return base.OnClick(e);
        }

        public override bool OnPressed(KeyBindingPressEvent<GlobalAction> e)
        {
            if (!e.Repeat && e.Action == GlobalAction.Back && State.Value == Visibility.Visible)
            {
                Hide();
                return true;
            }

            return base.OnPressed(e);
        }

        protected override void PopIn()
        {
            this.FadeIn(BriefingTheme.HoverDuration, Easing.OutQuint);
            mainPanel.ScaleTo(0.94f).ScaleTo(1f, BriefingTheme.EntranceDuration, Easing.OutBack)
                     .MoveToY(20).MoveToY(0, BriefingTheme.EntranceDuration, Easing.OutQuint);
            rebuildSections();
        }

        protected override void PopOut()
        {
            this.FadeOut(BriefingTheme.DismissDuration, Easing.OutQuint);
            mainPanel.ScaleTo(0.97f, BriefingTheme.DismissDuration, Easing.OutQuint);
        }

        private class Section
        {
            public List<string> Ids;
            public OsuSpriteText CountLabel;
            public Drawable Block;
        }

        // One catalog item: type icon + name on the left, a clickable in-store /
        // hidden pill on the right. The whole row is the hit target.
        private partial class ToggleRow : OsuClickableContainer
        {
            private readonly string label;
            private readonly IconUsage icon;
            private readonly Color4 accent;
            private readonly Action<bool> onToggle;
            private bool enabled;

            private Box rowBg;
            private Box pillBg;
            private OsuSpriteText pillText;
            private SpriteIcon pillIcon;

            public ToggleRow(string label, IconUsage icon, Color4 accent, bool enabled, Action<bool> onToggle)
            {
                this.label = label;
                this.icon = icon;
                this.accent = accent;
                this.enabled = enabled;
                this.onToggle = onToggle;
                RelativeSizeAxes = Axes.X;
                Height = 38;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Masking = true;
                CornerRadius = BriefingTheme.CornerSm;
                Children = new Drawable[]
                {
                    rowBg = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White.Opacity(0.04f) },
                    new FillFlowContainer
                    {
                        Anchor = Anchor.CentreLeft,
                        Origin = Anchor.CentreLeft,
                        X = 12,
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(9, 0),
                        Children = new Drawable[]
                        {
                            new SpriteIcon
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Icon = icon,
                                Size = new Vector2(12),
                                Colour = accent.Opacity(0.85f),
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                Text = label,
                                Font = OsuFont.GetFont(size: BriefingTheme.TypeBody, weight: FontWeight.SemiBold),
                            },
                        },
                    },
                    new Container
                    {
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        X = -10,
                        AutoSizeAxes = Axes.Both,
                        Masking = true,
                        CornerRadius = 5f,
                        Children = new Drawable[]
                        {
                            pillBg = new Box { RelativeSizeAxes = Axes.Both },
                            new FillFlowContainer
                            {
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(5, 0),
                                Margin = new MarginPadding { Horizontal = 9, Vertical = 4 },
                                Children = new Drawable[]
                                {
                                    pillIcon = new SpriteIcon
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Size = new Vector2(10),
                                        Colour = Color4.Black.Opacity(0.85f),
                                    },
                                    pillText = new OsuSpriteText
                                    {
                                        Anchor = Anchor.Centre,
                                        Origin = Anchor.Centre,
                                        Font = OsuFont.GetFont(size: 11, weight: FontWeight.Bold),
                                        Colour = Color4.Black.Opacity(0.85f),
                                    },
                                },
                            },
                        },
                    },
                };

                updatePill();
            }

            private void updatePill()
            {
                pillBg.Colour = (enabled ? BriefingTheme.AccentGain : new Color4(150, 155, 165, 255)).Opacity(0.92f);
                pillText.Text = enabled ? "IN STORE" : "HIDDEN";
                pillIcon.Icon = enabled ? FontAwesome.Solid.Check : FontAwesome.Solid.EyeSlash;
            }

            protected override bool OnClick(ClickEvent e)
            {
                enabled = !enabled;
                onToggle?.Invoke(enabled);
                updatePill();
                return true;
            }

            protected override bool OnHover(HoverEvent e)
            {
                rowBg.FadeColour(Color4.White.Opacity(0.1f), 120, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                rowBg.FadeColour(Color4.White.Opacity(0.04f), 160, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }

        // Small inline text action (Show all / Hide all).
        private partial class TextButton : OsuClickableContainer
        {
            private readonly string label;
            private OsuSpriteText text;

            public TextButton(string label, Action action)
            {
                this.label = label;
                Action = action;
                AutoSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Child = text = new OsuSpriteText
                {
                    Text = label,
                    Font = OsuFont.GetFont(size: BriefingTheme.TypeCaption, weight: FontWeight.Bold),
                    Colour = BriefingTheme.AccentPink,
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                text.FadeColour(Color4.White, 120, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                text.FadeColour(BriefingTheme.AccentPink, 160, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }

        // Round corner "X" close button (mirrors the store overlay's).
        private partial class CloseButton : OsuClickableContainer
        {
            private Box bg;

            public CloseButton()
            {
                Size = new Vector2(30);
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                Child = new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Masking = true,
                    Children = new Drawable[]
                    {
                        bg = new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.Black.Opacity(0.4f) },
                        new SpriteIcon
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Icon = FontAwesome.Solid.Times,
                            Size = new Vector2(13),
                            Colour = Color4.White,
                        },
                    },
                };
            }

            protected override bool OnHover(HoverEvent e)
            {
                bg.FadeColour(BriefingTheme.AccentPink, 150, Easing.OutQuint);
                this.ScaleTo(1.1f, 150, Easing.OutQuint);
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e)
            {
                bg.FadeColour(Color4.Black.Opacity(0.4f), 200, Easing.OutQuint);
                this.ScaleTo(1f, 200, Easing.OutQuint);
                base.OnHoverLost(e);
            }
        }
    }
}
