// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

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
    /// Autumn 2026 store refresh, name colours: the new candidates, the ones on sale today and
    /// the ones that retire. Same NameColourText the store uses.
    /// </summary>
    [TestFixture]
    public partial class TestSceneToriiAutumnNameColours : OsuTestScene
    {
        private FillFlowContainer grid = null!;
        private Box background = null!;
        private bool lightBackground;

        private static readonly string[] autumn_ids =
        {
            "name-maple", "name-amber", "name-moss",
            "name-harvest", "name-dusk", "name-ember", "name-cider", "name-twilight",
            "name-candlelight", "name-smoulder",
        };

        private IReadOnlyList<string> currentIds = autumn_ids;
        private string? tag;
        private Color4 tagColour;

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
                        Colour = new Color4(18, 18, 24, 255),
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
                            Padding = new MarginPadding(24),
                        },
                    },
                },
            });
        }

        [SetUp]
        public void Setup() => Schedule(setUpSteps);

        private void setUpSteps()
        {
            AddStep($"AUTUMN: candidates ({autumn_ids.Length})", () => show(autumn_ids, "NEW", new Color4(120, 180, 90, 255)));
            AddStep($"NOW: on sale ({nowIds().Count})", () => show(nowIds(), null, Color4.Transparent));
            AddStep($"RETIRING ({nowIds().Count})", () => show(nowIds(), "RETIRING", new Color4(200, 70, 60, 255)));
            AddStep("all together", () => show(autumn_ids.Concat(nowIds()).ToList(), null, Color4.Transparent));
            AddStep("dark background", () =>
            {
                lightBackground = false;
                background.Colour = new Color4(18, 18, 24, 255);
                rebuild();
            });
            AddStep("light background", () =>
            {
                lightBackground = true;
                background.Colour = new Color4(232, 232, 238, 255);
                rebuild();
            });
        }

        private static List<string> nowIds()
            => CosmeticNameColourCatalog.Buyable.Where(c => !autumn_ids.Contains(c.Id)).Select(c => c.Id).ToList();

        private void show(IReadOnlyList<string> ids, string? newTag, Color4 colour)
        {
            currentIds = ids;
            tag = newTag;
            tagColour = colour;
            rebuild();
        }

        private void rebuild()
        {
            grid.Clear();
            foreach (string id in currentIds)
                grid.Add(cell(id));
        }

        private Drawable cell(string id)
        {
            var colour = CosmeticNameColourCatalog.Buyable.FirstOrDefault(c => c.Id == id);

            if (colour == null)
            {
                return new Container
                {
                    Size = new Vector2(230, 90),
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

            var rarity = CosmeticRarities.Of(id);

            var children = new List<Drawable>
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = lightBackground ? new Color4(255, 255, 255, 20) : new Color4(255, 255, 255, 10),
                },
                new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    AutoSizeAxes = Axes.Both,
                    Child = new NameColourText(colour, 26f),
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Margin = new MarginPadding { Bottom = 6 },
                    Text = $"{colour.Name}  ·  {id}  ·  {colour.Price}  ·  {CosmeticRarities.DisplayName(rarity)}",
                    Font = OsuFont.Style.Caption2,
                    Colour = lightBackground ? new Color4(70, 70, 80, 255) : new Color4(160, 160, 175, 255),
                },
            };

            if (tag != null)
            {
                children.Add(new Container
                {
                    Anchor = Anchor.TopRight,
                    Origin = Anchor.TopRight,
                    Margin = new MarginPadding(8),
                    AutoSizeAxes = Axes.Both,
                    Masking = true,
                    CornerRadius = 4,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = tagColour },
                        new OsuSpriteText
                        {
                            Text = tag,
                            Margin = new MarginPadding { Horizontal = 6, Vertical = 2 },
                            Font = OsuFont.Style.Caption2.With(weight: FontWeight.Bold),
                            Colour = Color4.White,
                        },
                    },
                });
            }

            return new Container
            {
                Size = new Vector2(230, 90),
                Masking = true,
                CornerRadius = 8,
                Children = children,
            };
        }
    }
}
