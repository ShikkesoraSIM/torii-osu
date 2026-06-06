// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Cosmetics;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Graphics.UserEffects.Presets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tests.Visual.UserInterface
{
    /// <summary>
    /// Preview surface for the Torii cosmetics store: every cursor-trail
    /// cosmetic (auto-orbiting a synthetic cursor) plus the Summer seasonal
    /// aura, laid out as shop-style tiles. Sliders tweak length/density live so
    /// we can dial the feel before wiring the real shop.
    /// </summary>
    [TestFixture]
    public partial class TestSceneToriiCosmetics : OsuTestScene
    {
        private Box background = null!;
        private FillFlowContainer grid = null!;
        private readonly List<TrailPreviewTile> trailTiles = new List<TrailPreviewTile>();

        private float lengthMul = 1f;
        private float densityMul = 1f;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Children = new Drawable[]
            {
                background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Color4(18, 18, 26, 255),
                },
                new BasicScrollContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    ScrollbarVisible = false,
                    Child = grid = new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Padding = new MarginPadding(24),
                        Spacing = new Vector2(18),
                        Direction = FillDirection.Full,
                    },
                },
            };

            rebuild();

            AddSliderStep("trail length x", 0.3f, 2.5f, 1f, v =>
            {
                lengthMul = v;
                applyMultipliers();
            });

            AddSliderStep("density x", 0.5f, 2.5f, 1f, v =>
            {
                densityMul = v;
                applyMultipliers();
            });

            AddToggleStep("light background", light =>
                background.Colour = light ? new Color4(225, 225, 232, 255) : new Color4(18, 18, 26, 255));

            AddStep("rebuild", rebuild);
        }

        private void rebuild()
        {
            grid.Clear();
            trailTiles.Clear();

            foreach (var def in CosmeticCatalog.Trails)
            {
                var tile = new TrailPreviewTile(def);
                trailTiles.Add(tile);
                grid.Add(tile);
            }

            // The Summer seasonal aura (earned, not bought) shown alongside so
            // every cosmetic this branch adds is visible in one place.
            grid.Add(new AuraPreviewTile("Summer 2026", "Seasonal aura — earned", new SummerAuraPreset()));

            applyMultipliers();
        }

        private void applyMultipliers()
        {
            foreach (var tile in trailTiles)
                tile.SetMultipliers(lengthMul, densityMul);
        }

        private static Color4 tierColour(CosmeticTier tier) => tier switch
        {
            CosmeticTier.Basic => new Color4(150, 160, 175, 255),
            CosmeticTier.Special => new Color4(95, 190, 255, 255),
            CosmeticTier.Premium => new Color4(255, 205, 90, 255),
            _ => Color4.White,
        };

        private partial class TrailPreviewTile : Container
        {
            private readonly CosmeticTrailDefinition def;
            private Drawable trailDrawable = null!;
            private ICosmeticTrail trail = null!;

            public TrailPreviewTile(CosmeticTrailDefinition def)
            {
                this.def = def;
                Size = new Vector2(300, 165);
                Masking = true;
                CornerRadius = 12;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                trailDrawable = def.Create();
                trail = (ICosmeticTrail)trailDrawable;

                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(26, 26, 36, 255),
                    },
                    trailDrawable,
                    new FillFlowContainer
                    {
                        Direction = FillDirection.Vertical,
                        AutoSizeAxes = Axes.Both,
                        Padding = new MarginPadding(12),
                        Spacing = new Vector2(0, 2),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = def.Name,
                                Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold),
                            },
                            new OsuSpriteText
                            {
                                Text = $"{def.Tier} · {def.Price:N0} pts",
                                Font = OsuFont.GetFont(size: 12, weight: FontWeight.Regular),
                                Colour = tierColour(def.Tier),
                            },
                        },
                    },
                };
            }

            public void SetMultipliers(float lengthMul, float densityMul)
            {
                if (trail == null)
                    return;

                trail.SetLengthMultiplier(lengthMul);
                trail.SetDensityMultiplier(densityMul);
            }

            protected override void Update()
            {
                base.Update();

                if (trail == null || DrawWidth <= 0 || DrawHeight <= 0)
                    return;

                // Orbit a synthetic cursor in a lissajous figure so the trail
                // draws itself continuously without real mouse input.
                float t = (float)(Time.Current / 1000.0);
                var centre = DrawSize / 2;
                float r = Math.Min(DrawSize.X, DrawSize.Y) * 0.30f;
                var p = centre + new Vector2(MathF.Cos(t * 2.1f) * (DrawSize.X * 0.30f),
                                             MathF.Sin(t * 2.7f) * r);
                trail.Drive(ToScreenSpace(p));
            }
        }

        private partial class AuraPreviewTile : Container
        {
            private readonly string title;
            private readonly string subtitle;
            private readonly AuraPreset preset;

            public AuraPreviewTile(string title, string subtitle, AuraPreset preset)
            {
                this.title = title;
                this.subtitle = subtitle;
                this.preset = preset;
                Size = new Vector2(300, 165);
                Masking = true;
                CornerRadius = 12;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                InternalChildren = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(26, 26, 36, 255),
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Children = new Drawable[]
                        {
                            new ParticleAuraEmitter(preset)
                            {
                                RelativeSizeAxes = Axes.Both,
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                // Drawn first (behind the username sample).
                                Depth = 1f,
                            },
                            new OsuSpriteText
                            {
                                Anchor = Anchor.Centre,
                                Origin = Anchor.Centre,
                                Text = "Shikkesora",
                                Font = OsuFont.GetFont(size: 24, weight: FontWeight.SemiBold),
                            },
                        },
                    },
                    new FillFlowContainer
                    {
                        Direction = FillDirection.Vertical,
                        AutoSizeAxes = Axes.Both,
                        Padding = new MarginPadding(12),
                        Spacing = new Vector2(0, 2),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = title,
                                Font = OsuFont.GetFont(size: 18, weight: FontWeight.SemiBold),
                            },
                            new OsuSpriteText
                            {
                                Text = subtitle,
                                Font = OsuFont.GetFont(size: 12, weight: FontWeight.Regular),
                                Colour = new Color4(255, 205, 90, 255),
                            },
                        },
                    },
                };
            }
        }
    }
}
