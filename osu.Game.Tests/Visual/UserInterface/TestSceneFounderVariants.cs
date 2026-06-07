// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserEffects;
using osu.Game.Graphics.UserEffects.Presets;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Tests.Visual.UserInterface
{
    /// <summary>
    /// Side-by-side comparison of the five Founder-aura design
    /// variants. None of these are registered in
    /// <see cref="AuraRegistry"/> — they're built ad-hoc here so the
    /// reviewer can compare distinct stylistic directions for the
    /// Founder slot without polluting the production registry.
    ///
    /// Once the reviewer picks a winner, the chosen variant's visual
    /// becomes the new <c>FounderAuraPreset</c>, and the other four
    /// preset files can be deleted.
    /// </summary>
    [TestFixture]
    public partial class TestSceneFounderVariants : OsuTestScene
    {
        private string sampleUsername = "Shikkesora";

        // Per-variant tile content. Adding a new variant means
        // appending one row here and dropping a new AuraPreset in the
        // Presets/ folder — that's it.
        private (string label, string tagline, Func<AuraPreset> factory)[] variants =>
            new (string label, string tagline, Func<AuraPreset> factory)[]
            {
                ("Imperial Gold",
                 "Pure 24k. Zero vermillion. Gold sakura + gold-on-gold seals + gold torii flashes.",
                 () => new FounderImperialGoldPreset()),

                ("Sakura Garden",
                 "Dense pink hanami drift with restrained champagne-gold accents (seals + pollen + torii).",
                 () => new FounderSakuraGardenPreset()),

                ("Lacquered Box",
                 "Gold-on-onyx Japanese lacquerware. Sparser particles + drifting gold leaves + koi ripples.",
                 () => new FounderLacqueredBoxPreset()),

                ("Sunrise Pillar",
                 "Dawn-gradient palette. Pink/gold sakura, vertical sun rays, gradient seals — sun behind a torii.",
                 () => new FounderSunrisePillarPreset()),

                ("Crest of Honor",
                 "Largest most-detailed flanking crests + whisper-light particles. Heraldic, distinction-first.",
                 () => new FounderCrestOfHonorPreset()),
            };

        public TestSceneFounderVariants()
        {
            buildLayout();

            AddStep("short username (\"Mash39\")", () =>
            {
                sampleUsername = "Mash39";
                buildLayout();
            });

            AddStep("medium username (\"Shikkesora\")", () =>
            {
                sampleUsername = "Shikkesora";
                buildLayout();
            });

            AddStep("long username (\"FoundingShikkesoraGOAT\")", () =>
            {
                sampleUsername = "FoundingShikkesoraGOAT";
                buildLayout();
            });

            AddStep("rebuild (restart all emitters)", buildLayout);
        }

        private void buildLayout()
        {
            Clear();

            // Dark backplate so warm-gold particles read with proper
            // contrast — mirrors the dark profile-overlay panel the
            // username sits against in production.
            Add(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Colour4(0.07f, 0.06f, 0.08f, 1f),
            });

            var flow = new FillFlowContainer
            {
                Anchor = Anchor.TopCentre,
                Origin = Anchor.TopCentre,
                AutoSizeAxes = Axes.Y,
                RelativeSizeAxes = Axes.X,
                Padding = new MarginPadding { Top = 16, Bottom = 24, Horizontal = 24 },
                Spacing = new Vector2(0, 14),
                Direction = FillDirection.Vertical,
                Children = variants.Select(v => (Drawable)new VariantTile(v.label, v.tagline, v.factory(), sampleUsername)).ToArray(),
            };

            Add(new BasicScrollContainer
            {
                RelativeSizeAxes = Axes.Both,
                Child = flow,
            });
        }

        /// <summary>
        /// One cell per variant. Label + tagline in the corner, then
        /// the username centred in the cell with the full aura visual
        /// (glow + persistent background + particle emitter) wrapping
        /// it via <see cref="FounderVariantWrapper"/> (which mirrors
        /// the per-frame sizing logic of <see cref="UserAuraContainer"/>
        /// so seals + emitter resolve against the actual rendered
        /// text bounds, not against the whole tile).
        /// </summary>
        private partial class VariantTile : CompositeDrawable
        {
            private readonly string label;
            private readonly string tagline;
            private readonly AuraPreset preset;
            private readonly string username;

            public VariantTile(string label, string tagline, AuraPreset preset, string username)
            {
                this.label = label;
                this.tagline = tagline;
                this.preset = preset;
                this.username = username;

                RelativeSizeAxes = Axes.X;
                Height = 160;
                Masking = true;
                CornerRadius = 12;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                var background = new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = new Colour4(0.11f, 0.10f, 0.13f, 1f),
                };

                var header = new FillFlowContainer
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    AutoSizeAxes = Axes.Both,
                    Direction = FillDirection.Vertical,
                    Padding = new MarginPadding { Top = 10, Left = 16 },
                    Spacing = new Vector2(0, 3),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = label,
                            Font = OsuFont.GetFont(size: 15, weight: FontWeight.Bold),
                            Colour = new Colour4(0.95f, 0.92f, 0.86f, 1f),
                        },
                        new OsuSpriteText
                        {
                            Text = tagline,
                            Font = OsuFont.GetFont(size: 11),
                            Colour = new Colour4(0.65f, 0.62f, 0.60f, 1f),
                        },
                    },
                };

                var usernameText = new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = username,
                    Font = OsuFont.GetFont(size: 26, weight: FontWeight.SemiBold),
                    Colour = Colour4.White,
                };

                // Centre the wrapped username + aura in the tile.
                var wrapper = new FounderVariantWrapper(usernameText, preset)
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };

                InternalChildren = new Drawable[]
                {
                    background,
                    header,
                    wrapper,
                };
            }
        }

        /// <summary>
        /// Drop-in replacement for <see cref="UserAuraContainer"/> that
        /// takes an explicit <see cref="AuraPreset"/> instead of
        /// resolving from an <c>APIUser</c> via
        /// <see cref="AuraRegistry"/>. Used here so we can render
        /// variant presets that aren't registered in production.
        ///
        /// Sizing + composition strategy is copied verbatim from
        /// <c>UserAuraContainer.rebuildEmitter</c> + its per-frame
        /// <c>Update</c>:
        ///   * Wrapper auto-sizes to the target.
        ///   * Glow (if the preset opts in) drawn under everything,
        ///     positioned to align with target's TopLeft.
        ///   * Emitter anchored TopLeft, sized per-frame to either
        ///     the glow mirror's DrawSize (when present) or the
        ///     wrapper's own DrawSize.
        ///   * Target drawn last so it sits above the emitter.
        /// </summary>
        private partial class FounderVariantWrapper : Container
        {
            private readonly SpriteText target;
            private readonly AuraPreset preset;

            private ParticleAuraEmitter emitter = null!;
            private TextShapeGlow? textGlow;
            private FillFlowContainer? targetFlow;

            public FounderVariantWrapper(SpriteText target, AuraPreset preset)
            {
                this.target = target;
                this.preset = preset;
                AutoSizeAxes = Axes.Both;
            }

            [BackgroundDependencyLoader]
            private void load()
            {
                if (preset.GlowColour is Color4 glowColour)
                {
                    Add(textGlow = new TextShapeGlow(target.Text, target.Font, glowColour)
                    {
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Position = new Vector2(-TextShapeGlow.GlowPadding),
                        BypassAutoSizeAxes = Axes.Both,
                    });
                }

                // Decide layout up front so the emitter knows to skip
                // its CreateBackground call when inline ornaments will
                // render the same content (avoids the duplicate seals
                // issue — same gating as UserAuraContainer).
                Drawable? leading = preset.CreateLeadingOrnament();
                Drawable? trailing = preset.CreateTrailingOrnament();
                bool useInlineFlow = leading != null || trailing != null;

                Add(emitter = new ParticleAuraEmitter(preset, includeBackground: !useInlineFlow)
                {
                    Anchor = Anchor.TopLeft,
                    Origin = Anchor.TopLeft,
                    BypassAutoSizeAxes = Axes.Both,
                });

                // Mirror UserAuraContainer's inline-ornament path so the
                // variant test scene shows the same layout production
                // will: when the preset declares leading / trailing
                // ornaments, wrap the target in a horizontal flow so
                // seals + name + seals lay out as a single auto-sized
                // unit. When no ornaments, fall back to direct child.
                if (useInlineFlow)
                {
                    var flowChildren = new System.Collections.Generic.List<Drawable>();
                    if (leading != null) flowChildren.Add(leading);
                    flowChildren.Add(target);
                    if (trailing != null) flowChildren.Add(trailing);

                    targetFlow = new FillFlowContainer
                    {
                        Direction = FillDirection.Horizontal,
                        AutoSizeAxes = Axes.Both,
                        Spacing = new Vector2(2, 0),
                        Anchor = Anchor.TopLeft,
                        Origin = Anchor.TopLeft,
                        Children = flowChildren.ToArray(),
                    };
                    Add(targetFlow);
                }
                else
                {
                    Add(target);
                }
            }

            protected override void Update()
            {
                base.Update();

                if (emitter == null) return;

                Vector2 spawnSize = textGlow?.Mirror.DrawSize ?? DrawSize;
                if (spawnSize.X <= 0 || spawnSize.Y <= 0) return;

                // Track target's actual position (flow may have shifted
                // it right of any leading ornament). Glow + emitter
                // align to that so the text-shape blur / particle
                // spawn area stays on the text, not on the seal.
                Vector2 textOffset = targetFlow != null
                    ? targetFlow.Position + target.Position
                    : Vector2.Zero;

                if (textGlow != null)
                    textGlow.Position = textOffset - new Vector2(TextShapeGlow.GlowPadding);

                if (Math.Abs(emitter.Position.X - textOffset.X) > 0.5f
                    || Math.Abs(emitter.Position.Y - textOffset.Y) > 0.5f)
                {
                    emitter.Position = textOffset;
                }

                // Sub-pixel tolerance — avoid invalidating emitter
                // layout on every imperceptible jitter.
                if (Math.Abs(emitter.Size.X - spawnSize.X) > 0.5f
                    || Math.Abs(emitter.Size.Y - spawnSize.Y) > 0.5f)
                {
                    emitter.Size = spawnSize;
                }
            }
        }
    }
}
