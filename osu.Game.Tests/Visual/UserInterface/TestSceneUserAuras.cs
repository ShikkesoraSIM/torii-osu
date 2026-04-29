// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Configuration;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Users;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tests.Visual.UserInterface
{
    /// <summary>
    /// Visual catalogue of every <see cref="AuraPreset"/> registered in
    /// <see cref="AuraRegistry"/>. Renders one tile per preset, each tile
    /// containing a sample username wrapped by <see cref="UserAuraContainer"/>
    /// so the rendered effect matches what's drawn in production around real
    /// names.
    ///
    /// Used to:
    ///   - Eyeball-compare auras during tuning (sit them next to each other
    ///     and check that no one preset is dramatically louder than its
    ///     neighbours).
    ///   - Verify a new preset registers correctly without having to log into
    ///     a fake account that owns the right group.
    ///   - Sanity-check that <see cref="OsuSetting.UserAuraEnabled"/> globally
    ///     hides all effects when toggled off.
    ///
    /// Usage: open the visual test browser (osu.Game.Tests project), navigate
    /// to "User Interface › User Auras". Use the AddStep buttons in the
    /// right-hand panel to swap username length, toggle the global setting,
    /// or rebuild the whole grid.
    /// </summary>
    [TestFixture]
    public partial class TestSceneUserAuras : OsuTestScene
    {
        private FillFlowContainer<AuraTile> grid = null!;
        private string sampleUsername = "Shikkesora";

        [Resolved]
        private OsuConfigManager? config { get; set; }

        public TestSceneUserAuras()
        {
            buildGrid();

            // Toggle the global aura setting — same Bindable the production
            // UserAuraContainer reads, so this proves the kill-switch path.
            AddToggleStep("global aura setting (UserAuraEnabled)", v =>
            {
                config?.SetValue(OsuSetting.UserAuraEnabled, v);
            });

            AddStep("short username (\"Mash39\")", () =>
            {
                sampleUsername = "Mash39";
                buildGrid();
            });

            AddStep("medium username (\"Shikkesora\")", () =>
            {
                sampleUsername = "Shikkesora";
                buildGrid();
            });

            AddStep("long username (\"VeryLongUserName2026\")", () =>
            {
                sampleUsername = "VeryLongUserName2026";
                buildGrid();
            });

            AddStep("rebuild grid (restart all emitters)", buildGrid);
        }

        private void buildGrid()
        {
            // Tear down any previous grid so emitters get disposed cleanly —
            // otherwise a "rebuild" leaves orphan particle children behind.
            Clear();

            // Use Add (the public test-scene API) — AddInternal is
            // explicitly forbidden in TestScene and throws "Modifying
            // InternalChildren will cause critical failure. Use Add
            // instead." at runtime.
            Add(new Box
            {
                // Solid near-black background. Auras use additive blending,
                // so on a light background the colours wash out and on a
                // pure black the halos pop too hard — this matches the
                // bg colour the lazer profile overlay uses behind names.
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(20, 20, 28, 255),
            });

            grid = new FillFlowContainer<AuraTile>
            {
                RelativeSizeAxes = Axes.Both,
                Padding = new MarginPadding(20),
                Spacing = new Vector2(16),
                Direction = FillDirection.Full,
            };
            Add(grid);

            foreach (var preset in AuraRegistry.AllPresets)
                grid.Add(new AuraTile(preset, sampleUsername));
        }

        /// <summary>
        /// One cell of the catalogue. Renders the aura behind a sample
        /// username and labels the cell with the aura's id + the API
        /// group identifiers that grant access to it (so the test scene
        /// also serves as a quick visual reference for the OWNS table).
        /// </summary>
        private partial class AuraTile : Container
        {
            private readonly AuraPreset preset;
            private readonly string username;

            public AuraTile(AuraPreset preset, string username)
            {
                this.preset = preset;
                this.username = username;

                Size = new Vector2(280, 140);
                Masking = true;
                CornerRadius = 12;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                // Build a fake user whose Groups contain the first owning-
                // group identifier of the preset. This is the cleanest way
                // to drive UserAuraContainer through its real resolution
                // path (so we exercise everything end-to-end, not just the
                // emitter in isolation).
                APIUser fakeUser = new APIUser
                {
                    Id = -1,
                    Username = username,
                    Groups = new[]
                    {
                        new APIUserGroup
                        {
                            Identifier = preset.OwningGroupIdentifiers[0],
                            Name = preset.OwningGroupIdentifiers[0],
                            Colour = "#888888",
                        },
                    },
                };

                Children = new Drawable[]
                {
                    // Slightly different tile background so neighbouring tiles
                    // are visually separable when scanning the grid.
                    new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = new Color4(28, 28, 38, 255),
                    },
                    // Header — aura id + owning group(s). Small + dim so it
                    // doesn't compete with the actual aura preview.
                    new FillFlowContainer
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Padding = new MarginPadding { Top = 8, Left = 12 },
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Vertical,
                        Spacing = new Vector2(2),
                        Children = new Drawable[]
                        {
                            new OsuSpriteText
                            {
                                Text = preset.AuraId,
                                Font = OsuFont.GetFont(size: 13, weight: FontWeight.SemiBold),
                                Colour = new Color4(220, 220, 230, 255),
                            },
                            new OsuSpriteText
                            {
                                Text = string.Join(", ", preset.OwningGroupIdentifiers),
                                Font = OsuFont.GetFont(size: 10),
                                Colour = new Color4(140, 140, 150, 255),
                            },
                        },
                    },
                    // The aura itself — wrap a sample username with the
                    // production container so the rendered output matches
                    // exactly what users see in their profile/overlays.
                    new Container
                    {
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        AutoSizeAxes = Axes.Both,
                        Y = 8, // bias slightly down so the label has clear air above
                        Child = UserAuraContainer.Wrap(fakeUser, new OsuSpriteText
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Text = username,
                            Font = OsuFont.GetFont(size: 22, weight: FontWeight.SemiBold),
                            Colour = Color4.White,
                        }),
                    },
                };
            }
        }
    }
}
