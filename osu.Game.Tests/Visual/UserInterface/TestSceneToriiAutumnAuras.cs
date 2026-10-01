// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.Cosmetics;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tests.Visual.UserInterface
{
    /// <summary>
    /// Autumn 2026 store refresh, auras: the new candidates grouped, what is on sale today and
    /// what retires.
    /// </summary>
    /// <remarks>
    /// Second pass after feedback: Leaffall is the entry aura (2500) and Maple Wind the premium
    /// one of the season (4500); each has variants with different extras to pick one from. The
    /// other four ideas stay as alternatives. Rendered through the same path as the game (APIUser
    /// with EquippedAura + UserAuraContainer): a definition that fails to load shows "UNRESOLVED"
    /// in red.
    /// </remarks>
    [TestFixture]
    public partial class TestSceneToriiAutumnAuras : OsuTestScene
    {
        private FillFlowContainer grid = null!;
        private Box background = null!;
        private float nameScale = 1f;
        private bool lightBackground;

        private record Candidate(string Id, string Name, int Price);

        private static readonly (string header, Candidate[] items)[] autumn_groups =
        {
            ("IN THE STORE · Leaffall, the entry aura: the three variants (2500 each)", new[]
            {
                new Candidate("autumn-leaffall", "Leaffall", 2500),
                new Candidate("autumn-leaffall-gust", "Leaffall · Gust", 2500),
                new Candidate("autumn-leaffall-dusk", "Leaffall · Dusk", 2500),
            }),
            ("IN THE STORE · Maple Wind, the premium aura of the season (4500)", new[]
            {
                new Candidate("autumn-maple-wind", "Maple Wind", 4500),
            }),
            ("IN THE STORE · Stardust, existing, goes on sale (10000)", new[]
            {
                new Candidate("stardust", "Stardust", 10000),
            }),
            ("SHELVED · stay in the system, not for sale for now", new[]
            {
                new Candidate("autumn-maple-wind-gust", "Maple Wind · Gust", 0),
                new Candidate("autumn-maple-wind-ember", "Maple Wind · Ember Wind", 0),
                new Candidate("autumn-maple-wind-storm", "Maple Wind · Storm", 0),
                new Candidate("autumn-harvest-moon", "Harvest Moon", 0),
                new Candidate("autumn-bonfire", "Bonfire", 0),
                new Candidate("autumn-lantern-festival", "Lantern Festival", 0),
                new Candidate("autumn-first-rain", "First Rain", 0),
            }),
        };

        private enum Mode { Autumn, Current, Retiring, Single }

        private Mode mode = Mode.Autumn;
        private Candidate? single;

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
                            Spacing = new Vector2(14),
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
            AddStep($"AUTUMN: candidates ({autumn_groups.Sum(g => g.items.Length)})", () => { mode = Mode.Autumn; rebuild(); });
            AddStep("NOW: on sale", () => { mode = Mode.Current; rebuild(); });
            AddStep("RETIRING", () => { mode = Mode.Retiring; rebuild(); });
            AddStep("dark background", () =>
            {
                lightBackground = false;
                background.Colour = new Color4(18, 18, 24, 255);
                rebuild();
            });
            // Additive blending almost vanishes on light backgrounds, and a leaderboard over a
            // bright map is a real case.
            AddStep("light background", () =>
            {
                lightBackground = true;
                background.Colour = new Color4(232, 232, 238, 255);
                rebuild();
            });
            AddStep("small name (leaderboard)", () => { nameScale = 0.75f; rebuild(); });
            AddStep("normal name", () => { nameScale = 1f; rebuild(); });
            AddStep("large name (profile)", () => { nameScale = 1.5f; rebuild(); });

            foreach (var c in autumn_groups.SelectMany(g => g.items))
                AddStep($"only {c.Name}", () => { mode = Mode.Single; single = c; rebuild(); });
        }

        private static IReadOnlyList<Candidate> buyableNow()
            => BuyableAuraCatalog.All.Select(e => new Candidate(e.Id, e.Id, e.Price)).ToList();

        private void rebuild()
        {
            grid.Clear();

            switch (mode)
            {
                case Mode.Autumn:
                    foreach (var (header, items) in autumn_groups)
                    {
                        grid.Add(this.header(header, new Color4(224, 122, 47, 255)));
                        foreach (var c in items)
                            grid.Add(cell(c, "NEW", new Color4(120, 180, 90, 255)));
                    }

                    break;

                case Mode.Current:
                    foreach (var c in buyableNow())
                        grid.Add(cell(c, null, Color4.Transparent));
                    break;

                case Mode.Retiring:
                    grid.Add(header("Temporarily removed from the store (owners keep it)", new Color4(200, 70, 60, 255)));
                    foreach (var c in buyableNow())
                        grid.Add(cell(c, "RETIRING", new Color4(200, 70, 60, 255)));
                    break;

                case Mode.Single:
                    if (single != null)
                        grid.Add(cell(single, "NEW", new Color4(120, 180, 90, 255)));
                    break;
            }
        }

        private Drawable header(string text, Color4 colour) => new Container
        {
            // full width = new row in the flow.
            RelativeSizeAxes = Axes.X,
            Height = 36,
            Child = new OsuSpriteText
            {
                Anchor = Anchor.BottomLeft,
                Origin = Anchor.BottomLeft,
                Text = text,
                Font = OsuFont.Style.Heading2.With(weight: FontWeight.SemiBold),
                Colour = colour,
            },
        };

        private Drawable cell(Candidate c, string? tag, Color4 tagColour)
        {
            var preset = AuraRegistry.GetById(c.Id);

            var user = new APIUser
            {
                Id = 1,
                Username = "Torii Player",
                EquippedAura = c.Id,
            };

            // Default anchor on purpose: UserAuraContainer puts the target in a horizontal flow
            // next to the ornaments, and the flow requires the same X anchor on all children.
            // The cell does the centring.
            var label = new OsuSpriteText
            {
                Text = user.Username,
                Font = OsuFont.Style.Body.With(size: 18 * nameScale, weight: FontWeight.SemiBold),
                Colour = lightBackground ? new Color4(20, 20, 26, 255) : Color4.White,
            };

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
                    // no masking: the aura draws outside the name and that is exactly what we look at.
                    Child = new UserAuraContainer(user, label),
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    Margin = new MarginPadding(8),
                    Text = c.Name,
                    Font = OsuFont.Style.Body.With(weight: FontWeight.SemiBold),
                    Colour = lightBackground ? new Color4(40, 40, 50, 255) : Color4.White,
                },
                new OsuSpriteText
                {
                    Anchor = Anchor.BottomCentre,
                    Origin = Anchor.BottomCentre,
                    Margin = new MarginPadding { Bottom = 6 },
                    Text = preset == null ? $"{c.Id} (UNRESOLVED)" : $"{c.Id}  ·  {c.Price}",
                    Font = OsuFont.Style.Caption2,
                    Colour = preset == null
                        ? new Color4(255, 90, 90, 255)
                        : (lightBackground ? new Color4(70, 70, 80, 255) : new Color4(160, 160, 175, 255)),
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
                // taller than the old gallery: the autumn auras have particles falling from above.
                Size = new Vector2(300, 150),
                Children = children,
            };
        }
    }
}
