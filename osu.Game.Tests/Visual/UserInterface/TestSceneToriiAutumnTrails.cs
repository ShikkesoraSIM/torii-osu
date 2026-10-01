// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.Cosmetics;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tests.Visual.UserInterface
{
    /// <summary>
    /// Autumn 2026 store refresh, trails: what is in the store today, what retires and the new
    /// candidates, each group in its own step so they can be compared without mixing.
    /// </summary>
    /// <remarks>
    /// These are CANDIDATES on purpose: more than will ship, so the pick is made by looking rather
    /// than imagining. Each cell shows the proposed tier, price and rarity; rarity matters because
    /// the store builds the daily rotation per rarity (3 common, 2 uncommon, 2 rare, 1 epic,
    /// 1 legendary), so a tier with no candidates leaves that slot empty.
    /// Same CosmeticTrailPreview as the store, same synthetic path for every trail.
    /// </remarks>
    [TestFixture]
    public partial class TestSceneToriiAutumnTrails : OsuTestScene
    {
        private FillFlowContainer grid = null!;
        private Box background = null!;
        private float speed = 1f;

        /// <summary>The candidates of this batch, grouped the way they get compared.</summary>
        private static readonly (string header, string[] ids)[] autumn_groups =
        {
            ("Basic · solids (300)", new[] { "trail-maple", "trail-amber", "trail-copper", "trail-moss" }),
            ("Basic · particle (500)", new[] { "trail-mist" }),
            ("Special · gradients (900)", new[] { "trail-harvest", "trail-cider", "trail-dusk-fog" }),
            ("Special · particles (1000-1500)", new[] { "trail-leaf-fall", "trail-acorns", "trail-pumpkin-patch", "trail-autumn-rain", "trail-lanterns", "trail-ember-rise" }),
            ("Special · ribbons (1300)", new[] { "trail-cinnamon", "trail-first-frost" }),
            ("Premium · particle (2600)", new[] { "trail-golden-hour" }),
            ("Premium · ribbons and smooth (2800-3000)", new[] { "trail-bonfire", "trail-harvest-moon", "trail-maple-storm" }),
        };

        private static readonly HashSet<string> autumn_ids = autumn_groups.SelectMany(g => g.ids).ToHashSet();

        /// <summary>Of the current trails, the ones that stay on sale (with new prices). Rainbow
        /// (Engined) also gets a low rotation weight: it shows up less often.</summary>
        private static readonly string[] kept_ids = { "trail-rainbow-engined", "trail-galaxy", "trail-wisp", "trail-lovestruck" };

        private enum Mode { Autumn, Current, Retiring, Kept, All }

        private Mode mode = Mode.Autumn;

        [BackgroundDependencyLoader]
        private void load()
        {
            Add(new Container
            {
                RelativeSizeAxes = Axes.Both,
                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(14, 14, 20, 255),
                    },
                    new OsuScrollContainer
                    {
                        RelativeSizeAxes = Axes.Both,
                        Child = grid = new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(12),
                            Padding = new MarginPadding(20),
                        },
                    },
                },
            });
        }

        [SetUp]
        public void Setup() => Schedule(setUpSteps);

        private void setUpSteps()
        {
            AddStep($"AUTUMN: candidates ({autumn_ids.Count})", () => { mode = Mode.Autumn; rebuild(); });
            AddStep($"NOW: in the store ({currentIds().Count})", () => { mode = Mode.Current; rebuild(); });
            AddStep($"RETIRING ({currentIds().Count - kept_ids.Length})", () => { mode = Mode.Retiring; rebuild(); });
            AddStep($"KEPT ({kept_ids.Length})", () => { mode = Mode.Kept; rebuild(); });
            AddStep("all together", () => { mode = Mode.All; rebuild(); });
            AddStep("normal speed", () => { speed = 1f; rebuild(); });
            AddStep("fast speed", () => { speed = 2.2f; rebuild(); });
            AddStep("slow speed", () => { speed = 0.45f; rebuild(); });
            AddStep("light background", () => { background.Colour = new Color4(236, 236, 242, 255); rebuild(); });
            AddStep("dark background", () => { background.Colour = new Color4(14, 14, 20, 255); rebuild(); });
        }

        /// <summary>Everything on sale today: the whole catalogue minus the new candidates.</summary>
        private static List<string> currentIds()
            => CosmeticCatalog.Trails.Where(t => !autumn_ids.Contains(t.Id)).Select(t => t.Id).ToList();

        private void rebuild()
        {
            grid.Clear();

            switch (mode)
            {
                case Mode.Autumn:
                    foreach (var (header, ids) in autumn_groups)
                    {
                        grid.Add(this.header(header, new Color4(224, 122, 47, 255)));
                        foreach (string id in ids)
                            grid.Add(cell(id, "NEW", new Color4(120, 180, 90, 255)));
                    }

                    break;

                case Mode.Current:
                    addByTier(currentIds(), null, Color4.Transparent);
                    break;

                case Mode.Retiring:
                    grid.Add(header("Temporarily removed from the store (they come back later; owners keep them)", new Color4(200, 70, 60, 255)));
                    addByTier(currentIds().Except(kept_ids).ToList(), "RETIRING", new Color4(200, 70, 60, 255));
                    break;

                case Mode.Kept:
                    grid.Add(header("Still on sale, with new prices (Rainbow Engined shows up less often in the rotation)", new Color4(120, 180, 90, 255)));
                    foreach (string id in kept_ids)
                        grid.Add(cell(id, "KEPT", new Color4(120, 180, 90, 255)));
                    break;

                case Mode.All:
                    grid.Add(header("Autumn", new Color4(224, 122, 47, 255)));
                    foreach (string id in autumn_groups.SelectMany(g => g.ids))
                        grid.Add(cell(id, "NEW", new Color4(120, 180, 90, 255)));
                    grid.Add(header("Current", new Color4(143, 163, 184, 255)));
                    foreach (string id in currentIds())
                        grid.Add(cell(id, null, Color4.Transparent));
                    break;
            }
        }

        private void addByTier(List<string> ids, string? tag, Color4 tagColour)
        {
            foreach (var tier in new[] { CosmeticTier.Basic, CosmeticTier.Special, CosmeticTier.Premium })
            {
                var inTier = ids.Where(id => CosmeticCatalog.Trails.First(t => t.Id == id).Tier == tier).ToList();
                if (inTier.Count == 0) continue;

                grid.Add(header($"{tier} ({inTier.Count})", new Color4(143, 163, 184, 255)));
                foreach (string id in inTier)
                    grid.Add(cell(id, tag, tagColour));
            }
        }

        private Drawable header(string text, Color4 colour) => new Container
        {
            // full width = forces a new row in the flow.
            RelativeSizeAxes = Axes.X,
            Height = 34,
            Child = new OsuSpriteText
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Text = text,
                Font = OsuFont.Style.Heading2.With(weight: FontWeight.SemiBold),
                Colour = colour,
            },
        };

        private Drawable cell(string id, string? tag, Color4 tagColour)
        {
            var def = CosmeticCatalog.Trails.FirstOrDefault(t => t.Id == id);

            if (def == null)
            {
                return new Container
                {
                    Size = new Vector2(250, 150),
                    Masking = true,
                    CornerRadius = 8,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = new Color4(90, 20, 25, 255) },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = $"{id}\nnot in the catalogue",
                            Font = OsuFont.Style.Body,
                        },
                    },
                };
            }

            var rarity = CosmeticRarities.Of(def.Id);

            var children = new List<Drawable>
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(255, 255, 255, 12),
                },
                new CosmeticTrailPreview(def, speed)
                {
                    RelativeSizeAxes = Axes.Both,
                },
                new FillFlowContainer
                {
                    Anchor = Anchor.BottomLeft,
                    Origin = Anchor.BottomLeft,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Margin = new MarginPadding(8),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = def.Name,
                            Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold),
                        },
                        new OsuSpriteText
                        {
                            Text = $"{def.Id}  ·  {def.Tier}  ·  {def.Price}  ·  {CosmeticRarities.DisplayName(rarity)}",
                            Font = OsuFont.Style.Caption2,
                            Colour = CosmeticRarities.ColourOf(rarity),
                        },
                    },
                },
            };

            if (tag != null)
                children.Add(pill(tag, tagColour));

            return new Container
            {
                Size = new Vector2(250, 150),
                Masking = true,
                CornerRadius = 8,
                Children = children,
            };
        }

        private static Drawable pill(string text, Color4 colour) => new Container
        {
            Anchor = Anchor.TopRight,
            Origin = Anchor.TopRight,
            Margin = new MarginPadding(8),
            AutoSizeAxes = Axes.Both,
            Masking = true,
            CornerRadius = 4,
            Children = new Drawable[]
            {
                new Box { RelativeSizeAxes = Axes.Both, Colour = colour },
                new OsuSpriteText
                {
                    Text = text,
                    Margin = new MarginPadding { Horizontal = 6, Vertical = 2 },
                    Font = OsuFont.Style.Caption2.With(weight: FontWeight.Bold),
                    Colour = Color4.White,
                },
            },
        };
    }
}
